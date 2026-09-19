namespace AIOps.Domain.Entities;

public sealed class KnowledgeDocumentEntity
{
    public Guid Id { get; set; }

    public string Source { get; set; } = string.Empty;

    public string Title { get; set; } = string.Empty;

    public string Content { get; set; } = string.Empty;

    public string? MetadataJson { get; set; }

    public DateTime CreatedAt { get; set; }

    public ICollection<KnowledgeChunkEntity> Chunks { get; set; } =
        new List<KnowledgeChunkEntity>();
}