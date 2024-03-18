using System;
using System.Linq;
using System.Threading.Tasks;
using Helpdesk.Api.Incidents;
using Helpdesk.Api.Incidents.GetIncidentDetails;
using Helpdesk.Api.Tests.Incidents.Fixtures;
using Xunit;
using Xunit.Abstractions;
using static Ogooreck.API.ApiSpecification;

namespace Helpdesk.Api.Tests.Incidents;

public class AsyncSnapshotProjectionBugTests
    : IClassFixture<CustomApiWithLoggedIncident>
{
    private readonly CustomApiWithLoggedIncident _api;

    [Fact]
    public async Task CategoriseCommand_GeneratesProperAsyncSnapshot()
    {
        await _api
            .Given()
            .When(
                POST,
                URI($"/api/agents/{agentId}/incidents/{_api.Incident.Id}/category"),
                BODY(new CategoriseIncidentRequest(category)),
                HEADERS(IF_MATCH(1))
            )
            .Then(OK);

        
        
        // asserting that all events are present
        var events = await _api.FetchEvents(_api.Incident.Id);
        Assert.True(events.Count == 2, "Incident events count is not 2");
        Assert.True(events.Any(x => x.EventType == typeof(IncidentLogged)), "IncidentLogged event is not found");
        Assert.True(events.Any(x => x.EventType == typeof(IncidentCategorised)), "IncidentCategorised event is not found");
        
        // waiting for projection
        await _api.WaitForProjectionAsync<IncidentDetailsSnapshotAsyncProjection>();

        await _api
            .Given()
            .When(GET, URI($"/api/incidents/{_api.Incident.Id}/aggregate"))
            .Then(
                OK,
                RESPONSE_BODY(
                    new IncidentDetailsSnapshotAsyncProjection
                    {
                        Id = _api.Incident.Id,
                        Aggregated = _api.Incident with { Category = category } // we expect the category to be in the snapshot
                    }
                )
            );
    }

    private readonly Guid agentId = Guid.NewGuid();
    private readonly IncidentCategory category = IncidentCategory.Database;

    public AsyncSnapshotProjectionBugTests(CustomApiWithLoggedIncident api, ITestOutputHelper testOutputHelper)
    {
        _api = api;
        _api.TestOutputHelper = testOutputHelper;
    }
}