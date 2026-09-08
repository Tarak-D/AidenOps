using AIOps.Domain;
using Orleans;

namespace AIOps.Abstractions.Grains;

/// <summary>
/// Serialiable persistent state of a single ticket. Stored via a named Orleans storage
/// provider ("ticketStore"): in-memory for local dev, ADO.NET/PostgreSQL in deployment
/// (see docs/adr/0003-orleans-persistence.md).
/// </summary>
[GenerateSerializer]
public sealed class TicketState
{
    [Id(0)] public string? ExternalRef { get; set; }
    [Id(1)] public string Title { get; set; } = "";
    [Id(2)] public string Description { get; set; } = "";
    [Id(3)] public string ReporterEmail { get; set; } = "";
    [Id(4)] public string? ReporterName { get; set; }
    [Id(5)] public TicketDomain Domain { get; set; } = TicketDomain.Unknown;
    [Id(6)] public Severity Severity { get; set; } = Severity.P3;
    [Id(7)] public TicketStatus Status { get; set; } = TicketStatus.New;
    [Id(8)] public TicketSource Source { get; set; } = TicketSource.Manual;
    [Id(9)] public DateTimeOffset CreatedAt { get; set; }
    [Id(10)] public DateTimeOffset UpdatedAt { get; set; }
    [Id(11)] public DateTimeOffset? ResolvedAt { get; set; }
    [Id(12)] public Guid? AwaitingApprovalId { get; set; }
    [Id(13)] public List<ActivityEntry> RecentActivity { get; set; } = new();
    [Id(14)] public bool Exists { get; set; }
}

[GenerateSerializer]
public sealed class ActivityEntry
{
    [Id(0)] public DateTimeOffset OccurredAt { get; set; }
    [Id(1)] public ActorKind ActorKind { get; set; }
    [Id(2)] public string ActorId { get; set; } = "";
    [Id(3)] public string Kind { get; set; } = "";
    [Id(4)] public string Summary { get; set; } = "";
}

[GenerateSerializer]
public sealed record CreateTicketCommand(
    [property: Id(0)] string Title,
    [property: Id(1)] string Description,
    [property: Id(2)] string ReporterEmail,
    [property: Id(3)] string? ReporterName,
    [property: Id(4)] TicketSource Source,
    [property: Id(5)] string? ExternalRef);

/// <summary>
/// Orleans virtual actor owning one ticket. Single-writer guarantees that status
/// transitions are validated exactly once, in order — this is why Orleans is here.
/// Implemented in AIOps.Grains (Phase 2).
/// </summary>
public interface ITicketGrain : IGrainWithGuidKey
{
    Task<TicketState> GetState();
    Task<TicketState> Create(CreateTicketCommand command);
    Task TransitionTo(TicketStatus next, ActorKind actorKind, string actorId, string reason);
    Task ApplyTriage(TicketDomain domain, Severity severity, string actorId);
    Task AppendActivity(ActorKind actorKind, string actorId, string kind, string summary);
    Task SetAwaitingApproval(Guid approvalId);
    Task ApprovalResolved(Guid approvalId, ApprovalStatus outcome, string decidedBy);
}
