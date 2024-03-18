using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Helpdesk.Api.Incidents.GetIncidentDetails;
using Marten;
using Marten.Events;
using Marten.Events.Aggregation;
using Marten.Events.Daemon;
using Marten.Events.Daemon.Coordination;
using Marten.Events.Projections;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.HttpLogging;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Xunit;
using Xunit.Abstractions;

namespace Helpdesk.Api.Tests.Incidents.Fixtures;

public class CustomApiWithLoggedIncident : CustomApiSpecification<Program>, IAsyncLifetime
{
    public ITestOutputHelper? TestOutputHelper { get; set; }

    public override async Task InitializeAsync()
    {
        await base.InitializeAsync();
        Incident = await this.LoggedIncident();
    }

    public async Task WaitForProjectionAsync<TProjection>(string? projectionName = null)
    {
        var store = Services.GetRequiredService<IDocumentStore>();
        var projectionCoordinator = Services.GetRequiredService<IProjectionCoordinator>();

        var allRegisteredProjections = FetchRegisteredProjections(store).ToList();
        var (waitForProjectionShard, _) = allRegisteredProjections
            .FirstOrDefault(x =>
                x.ProjectionType == typeof(TProjection)
                && (projectionName == null ||
                    string.Equals(x.Shard.Name.ProjectionName, projectionName, StringComparison.Ordinal))
            );


        await StartProjectionsIfNotRunningAsync(projectionCoordinator, store);
        var ready = false;
        var attempts = 0;
        while (!ready)
        {
            var sequence = (await store.Advanced.FetchEventStoreStatistics()).EventSequenceNumber;
            var allProgress = await store.Advanced.AllProjectionProgress();

            ready =
                allProgress.Any(progress =>
                    string.Equals(progress.ShardName, waitForProjectionShard.Name.Identity, StringComparison.Ordinal)
                    && progress.Sequence >= sequence);

            if (!ready)
            {
                await WaitForProjectionShardToBeRunningWithoutExceptionsAsync(projectionCoordinator, store,
                    waitForProjectionShard.Name, TimeSpan.FromSeconds(5));
                await WaitForProjectionShardToReachSequenceWithoutExceptionsAsync(projectionCoordinator, store,
                    waitForProjectionShard.Name, sequence, TimeSpan.FromSeconds(5));
                if (++attempts > 20)
                {
                    var helpfulMessageForDeveloper = new StringBuilder();
                    helpfulMessageForDeveloper
                        .AppendLine(CultureInfo.InvariantCulture,
                            $"It took too long to synchronise the projection {typeof(TProjection)} {projectionName} to event sequence {sequence}.")
                        .AppendLine()
                        .AppendLine(allProgress.Any()
                            ? "The Daemon found the following projections:"
                            : "The Daemon found NO projections yet. This could be because it took too long to restart.");

                    foreach (var p in allProgress)
                    {
                        helpfulMessageForDeveloper.AppendLine(CultureInfo.InvariantCulture,
                            $"Projection {p.ShardName} = {p.Sequence}");
                    }

                    throw new ApplicationException(helpfulMessageForDeveloper.ToString());
                }
            }
        }
    }

    private static async Task WaitForProjectionShardToReachSequenceWithoutExceptionsAsync(
        IProjectionCoordinator projectionCoordinator,
        IDocumentStore store,
        ShardName shardName,
        long sequence,
        TimeSpan timeout)
    {
        try
        {
            var databases = await store.Storage.AllDatabases();

            foreach (var database in databases)
            {
                var daemon = await projectionCoordinator.DaemonForDatabase(database.Identifier);
                await daemon.Tracker.WaitForShardState(shardName, sequence, timeout);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or TaskCanceledException)
        {
            // noop
        }
    }

    private static async Task WaitForProjectionShardToBeRunningWithoutExceptionsAsync(
        IProjectionCoordinator projectionCoordinator,
        IDocumentStore store,
        ShardName shardName,
        TimeSpan timeout)
    {
        try
        {
            var databases = await store.Storage.AllDatabases();

            foreach (var database in databases)
            {
                var daemon = await projectionCoordinator.DaemonForDatabase(database.Identifier);
                await daemon.WaitForShardToBeRunning(shardName.Identity, timeout);
            }
        }
        catch (Exception ex) when (ex is TimeoutException or TaskCanceledException)
        {
            // noop
        }
    }

    private static async Task StartProjectionsIfNotRunningAsync(IProjectionCoordinator projectionCoordinator,
        IDocumentStore store)
    {
        var databases = await store.Storage.AllDatabases();

        foreach (var database in databases)
        {
            var daemon = await projectionCoordinator.DaemonForDatabase(database.Identifier);
            if (daemon is { IsRunning: false })
            {
                await daemon.StartAllAsync();
            }
        }
    }

    private static (AsyncProjectionShard Shard, Type ProjectionType)[] FetchRegisteredProjections(
        IDocumentStore store)
    {
        static Type? GetProjectionType(IProjectionSource source)
        {
            return source switch
            {
                IAggregateProjection aggregatedProjection => aggregatedProjection.AggregateType,
                IReadOnlyProjectionData eventProjection => UnwrapScopedProjectionWrapperIfNeeded(eventProjection
                    .ProjectionType),
                _ => throw new InvalidOperationException(
                    $"Projection type {source.GetType()} is not supported")
            };

            static Type UnwrapScopedProjectionWrapperIfNeeded(Type projectionType)
            {
                // assumption is our projection type is always concrete
                // unless it is a wrapper
                if (projectionType.IsGenericType)
                {
                    var genericArguments = projectionType.GetGenericArguments();
                    return genericArguments.Length == 1
                           && genericArguments[0].IsAssignableTo(typeof(IProjection))
                        ? genericArguments[0]
                        : projectionType;
                }

                return projectionType;
            }
        }

        var allRegisteredAsyncProjections =
            (store as DocumentStore)?.Options.Projections.AllShards() ?? [];

        var registeredWithUnderlyingTypes =
            allRegisteredAsyncProjections
                .Select(x => (Shard: x,
                    ProjectionType: GetProjectionType(x.Source)))
                .ToArray();

        return registeredWithUnderlyingTypes;
    }

    public override void ConfigureWebHostBuilder(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
            services.AddLogging()
                .AddHttpLogging(
                    logging =>
                    {
                        logging.LoggingFields = HttpLoggingFields.All;
                        logging.RequestHeaders.Add("authorization");
                        logging.RequestBodyLogLimit = 4096;
                        logging.ResponseBodyLogLimit = 4096;
                    }));

        builder.ConfigureLogging(
            loggingBuilder =>
            {
                loggingBuilder.ClearProviders();
                loggingBuilder.Services.RemoveAll<ILoggerFactory>();
                loggingBuilder.SetMinimumLevel(LogLevel.Debug);
                loggingBuilder.Services.AddSingleton<ILoggerFactory>(
                    _ => new XUnitLoggerFactory(() => TestOutputHelper));
                loggingBuilder.Services.AddSingleton<ILoggerProvider>(
                    _ => new XUnitLoggerProvider(() => TestOutputHelper));
            });
    }


    public IncidentDetails Incident { get; protected set; } = default!;
    public override Task DisposeAsync() => Task.CompletedTask;

    public async Task<IReadOnlyList<IEvent>> FetchEvents(Guid streamId)
    {
        var store = Services.GetRequiredService<IDocumentStore>();
        var events = await store.QuerySession().Events.FetchStreamAsync(streamId);
        return events;
    }
}