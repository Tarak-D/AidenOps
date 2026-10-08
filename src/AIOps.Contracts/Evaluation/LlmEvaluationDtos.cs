namespace AIOps.Contracts.Evaluation;

public sealed record LlmEvaluationRequest(
    string DatasetId,
    string DatasetName,
    string DatasetVersion,
    string Provider,
    IReadOnlyList<LlmEvaluationCaseRequest> Cases);

public sealed record LlmEvaluationCaseRequest(
    string CaseId,
    string Title,
    string Description,
    string ExpectedDomain,
    string ExpectedSeverity);

public sealed record LlmEvaluationResponse(
    string ModelType,
    LlmEvaluationDatasetResponse Dataset,
    LlmEvaluationModelResponse Model,
    int SampleCount,
    LlmEvaluationMetricsResponse Metrics,
    IReadOnlyList<LlmEvaluationPredictionResponse> Predictions);

public sealed record LlmEvaluationDatasetResponse(
    string Id,
    string Name,
    string Version);

public sealed record LlmEvaluationModelResponse(
    string Provider,
    string Name,
    string PromptVersion);

public sealed record LlmEvaluationMetricsResponse(
    double DomainAccuracy,
    double SeverityAccuracy,
    double TriageAccuracy,
    double AverageConfidence,
    double TotalLatencyMs,
    double AverageLatencyMs,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens);

public sealed record LlmEvaluationPredictionResponse(
    string CaseId,
    string Provider,
    string Model,
    string PromptVersion,
    string Domain,
    string Severity,
    double Confidence,
    bool DomainCorrect,
    bool SeverityCorrect,
    bool TriageCorrect,
    int PromptTokens,
    int CompletionTokens,
    int TotalTokens,
    double LatencyMs);