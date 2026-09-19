using Pgvector;

namespace AIOps.Domain.Entities;

public sealed class KnowledgeChunkEntity
{
    public Guid Id { get; set; }

    public Guid DocumentId { get; set; }

    public int ChunkIndex { get; set; }

    public string Content { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public Vector Embedding { get; set; } = null!;

    public DateTime CreatedAt { get; set; }

    public KnowledgeDocumentEntity Document { get; set; } = null!;
}