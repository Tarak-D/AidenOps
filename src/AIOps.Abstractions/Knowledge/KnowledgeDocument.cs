namespace AIOps.Abstractions.Knowledge;

public sealed record KnowledgeDocument(
    Guid Id,
    string Source,
    string Title,
    string Content,
    string? MetadataJson = null);