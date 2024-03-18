using Marten;
using Marten.Services;

namespace Helpdesk.Api.Core.Marten;

using System.Data.Common;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;
using ILogger = Microsoft.Extensions.Logging.ILogger;

public static class MartenEvents
{
    public static readonly EventId SchemaChange = new(100001, "Schema Change");
    public static readonly EventId Success = new(100002, "Success");
    public static readonly EventId Failure = new(100003, "Failure");
    public static readonly EventId SaveChanges = new(100004, "Save Changes");
    public static readonly EventId StartSession = new(100005, "Start Session");
    public static readonly EventId StartBatch = new(100007, "Start Batch");
}

public class MartenLogger(ILogger logger) : IMartenLogger, IMartenSessionLogger
{
    private Stopwatch? _stopwatch;

    private void Log(LogLevel logLevel, EventId eventId, string? message,
        params object?[] args)
    {
        Log(logLevel, eventId, null, message, args);
    }

    protected virtual void Log(LogLevel logLevel, EventId eventId, Exception? exception = null, string? message = null,
        params object?[] args)
    {
#pragma warning disable CA2254
        logger.Log(logLevel, eventId, exception, message, args);
#pragma warning restore CA2254
    }

    public IMartenSessionLogger StartSession(IQuerySession session)
    {
        Log(LogLevel.Debug, MartenEvents.StartSession, "Start Session");
        return this;
    }

    private bool ContainsBuggyProjectionUpsertCall(NpgsqlBatch batch)
    {
        if (batch.BatchCommands.Count > 0)
        {
            foreach (var command in batch.BatchCommands)
            {
                // additional logging for mt_upsert_incidentdetailssnapshotasyncprojection
                if (command.CommandText.Contains("mt_upsert_incidentdetailssnapshotasyncprojection"))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private bool ContainsBuggyProjectionUpsertCall(NpgsqlCommand command)
    {
        if (command.CommandText.Contains("mt_upsert_incidentdetailssnapshotasyncprojection"))
        {
            return true;
        }

        return false;
    }


    public void OnBeforeExecute(NpgsqlBatch batch)
    {
        if (ContainsBuggyProjectionUpsertCall(batch))
            LogTheBatch("SQL Batch starting", batch, MartenEvents.StartBatch, withParameters: true,
                maxJsonParamLength: Int32.MaxValue);
    }

    public void LogSuccess(NpgsqlBatch batch)
    {
        if (ContainsBuggyProjectionUpsertCall(batch))
            LogTheBatch("SQL Batch executed successfully", batch, MartenEvents.Success);
    }

    public void LogFailure(NpgsqlBatch batch, Exception ex)
    {
        if (ContainsBuggyProjectionUpsertCall(batch))
        {
            LogTheBatch("SQL Batch execution failed", batch,
                MartenEvents.Failure, LogLevel.Error, ex, true);
        }
    }

    private IEnumerable<(string Id, NpgsqlDbType Type, string? Value)> PrepareParameters(
        DbParameterCollection dbCommandParameters, int maxJsonLength = 200)
    {
        var parameters = dbCommandParameters.OfType<NpgsqlParameter>().ToArray();
        for (var index = 0; index < parameters.Length; index++)
        {
            var parameter = parameters[index]!;
            var stringValue = parameter.NpgsqlValue?.ToString() ?? parameter.Value?.ToString();
            if (parameter.NpgsqlDbType is NpgsqlDbType.Json or NpgsqlDbType.Jsonb)
            {
                stringValue = stringValue?.Length > maxJsonLength ? stringValue[..maxJsonLength] : stringValue;
            }

            yield return (
                parameter.ParameterName.Length > 0 ? parameter.ParameterName : $"@p{index}",
                parameter.NpgsqlDbType,
                stringValue
            );
        }
    }

    private void LogTheBatch(string baseMessage, NpgsqlBatch batch, EventId eventId,
        LogLevel logLevel = LogLevel.Debug,
        Exception? ex = null,
        bool withParameters = false,
        int maxJsonParamLength = 200)
    {
        var batchId = Guid.NewGuid();

        if (batch.BatchCommands.Count <= 0)
        {
            Log(
                logLevel,
                eventId,
                ex,
                "{BaseMessage} {BatchId} with no sql",
                baseMessage,
                batchId);
        }

        foreach (var batchCommand in batch.BatchCommands)
        {
            if (withParameters)
            {
                Log(
                    logLevel,
                    eventId,
                    ex,
                    "{BaseMessage} {BatchId} {Sql} {Parameters}",
                    baseMessage,
                    batchId,
                    batchCommand.CommandText,
                    PrepareParameters(batchCommand.Parameters, maxJsonParamLength));
            }
            else
            {
                Log(
                    logLevel,
                    eventId,
                    ex,
                    "{BaseMessage} {BatchId} {Sql}",
                    baseMessage,
                    batchId,
                    batchCommand.CommandText);
            }
        }
    }

    public void LogFailure(Exception ex, string message)
    {
        Log(LogLevel.Error,
            MartenEvents.Failure,
            ex,
            "Marten error {Message}",
            message);
    }


    public void SchemaChange(string sql)
    {
        Log(LogLevel.Information, MartenEvents.SchemaChange, "Schema Change {Sql}", sql);
    }

    public void LogSuccess(NpgsqlCommand command)
    {
        if (ContainsBuggyProjectionUpsertCall(command))

        Log(LogLevel.Debug, MartenEvents.Success, "SQL Executed successfully {Sql}", command.CommandText);
    }

    public void LogFailure(NpgsqlCommand command, Exception ex)
    {
        if (ContainsBuggyProjectionUpsertCall(command))
        {
            Log(LogLevel.Error,
                MartenEvents.Failure,
                ex,
                "Marten sql error {Sql} {Parameters}",
                command.CommandText,
                PrepareParameters(command.Parameters));
        }
    }

    public void RecordSavedChanges(IDocumentSession session, IChangeSet commit)
    {
        _stopwatch?.Stop();
        Log(LogLevel.Debug,
            MartenEvents.SaveChanges,
            "Persisted {Count} changes in {Elapsed}",
            commit.Updated.Count(),
            _stopwatch?.Elapsed);
    }

    public void OnBeforeExecute(NpgsqlCommand command)
    {
        _stopwatch = new Stopwatch();
        _stopwatch.Start();
    }
}