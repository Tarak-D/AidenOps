using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Evaluation;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Infrastructure.EfCore;

/// <summary>EF Core DbContext for audit and evaluation persistence.</summary>
public sealed class AIOpsDbContext : DbContext
{
    public AIOpsDbContext(DbContextOptions<AIOpsDbContext> options) : base(options) { }

    public DbSet<AuditRecordEntity> AuditRecords { get; set; }
    public DbSet<EvaluationRecordEntity> EvaluationRuns { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AuditRecordEntity>(entity =>
        {
            entity.ToTable("audit_records");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CorrelationId).HasColumnName("correlation_id");
            entity.Property(e => e.ActorType).HasColumnName("actor_type");
            entity.Property(e => e.ActorId).HasColumnName("actor_id").HasMaxLength(200);
            entity.Property(e => e.EventType).HasColumnName("event_type").HasMaxLength(100);
            entity.Property(e => e.EntityType).HasColumnName("entity_type").HasMaxLength(100);
            entity.Property(e => e.EntityId).HasColumnName("entity_id").HasMaxLength(200);
            entity.Property(e => e.PayloadJson).HasColumnName("payload_json");
            entity.Property(e => e.OccurredAt).HasColumnName("occurred_at");
            entity.Property(e => e.Sequence).HasColumnName("sequence");
            entity.HasIndex(e => e.EntityType);
            entity.HasIndex(e => e.EntityId);
            entity.HasIndex(e => e.ActorId);
            entity.HasIndex(e => e.OccurredAt);
        });

        modelBuilder.Entity<EvaluationRecordEntity>(entity =>
        {
            entity.ToTable("evaluation_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ExperimentId).HasColumnName("experiment_id").HasMaxLength(200);
            entity.Property(e => e.ModelType).HasColumnName("model_type").HasMaxLength(50);
            entity.Property(e => e.ModelName).HasColumnName("model_name").HasMaxLength(200);
            entity.Property(e => e.PromptVersion).HasColumnName("prompt_version").HasMaxLength(200);
            entity.Property(e => e.DatasetName).HasColumnName("dataset_name").HasMaxLength(200);
            entity.Property(e => e.DatasetVersion).HasColumnName("dataset_version").HasMaxLength(50);
            entity.Property(e => e.ConfigJson).HasColumnName("config_json");
            entity.Property(e => e.SampleCount).HasColumnName("sample_count");
            entity.Property(e => e.StartedAt).HasColumnName("started_at");
            entity.Property(e => e.FinishedAt).HasColumnName("finished_at");
            entity.Property(e => e.MetricsJson).HasColumnName("metrics_json");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.HasIndex(e => e.ExperimentId);
            entity.HasIndex(e => e.ModelType);
            entity.HasIndex(e => e.StartedAt);
        });
    }
}