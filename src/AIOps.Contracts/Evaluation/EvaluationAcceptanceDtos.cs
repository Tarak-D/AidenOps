namespace AIOps.Contracts.Evaluation;

public sealed record EvaluationAcceptanceRequest(
    string? BaselineExperimentId,
    string? CandidateExperimentId);

public sealed record EvaluationAcceptanceIssueDto(
    string Code,
    string Message,
    string? MetricName);

public sealed record EvaluationMetricAcceptanceDto(
    string MetricName,
    string Direction,
    decimal? BaselineValue,
    decimal? CandidateValue,
    decimal? Regression,
    decimal AllowedRegression,
    decimal? ThresholdViolation,
    string Status);

public sealed record EvaluationAcceptanceResponse(
    Guid ResultId,
    DateTimeOffset EvaluatedAt,
    string BaselineExperimentId,
    string CandidateExperimentId,
    Guid? BaselineRunId,
    Guid? CandidateRunId,
    string PolicyName,
    string Outcome,
    IReadOnlyList<EvaluationAcceptanceIssueDto> Issues,
    IReadOnlyList<EvaluationMetricAcceptanceDto> Metrics);
