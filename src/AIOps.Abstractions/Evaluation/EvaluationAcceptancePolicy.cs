namespace AIOps.Abstractions.Evaluation;

public enum EvaluationMetricDirection
{
    HigherIsBetter,
    LowerIsBetter
}

public sealed record EvaluationMetricAcceptanceRule(
    string MetricName,
    EvaluationMetricDirection Direction,
    decimal AllowedRegression);

public sealed record EvaluationAcceptancePolicy(
    string Name,
    IReadOnlyList<EvaluationMetricAcceptanceRule> RequiredMetrics)
{
    public void Validate()
    {
        _ = CreateValidatedSnapshot();
    }

    public EvaluationAcceptancePolicy CreateValidatedSnapshot()
    {
        if (string.IsNullOrWhiteSpace(Name))
        {
            throw new ArgumentException(
                "Acceptance policy name is required.",
                nameof(Name));
        }

        ArgumentNullException.ThrowIfNull(RequiredMetrics);

        var rules = RequiredMetrics.ToArray();

        if (rules.Length == 0)
        {
            throw new ArgumentException(
                "At least one required metric must be configured.",
                nameof(RequiredMetrics));
        }

        var metricNames = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        foreach (var rule in rules)
        {
            ArgumentNullException.ThrowIfNull(rule);

            if (string.IsNullOrWhiteSpace(rule.MetricName) ||
                !string.Equals(
                    rule.MetricName,
                    rule.MetricName.Trim(),
                    StringComparison.Ordinal))
            {
                throw new ArgumentException(
                    "Metric names must be non-empty and have no surrounding whitespace.",
                    nameof(RequiredMetrics));
            }

            if (!metricNames.Add(rule.MetricName))
            {
                throw new ArgumentException(
                    $"Metric '{rule.MetricName}' is defined more than once.",
                    nameof(RequiredMetrics));
            }

            if (!Enum.IsDefined(rule.Direction))
            {
                throw new ArgumentException(
                    $"Metric '{rule.MetricName}' has an invalid direction.",
                    nameof(RequiredMetrics));
            }

            if (rule.AllowedRegression < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(RequiredMetrics),
                    $"Metric '{rule.MetricName}' must have a non-negative regression allowance.");
            }
        }

        return this with
        {
            RequiredMetrics = Array.AsReadOnly(rules)
        };
    }
}

public enum EvaluationAcceptanceStatus
{
    Passed,
    Failed,
    Blocked
}

public enum EvaluationMetricAcceptanceStatus
{
    Passed,
    ThresholdExceeded,
    MissingOrInvalid,
    NotEvaluated
}

public enum EvaluationAcceptanceIssueCode
{
    MissingBaseline,
    MissingCandidate,
    AmbiguousBaseline,
    AmbiguousCandidate,
    MissingCompatibilityMetadata,
    IncompatibleRuns,
    MissingOrInvalidMetric,
    ThresholdExceeded
}

public sealed record EvaluationAcceptanceIssue(
    EvaluationAcceptanceIssueCode Code,
    string Message,
    string? MetricName = null);

public sealed record EvaluationMetricAcceptanceOutcome(
    string MetricName,
    EvaluationMetricDirection Direction,
    decimal? BaselineValue,
    decimal? CandidateValue,
    decimal? Regression,
    decimal AllowedRegression,
    decimal? ThresholdViolation,
    EvaluationMetricAcceptanceStatus Status);

public sealed record EvaluationAcceptanceResult(
    string PolicyName,
    string BaselineExperimentId,
    string CandidateExperimentId,
    Guid? BaselineRunId,
    Guid? CandidateRunId,
    EvaluationAcceptanceStatus Status,
    IReadOnlyList<EvaluationAcceptanceIssue> Issues,
    IReadOnlyList<EvaluationMetricAcceptanceOutcome> Metrics);
