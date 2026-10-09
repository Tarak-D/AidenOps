
using System.Text.Json;
using AIOps.Abstractions.Evaluation;

namespace AIOps.Orchestration.Evaluation;

public sealed record EvaluationRunComparison(
    Guid RunId,
    string ExperimentId,
    string ModelType,
    string ModelName,
    string? Provider,
    string? ModelVersion,
    string? PromptVersion,
    string DatasetName,
    string DatasetVersion,
    int SampleCount,
    DateTimeOffset StartedAt,
    DateTimeOffset? FinishedAt,
    IReadOnlyDictionary<string, double> Metrics);

public enum EvaluationMetricTrend
{
    Improved,
    Regressed,
    Unchanged,
    Unknown
}

public sealed record EvaluationMetricDelta(
    string MetricName,
    double BaselineValue,
    double CandidateValue,
    double Delta,
    EvaluationMetricTrend Trend);

public sealed record EvaluationRunDelta(
    Guid BaselineRunId,
    Guid CandidateRunId,
    IReadOnlyList<EvaluationMetricDelta> Metrics);

public sealed record EvaluationDatasetComparison(
    string DatasetName,
    string DatasetVersion,
    IReadOnlyList<EvaluationRunComparison> Runs,
    IReadOnlyList<EvaluationRunDelta> Comparisons);

public sealed record EvaluationComparisonReport(
    int RunCount,
    IReadOnlyList<EvaluationDatasetComparison> Datasets);

public sealed class EvaluationComparisonService
{
    private readonly IEvaluationStore _evaluationStore;

    public EvaluationComparisonService(IEvaluationStore evaluationStore)
    {
        ArgumentNullException.ThrowIfNull(evaluationStore);
        _evaluationStore = evaluationStore;
    }

    public async Task<EvaluationComparisonReport> CompareAsync(
        string? experimentId = null,
        string? datasetName = null,
        CancellationToken cancellationToken = default)
    {
        var runs = await _evaluationStore.ListRunsAsync(
            experimentId,
            datasetName,
            cancellationToken);

        var comparisons = runs
            .Select(CreateRunComparison)
            .ToArray();

        var datasets = comparisons
            .GroupBy(run => new
            {
                run.DatasetName,
                run.DatasetVersion
            })
            .Select(group =>
            {
                var orderedRuns = group
                    .OrderBy(run => run.StartedAt)
                    .ThenBy(run => run.ModelName, StringComparer.Ordinal)
                    .ToArray();

                return new EvaluationDatasetComparison(
                    group.Key.DatasetName,
                    group.Key.DatasetVersion,
                    orderedRuns,
                    CompareRuns(orderedRuns));
            })
            .OrderBy(
                group => group.DatasetName,
                StringComparer.Ordinal)
            .ThenBy(
                group => group.DatasetVersion,
                StringComparer.Ordinal)
            .ToArray();

        return new EvaluationComparisonReport(
            comparisons.Length,
            datasets);
    }

    private static IReadOnlyList<EvaluationRunDelta> CompareRuns(
        IReadOnlyList<EvaluationRunComparison> runs)
    {
        var comparisons = new List<EvaluationRunDelta>();

        for (var i = 0; i < runs.Count; i++)
        {
            for (var j = i + 1; j < runs.Count; j++)
            {
                var baseline = runs[i];
                var candidate = runs[j];

                var deltas = new List<EvaluationMetricDelta>();

                foreach (var metric in baseline.Metrics)
                {
                    if (!candidate.Metrics.TryGetValue(
                            metric.Key,
                            out var candidateValue))
                    {
                        // A delta is meaningful only when both runs
                        // contain the same metric.
                        continue;
                    }

                    var baselineValue = metric.Value;
                    var delta = candidateValue - baselineValue;

                    deltas.Add(new EvaluationMetricDelta(
                        metric.Key,
                        baselineValue,
                        candidateValue,
                        delta,
                        GetMetricTrend(metric.Key, delta)));
                }

                comparisons.Add(new EvaluationRunDelta(
                    baseline.RunId,
                    candidate.RunId,
                    deltas));
            }
        }

        return comparisons;
    }

    private static EvaluationMetricTrend GetMetricTrend(
        string metricName,
        double delta)
    {
        if (delta == 0)
        {
            return EvaluationMetricTrend.Unchanged;
        }

        // Higher values are better for these metrics.
        if (ContainsAny(
                metricName,
                "Accuracy",
                "Recall",
                "Precision",
                "F1",
                "MRR",
                "HitAt"))
        {
            return delta > 0
                ? EvaluationMetricTrend.Improved
                : EvaluationMetricTrend.Regressed;
        }

        // Lower values are better for these metrics.
        if (ContainsAny(
                metricName,
                "Latency",
                "Cost",
                "Error",
                "Failure",
                "FirstRelevantRank"))
        {
            return delta < 0
                ? EvaluationMetricTrend.Improved
                : EvaluationMetricTrend.Regressed;
        }

        // Do not assume whether an unknown metric should increase
        // or decrease.
        return EvaluationMetricTrend.Unknown;
    }

    private static bool ContainsAny(
        string metricName,
        params string[] terms)
    {
        return terms.Any(term =>
            metricName.Contains(
                term,
                StringComparison.OrdinalIgnoreCase));
    }

    private static EvaluationRunComparison CreateRunComparison(
        ExperimentRunRecord run)
    {
        var configuration = ParseObject(run.ConfigJson);
        var model = GetProperty(configuration, "Model");

        var provider = GetString(model, "Provider");
        var modelVersion = GetString(model, "ModelVersion");
        var configuredPromptVersion = GetString(model, "PromptVersion");

        var promptVersion = !string.IsNullOrWhiteSpace(run.PromptVersion)
            ? run.PromptVersion
            : configuredPromptVersion;

        var metrics = ParseNumericMetrics(run.MetricsJson);

        return new EvaluationRunComparison(
            run.Id,
            run.ExperimentId,
            run.ModelType,
            run.ModelName,
            provider,
            modelVersion,
            promptVersion,
            run.DatasetName,
            run.DatasetVersion,
            run.SampleCount,
            run.StartedAt,
            run.FinishedAt,
            metrics);
    }

    private static IReadOnlyDictionary<string, double> ParseNumericMetrics(
        string json)
    {
        var metrics = new Dictionary<string, double>(
            StringComparer.OrdinalIgnoreCase);

        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind != JsonValueKind.Object)
            {
                return metrics;
            }

            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (property.Value.ValueKind == JsonValueKind.Number &&
                    property.Value.TryGetDouble(out var value) &&
                    double.IsFinite(value))
                {
                    metrics[property.Name] = value;
                }
            }
        }
        catch (JsonException)
        {
            // Invalid metrics JSON produces no metrics for this run.
        }

        return metrics;
    }

    private static JsonElement ParseObject(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);

            if (document.RootElement.ValueKind == JsonValueKind.Object)
            {
                return document.RootElement.Clone();
            }
        }
        catch (JsonException)
        {
            // Configuration metadata is optional.
        }

        return default;
    }

    private static JsonElement GetProperty(
        JsonElement element,
        string propertyName)
    {
        if (element.ValueKind == JsonValueKind.Object &&
            element.TryGetProperty(propertyName, out var property))
        {
            return property;
        }

        return default;
    }

    private static string? GetString(
        JsonElement element,
        string propertyName)
    {
        var property = GetProperty(element, propertyName);

        return property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;
    }
}