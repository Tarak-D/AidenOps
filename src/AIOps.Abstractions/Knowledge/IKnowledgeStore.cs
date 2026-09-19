namespace AIOps.Abstractions.Knowledge;

public interface IKnowledgeStore
{
    Task SaveDocumentAsync(
        KnowledgeDocument document,
        IReadOnlyList<KnowledgeChunk> chunks,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<KnowledgeSearchResult>> SearchAsync(
        string query,
        int limit = 5,
        CancellationToken cancellationToken = default);
}