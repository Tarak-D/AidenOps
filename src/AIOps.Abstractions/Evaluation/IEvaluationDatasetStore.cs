namespace AIOps.Abstractions.Evaluation;

public interface IEvaluationDatasetStore
{
    Task SaveAsync(
        EvaluationDatasetMetadata metadata,
        EvaluationDataset dataset,
        CancellationToken cancellationToken = default);

    Task<EvaluationDatasetRecord?> GetAsync(
        string datasetId,
        string version,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<EvaluationDatasetRecord>> ListAsync(
        string? datasetId = null,
        CancellationToken cancellationToken = default);
}

public sealed record EvaluationDatasetRecord(
    EvaluationDatasetMetadata Metadata,
    EvaluationDataset Dataset);