using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Grains;
using AIOps.Abstractions.Persistence;
using AIOps.Contracts.Api;
using AIOps.Domain;
using Microsoft.Extensions.Logging;
using Orleans;

namespace AIOps.Orchestration.Tickets;

/// <summary>
/// Application service for the ticket lifecycle. Coordinates the ticket grain
/// (authoritative state), the append-only audit store, and the read-side repository.
/// Keeps HTTP controllers free of business rules.
/// </summary>
public sealed class TicketService(
    IClusterClient cluster,
    IAuditStore audit,
    ITicketRepository repository,
    ILogger<TicketService> logger)
{
    public async Task<TicketDetailDto> CreateAsync(CreateTicketRequest request,
        string actorId, CancellationToken ct = default)
    {
        var id = Guid.NewGuid();
        var grain = cluster.GetGrain<ITicketGrain>(id);
        var cmd = new CreateTicketCommand(request.Title, request.Description,
            request.ReporterEmail, request.ReporterName, request.Source, request.ExternalRef);
        var state = await grain.Create(cmd);
        await SyncReadModelAsync(id, state, ct);
        await AppendAuditAsync(id, ActorKind.Human, actorId, "TicketCreated", $"Ticket created: {state.Title}");
        return Map(id, state);
    }

    public async Task<TicketDetailDto?> GetAsync(Guid id, CancellationToken ct = default)
    {
        var state = await cluster.GetGrain<ITicketGrain>(id).GetState();
        return state.Exists ? Map(id, state) : null;
    }

    public async Task<TicketDetailDto> TransitionAsync(Guid id, TicketStatus to,
        string reason, string actorId, CancellationToken ct = default)
    {
        var grain = cluster.GetGrain<ITicketGrain>(id);
        var before = (await grain.GetState()).Status;
        using (logger.BeginScope(new Dictionary<string, object> { ["TicketId"] = id }))
        {
            await grain.TransitionTo(to, ActorKind.Human, actorId, reason);
        }
        var state = await grain.GetState();
        await SyncReadModelAsync(id, state, ct);
        await AppendAuditAsync(id, ActorKind.Human, actorId, "TicketStatusChanged",
            $"{before} -> {to}. Reason: {reason}");
        return Map(id, state);
    }

    private async Task SyncReadModelAsync(Guid id, Abstractions.Grains.TicketState s, CancellationToken ct)
    {
        await repository.UpsertSnapshotAsync(id, s.Status, s.Domain, s.Severity,
            s.Title, s.ReporterEmail, s.UpdatedAt, ct);
    }

    private Task AppendAuditAsync(Guid ticketId, ActorKind kind, string actorId, string action, string summary)
        => audit.AppendAsync(new AuditRecordInput(
            CorrelationId: Guid.NewGuid(),
            ActorType: kind,
            ActorId: actorId,
            EventType: action,
            EntityType: "ticket",
            EntityId: ticketId.ToString("N"),
            PayloadJson: System.Text.Json.JsonSerializer.Serialize(new { summary })));

    private static TicketDetailDto Map(Guid id, Abstractions.Grains.TicketState s) => new(
        id, s.ExternalRef, s.Title, s.Description, s.ReporterEmail, s.ReporterName,
        s.Domain, s.Severity, s.Status, s.Source, s.AwaitingApprovalId,
        s.CreatedAt, s.UpdatedAt, s.ResolvedAt,
        s.RecentActivity.Select(a => new TimelineEntryDto(a.OccurredAt, a.ActorKind, a.ActorId, a.Kind, a.Summary)).ToList());
}
