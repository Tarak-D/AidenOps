using System.Globalization;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Evaluation;

namespace AIOps.Orchestration.Evaluation;

public sealed class EvaluationAcceptanceGate
{
    private readonly IEvaluationStore _evaluationStore;

    public EvaluationAcceptanceGate(IEvaluationStore evaluationStore)
    {
        ArgumentNullException.ThrowIfNull(evaluationStore);
        _evaluationStore = evaluationStore;
    }

    public async Task<EvaluationAcceptanceResult> EvaluateAsync(
        string baselineExperimentId,
        string candidateExperimentId,
        EvaluationAcceptancePolicy policy,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(baselineExperimentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(candidateExperimentId);
        ArgumentNullException.ThrowIfNull(policy);
        policy = policy.CreateValidatedSnapshot();

        var issues = new List<EvaluationAcceptanceIssue>();

        if (string.Equals(
                baselineExperimentId,
                candidateExperimentId,
                StringComparison.Ordinal))
        {
            issues.Add(new EvaluationAcceptanceIssue(
                EvaluationAcceptanceIssueCode.IncompatibleRuns,
                "Baseline and candidate must be different named experiments."));

            return CreateResult(
                baselineExperimentId,
                candidateExperimentId,
                policy,
                baseline: null,
                candidate: null,
                EvaluationAcceptanceStatus.Blocked,
                issues,
                CreateNotEvaluatedMetrics(policy));
        }

        var baselineRuns = await _evaluationStore.ListRunsAsync(
            baselineExperimentId,
            ct: cancellationToken);
        var candidateRuns = await _evaluationStore.ListRunsAsync(
            candidateExperimentId,
            ct: cancellationToken);

        var baseline = ResolveRun(
            baselineExperimentId,
            baselineRuns,
            isBaseline: true,
            issues);
        var candidate = ResolveRun(
            candidateExperimentId,
            candidateRuns,
            isBaseline: false,
            issues);

        if (baseline is null || candidate is null)
        {
            return CreateResult(
                baselineExperimentId,
                candidateExperimentId,
                policy,
                baseline,
                candidate,
                EvaluationAcceptanceStatus.Blocked,
                issues,
                CreateNotEvaluatedMetrics(policy));
        }

        CheckCompatibility(baseline, candidate, issues);

        if (issues.Count > 0)
        {
            return CreateResult(
                baselineExperimentId,
                candidateExperimentId,
                policy,
                baseline,
                candidate,
                EvaluationAcceptanceStatus.Blocked,
                issues,
                CreateNotEvaluatedMetrics(policy));
        }

        var metrics = EvaluateMetrics(
            baseline,
            candidate,
            policy,
            issues);

        var status = issues.Count == 0
            ? EvaluationAcceptanceStatus.Passed
            : EvaluationAcceptanceStatus.Failed;

        return CreateResult(
            baselineExperimentId,
            candidateExperimentId,
            policy,
            baseline,
            candidate,
            status,
            issues,
            metrics);
    }

    private static ExperimentRunRecord? ResolveRun(
        string experimentId,
        IReadOnlyList<ExperimentRunRecord> runs,
        bool isBaseline,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        if (runs.Count == 0)
        {
            issues.Add(new EvaluationAcceptanceIssue(
                isBaseline
                    ? EvaluationAcceptanceIssueCode.MissingBaseline
                    : EvaluationAcceptanceIssueCode.MissingCandidate,
                $"No run exists for {(isBaseline ? "baseline" : "candidate")} experiment '{experimentId}'."));
            return null;
        }

        if (runs.Count != 1)
        {
            issues.Add(new EvaluationAcceptanceIssue(
                isBaseline
                    ? EvaluationAcceptanceIssueCode.AmbiguousBaseline
                    : EvaluationAcceptanceIssueCode.AmbiguousCandidate,
                $"Experiment '{experimentId}' has {runs.Count.ToString(CultureInfo.InvariantCulture)} runs; exactly one is required."));
            return null;
        }

        var run = runs[0];
        if (!string.Equals(
                run.ExperimentId,
                experimentId,
                StringComparison.Ordinal))
        {
            issues.Add(new EvaluationAcceptanceIssue(
                isBaseline
                    ? EvaluationAcceptanceIssueCode.MissingBaseline
                    : EvaluationAcceptanceIssueCode.MissingCandidate,
                $"The evaluation store returned a run for a different {(isBaseline ? "baseline" : "candidate")} experiment."));
            return null;
        }

        return run;
    }

    private static void CheckCompatibility(
        ExperimentRunRecord baseline,
        ExperimentRunRecord candidate,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        ValidateRunMetadata(baseline, "baseline", issues);
        ValidateRunMetadata(candidate, "candidate", issues);

        if (baseline.Id == Guid.Empty ||
            candidate.Id == Guid.Empty ||
            baseline.Id == candidate.Id)
        {
            AddIncompatibility(
                "Baseline and candidate must identify distinct persisted runs.",
                issues);
        }

        CheckEqual(
            baseline.DatasetName,
            candidate.DatasetName,
            "dataset name",
            issues);
        CheckEqual(
            baseline.DatasetVersion,
            candidate.DatasetVersion,
            "dataset version",
            issues);
        CheckEqual(
            baseline.SampleCount,
            candidate.SampleCount,
            "sample count",
            issues);
        CheckEqual(
            baseline.ModelType,
            candidate.ModelType,
            "model type",
            issues);
        CheckEqual(
            baseline.ModelName,
            candidate.ModelName,
            "model name",
            issues);
        CheckEqual(
            baseline.PromptVersion,
            candidate.PromptVersion,
            "prompt version",
            issues);

        if (baseline.SampleCount <= 0)
        {
            AddIncompatibility(
                "Both runs must have a positive sample count.",
                issues);
        }

        var baselineConfigurationValid = TryCanonicalizeConfiguration(
            baseline.ConfigJson,
            out var baselineConfiguration);
        var candidateConfigurationValid = TryCanonicalizeConfiguration(
            candidate.ConfigJson,
            out var candidateConfiguration);

        if (!baselineConfigurationValid ||
            !candidateConfigurationValid ||
            baselineConfiguration == "{}" ||
            candidateConfiguration == "{}")
        {
            AddMissingMetadata(
                "Both runs must contain non-empty, valid object-valued configuration metadata.",
                issues);
        }
        else if (!string.Equals(
                     baselineConfiguration,
                     candidateConfiguration,
                     StringComparison.Ordinal))
        {
            AddIncompatibility(
                "Run configurations differ in compatibility-relevant fields.",
                issues);
        }

        ValidateModelProvenance(baseline, "baseline", issues);
        ValidateModelProvenance(candidate, "candidate", issues);
    }

    private static void ValidateRunMetadata(
        ExperimentRunRecord run,
        string runLabel,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        ValidateRequiredMetadata(run.DatasetName, "dataset name", runLabel, issues);
        ValidateRequiredMetadata(run.DatasetVersion, "dataset version", runLabel, issues);
        ValidateRequiredMetadata(run.ModelType, "model type", runLabel, issues);
        ValidateRequiredMetadata(run.ModelName, "model name", runLabel, issues);
        ValidateRequiredMetadata(run.PromptVersion, "prompt version", runLabel, issues);
    }

    private static void ValidateRequiredMetadata(
        string? value,
        string fieldName,
        string runLabel,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            AddMissingMetadata(
                $"The {runLabel} run is missing required {fieldName} metadata.",
                issues);
        }
    }

