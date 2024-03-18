using System;
using System.Threading.Tasks;
using Helpdesk.Api.Incidents.GetIncidentDetails;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Ogooreck.API;
using Xunit;

namespace Helpdesk.Api.Tests.Incidents.Fixtures;

public abstract class CustomApiSpecification<TProgram>(WebApplicationFactory<TProgram> applicationFactory)
    : ApiSpecification<TProgram>(applicationFactory), IAsyncLifetime
    where TProgram : class
{
    private WebApplicationFactory<TProgram> ApplicationFactory { get; set; } = applicationFactory;

    protected IServiceProvider Services => ApplicationFactory.Services;

    protected CustomApiSpecification() : this(new WebApplicationFactory<TProgram>())
    {
    }

    public virtual Task InitializeAsync()
    {
        ApplicationFactory = ApplicationFactory.WithWebHostBuilder(ConfigureWebHostBuilder);
        return Task.CompletedTask;
    }

    public abstract void ConfigureWebHostBuilder(IWebHostBuilder builder);

    public virtual Task DisposeAsync() => Task.CompletedTask;
}