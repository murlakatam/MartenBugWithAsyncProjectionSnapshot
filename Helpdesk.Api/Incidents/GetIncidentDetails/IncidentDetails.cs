using Marten.Events.Aggregation;
using Newtonsoft.Json;

namespace Helpdesk.Api.Incidents.GetIncidentDetails;

public record IncidentDetails(
    Guid Id,
    Guid CustomerId,
    IncidentStatus Status,
    IncidentNote[] Notes,
    IncidentCategory? Category = null,
    IncidentPriority? Priority = null,
    Guid? AgentId = null,
    int Version = 1
);

public record IncidentNote(
    IncidentNoteType Type,
    Guid From,
    string Content,
    bool VisibleToCustomer
);

public enum IncidentNoteType
{
    FromAgent,
    FromCustomer
}

public class IncidentDetailsSnapshotAsyncProjection
{
    public Guid Id { get; init; }

    public IncidentDetails Aggregated { get; set; } =
        new(Guid.Empty, Guid.Empty, IncidentStatus.Pending, Array.Empty<IncidentNote>());

    [JsonConstructor]
    public IncidentDetailsSnapshotAsyncProjection()
    {
        Id = Guid.NewGuid();
    }

    public IncidentDetailsSnapshotAsyncProjection(IncidentLogged logged)
    {
        Id = logged.IncidentId;
        Aggregated = new(logged.IncidentId, logged.CustomerId, IncidentStatus.Pending, Array.Empty<IncidentNote>());
    }

    public void Apply(IncidentCategorised categorised) =>
        Aggregated = Aggregated with { Category = categorised.Category };

    public void Apply(IncidentPrioritised prioritised) =>
        Aggregated = Aggregated with { Priority = prioritised.Priority };

    public void Apply(AgentAssignedToIncident prioritised) =>
        Aggregated = Aggregated with { AgentId = prioritised.AgentId };

    public void Apply(AgentRespondedToIncident agentResponded) =>
        Aggregated = Aggregated with
        {
            Notes = Aggregated.Notes.Union(
                new[]
                {
                    new IncidentNote(
                        IncidentNoteType.FromAgent,
                        agentResponded.Response.AgentId,
                        agentResponded.Response.Content,
                        agentResponded.Response.VisibleToCustomer
                    )
                }).ToArray()
        };

    public void Apply(CustomerRespondedToIncident customerResponded) =>
        Aggregated = Aggregated with
        {
            Notes = Aggregated.Notes.Union(
                new[]
                {
                    new IncidentNote(
                        IncidentNoteType.FromCustomer,
                        customerResponded.Response.CustomerId,
                        customerResponded.Response.Content,
                        true
                    )
                }).ToArray()
        };

    public void Apply(IncidentResolved resolved) =>
        Aggregated = Aggregated with { Status = IncidentStatus.Resolved };

    public void Apply(ResolutionAcknowledgedByCustomer acknowledged) =>
        Aggregated = Aggregated with { Status = IncidentStatus.ResolutionAcknowledgedByCustomer };

    public void Apply(IncidentClosed closed) =>
        Aggregated = Aggregated with { Status = IncidentStatus.Closed };
}

public class IncidentDetailsProjection : SingleStreamProjection<IncidentDetails>
{
    public static IncidentDetails Create(IncidentLogged logged) =>
        new(logged.IncidentId, logged.CustomerId, IncidentStatus.Pending, Array.Empty<IncidentNote>());

    public IncidentDetails Apply(IncidentCategorised categorised, IncidentDetails current) =>
        current with { Category = categorised.Category };

    public IncidentDetails Apply(IncidentPrioritised prioritised, IncidentDetails current) =>
        current with { Priority = prioritised.Priority };

    public IncidentDetails Apply(AgentAssignedToIncident prioritised, IncidentDetails current) =>
        current with { AgentId = prioritised.AgentId };

    public IncidentDetails Apply(AgentRespondedToIncident agentResponded, IncidentDetails current) =>
        current with
        {
            Notes = current.Notes.Union(
                new[]
                {
                    new IncidentNote(
                        IncidentNoteType.FromAgent,
                        agentResponded.Response.AgentId,
                        agentResponded.Response.Content,
                        agentResponded.Response.VisibleToCustomer
                    )
                }).ToArray()
        };

    public IncidentDetails Apply(CustomerRespondedToIncident customerResponded, IncidentDetails current) =>
        current with
        {
            Notes = current.Notes.Union(
                new[]
                {
                    new IncidentNote(
                        IncidentNoteType.FromCustomer,
                        customerResponded.Response.CustomerId,
                        customerResponded.Response.Content,
                        true
                    )
                }).ToArray()
        };

    public IncidentDetails Apply(IncidentResolved resolved, IncidentDetails current) =>
        current with { Status = IncidentStatus.Resolved };

    public IncidentDetails Apply(ResolutionAcknowledgedByCustomer acknowledged, IncidentDetails current) =>
        current with { Status = IncidentStatus.ResolutionAcknowledgedByCustomer };

    public IncidentDetails Apply(IncidentClosed closed, IncidentDetails current) =>
        current with { Status = IncidentStatus.Closed };
}