using AIOps.Abstractions.Evaluation;
using AIOps.Orchestration.Evaluation;
using Moq;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationComparisonServiceTests
{
    [Fact]
    public async Task CompareAsync_groups_runs_by_dataset_name_and_version()
    {
        var store = new Mock<IEvaluationStore>();
        var startedAt = DateTimeOffset.UtcNow;

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "provider-comparison",
                    "model-a",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.8}""",
                    startedAt: startedAt),

                CreateRun(
                    "provider-comparison",
                    "model-b",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.9}""",
                    startedAt: startedAt.AddMinutes(1)),

                CreateRun(
                    "provider-comparison",
                    "model-a",
                    "golden",
                    "v2",
                    """{"TriageAccuracy":0.95}""",
                    startedAt: startedAt.AddMinutes(2))
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();

        Assert.Equal(3, report.RunCount);
        Assert.Equal(2, report.Datasets.Count);

        var v1 = Assert.Single(
            report.Datasets,
            d => d.DatasetVersion == "v1");

        Assert.Equal(2, v1.Runs.Count);
        Assert.Equal(0.8, v1.Runs[0].Metrics["TriageAccuracy"]);
        Assert.Equal(0.9, v1.Runs[1].Metrics["TriageAccuracy"]);

        var v1Comparison = Assert.Single(v1.Comparisons);
        var v1Metric = Assert.Single(v1Comparison.Metrics);

        Assert.Equal("TriageAccuracy", v1Metric.MetricName);
        Assert.Equal(0.1, v1Metric.Delta, precision: 10);
        Assert.Equal(EvaluationMetricTrend.Improved, v1Metric.Trend);

        var v2 = Assert.Single(
            report.Datasets,
            d => d.DatasetVersion == "v2");

        Assert.Single(v2.Runs);
        Assert.Empty(v2.Comparisons);
    }

    [Fact]
    public async Task CompareAsync_extracts_provider_model_and_prompt_metadata()
    {
        var store = new Mock<IEvaluationStore>();

        store.Setup(s => s.ListRunsAsync(
                "experiment-1",
                "golden",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "experiment-1",
                    "model-a",
                    "golden",
                    "v1",
                    """{"DecisionAccuracy":0.75}""",
                    configJson:
                        """{"Model":{"Provider":"provider-a","ModelVersion":"2026-01","PromptVersion":"prompt-config"}}""",
                    promptVersion: "prompt-run")
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync(
            "experiment-1",
            "golden");

        var run = Assert.Single(
            Assert.Single(report.Datasets).Runs);

        Assert.Equal("provider-a", run.Provider);
        Assert.Equal("2026-01", run.ModelVersion);
        Assert.Equal("prompt-run", run.PromptVersion);
        Assert.Equal(0.75, run.Metrics["DecisionAccuracy"]);

        store.Verify(s => s.ListRunsAsync(
                "experiment-1",
                "golden",
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task CompareAsync_keeps_missing_metrics_unavailable()
    {
        var store = new Mock<IEvaluationStore>();
        var startedAt = DateTimeOffset.UtcNow;

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "experiment-1",
                    "model-a",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.8,"AverageLatencyMs":120}""",
                    startedAt: startedAt),

                CreateRun(
                    "experiment-1",
                    "model-b",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.9,"CostUsd":0.02}""",
                    startedAt: startedAt.AddMinutes(1))
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();
        var runs = Assert.Single(report.Datasets).Runs;

        Assert.True(runs[0].Metrics.ContainsKey("TriageAccuracy"));
        Assert.True(runs[0].Metrics.ContainsKey("AverageLatencyMs"));
        Assert.False(runs[0].Metrics.ContainsKey("CostUsd"));

        Assert.True(runs[1].Metrics.ContainsKey("TriageAccuracy"));
        Assert.False(runs[1].Metrics.ContainsKey("AverageLatencyMs"));
        Assert.True(runs[1].Metrics.ContainsKey("CostUsd"));

        var comparison = Assert.Single(
            Assert.Single(report.Datasets).Comparisons);

        var metric = Assert.Single(comparison.Metrics);

        Assert.Equal("TriageAccuracy", metric.MetricName);
        Assert.Equal(EvaluationMetricTrend.Improved, metric.Trend);
    }

    [Fact]
    public async Task CompareAsync_returns_empty_report_when_no_runs_exist()
    {
        var store = new Mock<IEvaluationStore>();

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(Array.Empty<ExperimentRunRecord>());

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();

        Assert.Equal(0, report.RunCount);
        Assert.Empty(report.Datasets);
    }

    [Fact]
    public async Task CompareAsync_marks_higher_accuracy_as_improved()
    {
        var store = new Mock<IEvaluationStore>();
        var startedAt = DateTimeOffset.UtcNow;

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "experiment-1",
                    "model-a",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.8}""",
                    startedAt: startedAt),

                CreateRun(
                    "experiment-1",
                    "model-b",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.9}""",
                    startedAt: startedAt.AddMinutes(1))
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();
        var comparison = Assert.Single(
            Assert.Single(report.Datasets).Comparisons);

        var metric = Assert.Single(comparison.Metrics);

        Assert.Equal("TriageAccuracy", metric.MetricName);
        Assert.Equal(0.8, metric.BaselineValue);
        Assert.Equal(0.9, metric.CandidateValue);
        Assert.Equal(0.1, metric.Delta, precision: 10);
        Assert.Equal(EvaluationMetricTrend.Improved, metric.Trend);
    }

    [Fact]
    public async Task CompareAsync_marks_lower_latency_and_cost_as_improved()
    {
        var store = new Mock<IEvaluationStore>();
        var startedAt = DateTimeOffset.UtcNow;

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "experiment-1",
                    "model-a",
                    "golden",
                    "v1",
                    """{"AverageLatencyMs":120,"CostUsd":0.05}""",
                    startedAt: startedAt),

                CreateRun(
                    "experiment-1",
                    "model-b",
                    "golden",
                    "v1",
                    """{"AverageLatencyMs":80,"CostUsd":0.02}""",
                    startedAt: startedAt.AddMinutes(1))
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();
        var comparison = Assert.Single(
            Assert.Single(report.Datasets).Comparisons);

        Assert.Equal(2, comparison.Metrics.Count);

        var latency = Assert.Single(
            comparison.Metrics,
            m => m.MetricName == "AverageLatencyMs");

        Assert.Equal(-40, latency.Delta);
        Assert.Equal(EvaluationMetricTrend.Improved, latency.Trend);

        var cost = Assert.Single(
            comparison.Metrics,
            m => m.MetricName == "CostUsd");

        Assert.Equal(-0.03, cost.Delta, precision: 10);
        Assert.Equal(EvaluationMetricTrend.Improved, cost.Trend);
    }

    [Fact]
    public async Task CompareAsync_marks_unchanged_and_unknown_metrics_correctly()
    {
        var store = new Mock<IEvaluationStore>();
        var startedAt = DateTimeOffset.UtcNow;

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "experiment-1",
                    "model-a",
                    "golden",
                    "v1",
                    """{"DecisionAccuracy":0.8,"CustomMetric":5}""",
                    startedAt: startedAt),

                CreateRun(
                    "experiment-1",
                    "model-b",
                    "golden",
                    "v1",
                    """{"DecisionAccuracy":0.8,"CustomMetric":6}""",
                    startedAt: startedAt.AddMinutes(1))
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();
        var comparison = Assert.Single(
            Assert.Single(report.Datasets).Comparisons);

        var unchanged = Assert.Single(
            comparison.Metrics,
            m => m.MetricName == "DecisionAccuracy");

        Assert.Equal(0, unchanged.Delta);
        Assert.Equal(EvaluationMetricTrend.Unchanged, unchanged.Trend);

        var unknown = Assert.Single(
            comparison.Metrics,
            m => m.MetricName == "CustomMetric");

        Assert.Equal(1, unknown.Delta);
        Assert.Equal(EvaluationMetricTrend.Unknown, unknown.Trend);
    }

    [Fact]
    public async Task CompareAsync_compares_only_metrics_shared_by_both_runs()
    {
        var store = new Mock<IEvaluationStore>();
        var startedAt = DateTimeOffset.UtcNow;

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "experiment-1",
                    "model-a",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.8,"AverageLatencyMs":120}""",
                    startedAt: startedAt),

                CreateRun(
                    "experiment-1",
                    "model-b",
                    "golden",
                    "v1",
                    """{"TriageAccuracy":0.9,"CostUsd":0.02}""",
                    startedAt: startedAt.AddMinutes(1))
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();
        var comparison = Assert.Single(
            Assert.Single(report.Datasets).Comparisons);

        var metric = Assert.Single(comparison.Metrics);

        Assert.Equal("TriageAccuracy", metric.MetricName);
        Assert.Equal(EvaluationMetricTrend.Improved, metric.Trend);
    }

    [Fact]
    public async Task CompareAsync_classifies_retrieval_metric_trends_correctly()
    {
        var store = new Mock<IEvaluationStore>();
        var startedAt = DateTimeOffset.UtcNow;

        store.Setup(s => s.ListRunsAsync(
                null,
                null,
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(new[]
            {
                CreateRun(
                    "experiment-retrieval",
                    "model-a",
                    "golden",
                    "v1",
                    """{"Hit@5":0.6,"MRR":0.5,"AverageFirstRelevantRank":3.0}""",
                    startedAt: startedAt),

                CreateRun(
                    "experiment-retrieval",
                    "model-b",
                    "golden",
                    "v1",
                    """{"Hit@5":0.8,"MRR":0.7,"AverageFirstRelevantRank":2.0}""",
                    startedAt: startedAt.AddMinutes(1))
            });

        var service = new EvaluationComparisonService(store.Object);

        var report = await service.CompareAsync();
        var comparison = Assert.Single(
            Assert.Single(report.Datasets).Comparisons);

        Assert.Equal(3, comparison.Metrics.Count);

        Assert.Equal(
            EvaluationMetricTrend.Improved,
            Assert.Single(
                comparison.Metrics,
                m => m.MetricName == "Hit@5").Trend);

        Assert.Equal(
            EvaluationMetricTrend.Improved,
            Assert.Single(
                comparison.Metrics,
                m => m.MetricName == "MRR").Trend);

        Assert.Equal(
            EvaluationMetricTrend.Improved,
            Assert.Single(
                comparison.Metrics,
                m => m.MetricName == "AverageFirstRelevantRank").Trend);
    }

    private static ExperimentRunRecord CreateRun(
        string experimentId,
        string modelName,
        string datasetName,
        string datasetVersion,
        string metricsJson,
        string configJson = "{}",
        string? promptVersion = null,
        DateTimeOffset? startedAt = null)
    {
        var runStartedAt = startedAt ?? DateTimeOffset.UtcNow;

        return new ExperimentRunRecord(
            Guid.NewGuid(),
            experimentId,
            "llm",
            modelName,
            promptVersion,
            datasetName,
            datasetVersion,
            configJson,
            10,
            runStartedAt,
            runStartedAt.AddSeconds(1),
            metricsJson,
            null);
    }
}