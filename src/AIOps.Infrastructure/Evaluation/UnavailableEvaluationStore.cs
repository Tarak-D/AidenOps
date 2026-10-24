using AIOps.Abstractions.Evaluation;

namespace AIOps.Infrastructure.Evaluation;

public sealed class UnavailableEvaluationStore : IEvaluationStore
{
    public Task RecordRunAsync(
        ExperimentRunRecord run,
        CancellationToken ct = default) =>
        throw new EvaluationStoreUnavailableException(
            "Evaluation run persistence is unavailable because PostgreSQL is not configured.");

    public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
        string? experimentId = null,
        string? datasetName = null,
        CancellationToken ct = default) =>
        throw new EvaluationStoreUnavailableException(
            "Evaluation run persistence is unavailable because PostgreSQL is not configured.");
}
