namespace AIOps.Abstractions.Evaluation;

/// <summary>
/// Persistence for AI/ML experiment runs (LLM agents, classical ML baselines).
/// Implemented with EF Core in Phase 12-13; contract defined now.
/// </summary>
public sealed record ExperimentRunRecord(
    Guid Id,
    string ExperimentId,
    string ModelType,        // "classical_ml" | "llm"
    string ModelName,        // "tfidf-logreg-v1" | "moonshotai/kimi-k3" | ...
    string? PromptVersion,
    string DatasetName,
    string DatasetVersion,
    string ConfigJson,
    int SampleCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    string MetricsJson,
    string? Notes);

public interface IEvaluationStore
{
    Task RecordRunAsync(ExperimentRunRecord run, CancellationToken ct = default);
    Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
        string? experimentId = null, string? datasetName = null, CancellationToken ct = default);
}
