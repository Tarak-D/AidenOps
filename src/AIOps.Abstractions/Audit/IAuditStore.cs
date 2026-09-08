using AIOps.Domain;

namespace AIOps.Abstractions.Audit;

/// <summary>
/// One audit event. Audit records are append-only BY DESIGN: this abstraction
/// intentionally exposes no update/delete surface. Persistence-layer interceptors
/// (Phase 3) and DB permissions (Phase 15) provide defense in depth.
/// </summary>
public sealed record AuditRecordInput(
    Guid CorrelationId,
    ActorKind ActorType,
    string ActorId,
    string EventType,
    string EntityType,
    string EntityId,
    string PayloadJson);

public sealed record AuditRecordView(
    long Sequence,
    DateTimeOffset OccurredAt,
    AuditRecordInput Record);

public interface IAuditStore
{
    Task AppendAsync(AuditRecordInput record, CancellationToken ct = default);
    Task<IReadOnlyList<AuditRecordView>> QueryAsync(
        string? entityType = null, string? entityId = null, string? actorId = null,
        int take = 100, CancellationToken ct = default);
}
