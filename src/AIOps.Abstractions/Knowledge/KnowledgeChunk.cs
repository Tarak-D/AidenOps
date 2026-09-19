namespace AIOps.Abstractions.Knowledge;

public sealed record KnowledgeChunk(
    Guid Id,
    Guid DocumentId,
    int ChunkIndex,
    string Content,
    string? MetadataJson = null);