    private static void ValidateModelProvenance(
        ExperimentRunRecord run,
        string runLabel,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        if (!string.Equals(
                run.ModelType,
                "llm",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        if (!TryGetLlmProvenance(
                run.ConfigJson,
                out var modelType,
                out var modelName,
                out var modelPromptVersion,
                out _,
                out _) ||
            !string.Equals(
                modelType,
                run.ModelType,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                modelName,
                run.ModelName,
                StringComparison.Ordinal) ||
            !string.Equals(
                modelPromptVersion,
                run.PromptVersion,
                StringComparison.Ordinal))
        {
            AddMissingMetadata(
                $"The {runLabel} LLM run is missing or inconsistent model type/name, prompt, provider, or model-version metadata in its configuration.",
                issues);
        }
    }

    private static bool TryGetLlmProvenance(
        string json,
        out string? modelType,
        out string? modelName,
        out string? promptVersion,
        out string? provider,
        out string? modelVersion)
    {
        modelType = null;
        modelName = null;
        promptVersion = null;
        provider = null;
        modelVersion = null;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object ||
                !document.RootElement.TryGetProperty("Model", out var model) ||
                model.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            modelType = GetNonEmptyString(model, "Type");
            modelName = GetNonEmptyString(model, "Name");
            promptVersion = GetNonEmptyString(model, "PromptVersion");
            provider = GetNonEmptyString(model, "Provider");
            modelVersion = GetNonEmptyString(model, "ModelVersion");
            return modelType is not null &&
                   modelName is not null &&
                   promptVersion is not null &&
                   provider is not null &&
                   modelVersion is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string? GetNonEmptyString(
        JsonElement element,
        string propertyName)
    {
        return element.TryGetProperty(propertyName, out var property) &&
               property.ValueKind == JsonValueKind.String &&
               !string.IsNullOrWhiteSpace(property.GetString())
            ? property.GetString()
            : null;
    }

    private static void CheckEqual<T>(
        T baselineValue,
        T candidateValue,
        string fieldName,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        if (!EqualityComparer<T>.Default.Equals(
                baselineValue,
                candidateValue))
        {
            AddIncompatibility(
                $"Baseline and candidate {fieldName} do not match.",
                issues);
        }
    }

    private static void AddIncompatibility(
        string message,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        issues.Add(new EvaluationAcceptanceIssue(
            EvaluationAcceptanceIssueCode.IncompatibleRuns,
            message));
    }

    private static void AddMissingMetadata(
        string message,
        ICollection<EvaluationAcceptanceIssue> issues)
    {
        issues.Add(new EvaluationAcceptanceIssue(
            EvaluationAcceptanceIssueCode.MissingCompatibilityMetadata,
            message));
    }

    private static IReadOnlyList<EvaluationMetricAcceptanceOutcome>
        EvaluateMetrics(
            ExperimentRunRecord baseline,
            ExperimentRunRecord candidate,
            EvaluationAcceptancePolicy policy,
            ICollection<EvaluationAcceptanceIssue> issues)
    {
        var baselineMetrics = ParseMetrics(
            baseline.MetricsJson,
            policy);
        var candidateMetrics = ParseMetrics(
            candidate.MetricsJson,
            policy);
        var outcomes =
            new List<EvaluationMetricAcceptanceOutcome>(
                policy.RequiredMetrics.Count);

        foreach (var rule in policy.RequiredMetrics)
        {
            var hasBaselineValue = baselineMetrics.TryGetValue(
                rule.MetricName,
                out var baselineValue);
            var hasCandidateValue = candidateMetrics.TryGetValue(
                rule.MetricName,
                out var candidateValue);

            if (!hasBaselineValue || !hasCandidateValue)
            {
                outcomes.Add(new EvaluationMetricAcceptanceOutcome(
                    rule.MetricName,
                    rule.Direction,
                    hasBaselineValue ? baselineValue : null,
                    hasCandidateValue ? candidateValue : null,
                    null,
                    rule.AllowedRegression,
                    null,
                    EvaluationMetricAcceptanceStatus.MissingOrInvalid));
                issues.Add(new EvaluationAcceptanceIssue(
                    EvaluationAcceptanceIssueCode.MissingOrInvalidMetric,
                    $"Required metric '{rule.MetricName}' is missing or invalid in one or both runs.",
                    rule.MetricName));
                continue;
            }

            decimal regression;
            try
            {
                regression = rule.Direction switch
                {
                    EvaluationMetricDirection.HigherIsBetter =>
                        Math.Max(0m, baselineValue - candidateValue),
                    EvaluationMetricDirection.LowerIsBetter =>
                        Math.Max(0m, candidateValue - baselineValue),
                    _ => throw new InvalidOperationException(
                        "Acceptance policy validation should reject unknown metric directions.")
                };
            }
            catch (OverflowException)
            {
                outcomes.Add(new EvaluationMetricAcceptanceOutcome(
                    rule.MetricName,
                    rule.Direction,
                    baselineValue,
                    candidateValue,
                    null,
                    rule.AllowedRegression,
                    null,
                    EvaluationMetricAcceptanceStatus.MissingOrInvalid));
                issues.Add(new EvaluationAcceptanceIssue(
                    EvaluationAcceptanceIssueCode.MissingOrInvalidMetric,
                    $"Required metric '{rule.MetricName}' produced a non-finite regression calculation.",
                    rule.MetricName));
                continue;
            }

            var thresholdExceeded =
                regression > rule.AllowedRegression;
            decimal? thresholdViolation = thresholdExceeded
                ? regression - rule.AllowedRegression
                : null;

            outcomes.Add(new EvaluationMetricAcceptanceOutcome(
                rule.MetricName,
                rule.Direction,
                baselineValue,
                candidateValue,
                regression,
                rule.AllowedRegression,
                thresholdViolation,
                thresholdExceeded
                    ? EvaluationMetricAcceptanceStatus.ThresholdExceeded
                    : EvaluationMetricAcceptanceStatus.Passed));

            if (thresholdExceeded)
            {
                issues.Add(new EvaluationAcceptanceIssue(
                    EvaluationAcceptanceIssueCode.ThresholdExceeded,
                    $"Required metric '{rule.MetricName}' regressed by {regression.ToString(CultureInfo.InvariantCulture)} with an allowed regression of {rule.AllowedRegression.ToString(CultureInfo.InvariantCulture)}.",
                    rule.MetricName));
            }
        }

        return outcomes;
    }

    private static Dictionary<string, decimal> ParseMetrics(
        string json,
        EvaluationAcceptancePolicy policy)
    {
        var requiredNames = policy.RequiredMetrics
            .Select(rule => rule.MetricName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var metrics = new Dictionary<string, decimal>(
            StringComparer.OrdinalIgnoreCase);
        var invalidMetrics = new HashSet<string>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return metrics;
            }

            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return metrics;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (!requiredNames.Contains(property.Name))
                {
                    continue;
                }

                if (invalidMetrics.Contains(property.Name))
                {
                    continue;
                }

                if (metrics.ContainsKey(property.Name) ||
                    property.Value.ValueKind != JsonValueKind.Number ||
                    !property.Value.TryGetDecimal(out var value))
                {
                    metrics.Remove(property.Name);
                    invalidMetrics.Add(property.Name);
                    continue;
                }

                metrics[property.Name] = value;
            }
        }
        catch (JsonException)
        {
            return new Dictionary<string, decimal>(
                StringComparer.OrdinalIgnoreCase);
        }
        catch (FormatException)
        {
            return new Dictionary<string, decimal>(
                StringComparer.OrdinalIgnoreCase);
        }

        return metrics;
    }

    private static bool TryCanonicalizeConfiguration(
        string json,
        out string canonicalConfiguration)
    {
        canonicalConfiguration = string.Empty;

        if (string.IsNullOrWhiteSpace(json))
        {
            return false;
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return false;
            }

            var builder = new StringBuilder();
            AppendCanonicalJson(
                document.RootElement,
                builder,
                isRoot: true);
            canonicalConfiguration = builder.ToString();
            return true;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static void AppendCanonicalJson(
        JsonElement element,
        StringBuilder builder,
        bool isRoot = false)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                builder.Append('{');
                var properties = element.EnumerateObject()
                    .Where(property =>
                        !isRoot ||
                        !IsNonCompatibilityRootProperty(property.Name))
                    .OrderBy(
                        property => property.Name,
                        StringComparer.Ordinal)
                    .ToArray();

                for (var index = 0; index < properties.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    builder.Append(JsonSerializer.Serialize(
                        properties[index].Name));
                    builder.Append(':');
                    AppendCanonicalJson(
                        properties[index].Value,
                        builder);
                }

                builder.Append('}');
                break;

            case JsonValueKind.Array:
                builder.Append('[');
                var values = element.EnumerateArray().ToArray();
                for (var index = 0; index < values.Length; index++)
                {
                    if (index > 0)
                    {
                        builder.Append(',');
                    }

                    AppendCanonicalJson(values[index], builder);
                }

                builder.Append(']');
                break;

            case JsonValueKind.String:
                builder.Append(JsonSerializer.Serialize(
                    element.GetString()));
                break;

            case JsonValueKind.Number:
                if (element.TryGetDecimal(out var decimalValue))
                {
                    builder.Append(decimalValue.ToString(
                        "G29",
                        CultureInfo.InvariantCulture));
                }
                else
                {
                    builder.Append(element.GetRawText());
                }

                break;

            default:
                builder.Append(element.GetRawText());
                break;
        }
    }

