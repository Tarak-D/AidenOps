using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Evaluation;
using AIOps.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Infrastructure.EfCore;

/// <summary>EF Core DbContext for platform persistence, including Phase 7 knowledge retrieval.</summary>
public sealed class AIOpsDbContext : DbContext
{
    public AIOpsDbContext(DbContextOptions<AIOpsDbContext> options) : base(options) { }

    public DbSet<AuditRecordEntity> AuditRecords { get; set; }

    public DbSet<EvaluationRecordEntity> EvaluationRuns { get; set; }

    public DbSet<ActionExecution> ActionExecutions { get; set; }

    public DbSet<ApprovalRequest> ApprovalRequests { get; set; }

    public DbSet<KnowledgeDocumentEntity> KnowledgeDocuments { get; set; }

    public DbSet<KnowledgeChunkEntity> KnowledgeChunks { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("vector");

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

        modelBuilder.Entity<ActionExecution>(entity =>
        {
            entity.ToTable("action_executions");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.TicketId)
                .HasColumnName("ticket_id");

            entity.Property(e => e.ToolName)
                .HasColumnName("tool_name")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.ArgumentsJson)
                .HasColumnName("arguments_json")
                .IsRequired();

            entity.Property(e => e.Risk)
                .HasColumnName("risk")
                .IsRequired();

            entity.Property(e => e.RequiresApproval)
                .HasColumnName("requires_approval")
                .IsRequired();

            entity.Property(e => e.ProposedBy)
                .HasColumnName("proposed_by")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Reason)
                .HasColumnName("reason");

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .IsRequired();

            entity.Property(e => e.ResultSummary)
                .HasColumnName("result_summary");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.Property(e => e.ExecutedAt)
                .HasColumnName("executed_at");

            entity.HasIndex(e => e.TicketId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.CreatedAt);
        });

        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.ToTable("approval_requests");
            entity.HasKey(e => e.Id);

            entity.Property(e => e.ActionExecutionId)
                .HasColumnName("action_execution_id")
                .IsRequired();

            entity.Property(e => e.TicketId)
                .HasColumnName("ticket_id")
                .IsRequired();

            entity.Property(e => e.RequestedBy)
                .HasColumnName("requested_by")
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(e => e.Justification)
                .HasColumnName("justification")
                .IsRequired();

            entity.Property(e => e.Status)
                .HasColumnName("status")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.Property(e => e.ExpiresAt)
                .HasColumnName("expires_at")
                .IsRequired();

            entity.Property(e => e.DecidedBy)
                .HasColumnName("decided_by")
                .HasMaxLength(200);

            entity.Property(e => e.DecidedAt)
                .HasColumnName("decided_at");

            entity.Property(e => e.DecisionComment)
                .HasColumnName("decision_comment");

            entity.HasIndex(e => e.ActionExecutionId)
                .IsUnique();

            entity.HasIndex(e => e.TicketId);
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.ExpiresAt);
        });

        modelBuilder.Entity<KnowledgeDocumentEntity>(entity =>
{
    entity.ToTable("knowledge_documents");

    entity.HasKey(e => e.Id);

    entity.Property(e => e.Id)
        .HasColumnName("id");

    entity.Property(e => e.Source)
                .HasColumnName("source")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.Title)
                .HasColumnName("title")
                .HasMaxLength(500)
                .IsRequired();

            entity.Property(e => e.Content)
                .HasColumnName("content")
                .IsRequired();

            entity.Property(e => e.MetadataJson)
                .HasColumnName("metadata_json");

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.HasIndex(e => e.Source);

            entity.HasIndex(e => e.Title);
        });

        modelBuilder.Entity<KnowledgeChunkEntity>(entity =>
        {
            entity.ToTable("knowledge_chunks");

            entity.HasKey(e => e.Id);
            entity.Property(e => e.Id)
            .HasColumnName("id");

            entity.Property(e => e.DocumentId)
                .HasColumnName("document_id")
                .IsRequired();

            entity.Property(e => e.ChunkIndex)
                .HasColumnName("chunk_index")
                .IsRequired();

            entity.Property(e => e.Content)
                .HasColumnName("content")
                .IsRequired();

            entity.Property(e => e.MetadataJson)
                .HasColumnName("metadata_json");

            entity.Property(e => e.Embedding)
                .HasColumnName("embedding")
                .HasColumnType("vector(64)")
                .IsRequired();

            entity.Property(e => e.CreatedAt)
                .HasColumnName("created_at")
                .IsRequired();

            entity.HasOne(e => e.Document)
                .WithMany(e => e.Chunks)
                .HasForeignKey(e => e.DocumentId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasIndex(e => new { e.DocumentId, e.ChunkIndex })
                .IsUnique();

            entity.HasIndex(e => e.Embedding)
                .HasMethod("hnsw")
                .HasOperators("vector_cosine_ops");
        });
    }
}