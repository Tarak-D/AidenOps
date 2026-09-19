namespace AIOps.Abstractions.Knowledge;

public sealed record KnowledgeSearchResult(
    Guid ChunkId,
    Guid DocumentId,
    string Source,
    string Title,
    int ChunkIndex,
    string Content,
    double Similarity,
    string? MetadataJson);