    private static bool IsNonCompatibilityRootProperty(string name)
    {
        return name.Equals("Notes", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("ExecutionNote", StringComparison.OrdinalIgnoreCase) ||
               name.Equals("SampleCount", StringComparison.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<EvaluationMetricAcceptanceOutcome>
        CreateNotEvaluatedMetrics(EvaluationAcceptancePolicy policy)
    {
        return policy.RequiredMetrics
            .Select(rule => new EvaluationMetricAcceptanceOutcome(
                rule.MetricName,
                rule.Direction,
                null,
                null,
                null,
                rule.AllowedRegression,
                null,
                EvaluationMetricAcceptanceStatus.NotEvaluated))
            .ToArray();
    }

    private static EvaluationAcceptanceResult CreateResult(
        string baselineExperimentId,
        string candidateExperimentId,
        EvaluationAcceptancePolicy policy,
        ExperimentRunRecord? baseline,
        ExperimentRunRecord? candidate,
        EvaluationAcceptanceStatus status,
        IReadOnlyList<EvaluationAcceptanceIssue> issues,
        IReadOnlyList<EvaluationMetricAcceptanceOutcome> metrics)
    {
        return new EvaluationAcceptanceResult(
            policy.Name,
            baselineExperimentId,
            candidateExperimentId,
            baseline?.Id,
            candidate?.Id,
            status,
            issues,
            metrics);
    }
}
