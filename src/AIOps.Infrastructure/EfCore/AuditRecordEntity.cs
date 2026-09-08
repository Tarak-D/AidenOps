using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using AIOps.Domain;

namespace AIOps.Infrastructure.EfCore;

/// <summary>EF Core entity for audit records.</summary>
[Table("audit_records")]
public sealed class AuditRecordEntity
{
    [Key, Column("id")]
    public Guid Id { get; set; }

    [Column("correlation_id")]
    public Guid CorrelationId { get; set; }

    [Column("actor_type")]
    public ActorKind ActorType { get; set; }

    [Column("actor_id"), MaxLength(200)]
    public string ActorId { get; set; } = "";

    [Column("event_type"), MaxLength(100)]
    public string EventType { get; set; } = "";

    [Column("entity_type"), MaxLength(100)]
    public string EntityType { get; set; } = "";

    [Column("entity_id"), MaxLength(200)]
    public string EntityId { get; set; } = "";

    [Column("payload_json")]
    public string PayloadJson { get; set; } = "";

    [Column("occurred_at")]
    public DateTimeOffset OccurredAt { get; set; }

    [Column("sequence")]
    public long Sequence { get; set; }
}