using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Orchestration.Evaluation;
using Moq;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationAcceptanceGateTests
{
    [Fact]
    public async Task EvaluateAsync_passes_when_metric_regressions_are_within_policy()
    {
        var gate = CreateGate(
            CreateRun(
                "baseline",
                """{"TriageAccuracy":0.9,"AverageLatencyMs":100}"""),
            CreateRun(
                "candidate",
                """{"TriageAccuracy":0.88,"AverageLatencyMs":105}"""));
        var policy = CreatePolicy(
            new EvaluationMetricAcceptanceRule(
                "TriageAccuracy",
                EvaluationMetricDirection.HigherIsBetter,
                0.021m),
            new EvaluationMetricAcceptanceRule(
                "AverageLatencyMs",
                EvaluationMetricDirection.LowerIsBetter,
                5m));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            policy);

        Assert.Equal(EvaluationAcceptanceStatus.Passed, result.Status);
        Assert.Empty(result.Issues);
        Assert.Equal(2, result.Metrics.Count);
        Assert.Equal(0.02m, result.Metrics[0].Regression!.Value);
        Assert.Null(result.Metrics[0].ThresholdViolation);
        Assert.Equal(5m, result.Metrics[1].Regression);
        Assert.Null(result.Metrics[1].ThresholdViolation);
    }

    [Fact]
    public async Task EvaluateAsync_fails_when_higher_is_better_metric_exceeds_allowance()
    {
        var gate = CreateGate(
            CreateRun("baseline", """{"Accuracy":0.95}"""),
            CreateRun("candidate", """{"Accuracy":0.90}"""));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0.02m)));

        var metric = Assert.Single(result.Metrics);
        Assert.Equal(EvaluationAcceptanceStatus.Failed, result.Status);
        Assert.Equal(EvaluationMetricAcceptanceStatus.ThresholdExceeded, metric.Status);
        Assert.Equal(0.05m, metric.Regression!.Value);
        Assert.Equal(0.03m, metric.ThresholdViolation!.Value);
        Assert.Equal(
            EvaluationAcceptanceIssueCode.ThresholdExceeded,
            Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task EvaluateAsync_fails_when_lower_is_better_metric_exceeds_allowance()
    {
        var gate = CreateGate(
            CreateRun("baseline", """{"AverageLatencyMs":100}"""),
            CreateRun("candidate", """{"AverageLatencyMs":112}"""));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "AverageLatencyMs",
                    EvaluationMetricDirection.LowerIsBetter,
                    2m)));

        var metric = Assert.Single(result.Metrics);
        Assert.Equal(EvaluationAcceptanceStatus.Failed, result.Status);
        Assert.Equal(12m, metric.Regression);
        Assert.Equal(10m, metric.ThresholdViolation);
    }

    [Theory]
    [InlineData("higher", "0.90", "0.88", true)]
    [InlineData("higher", "0.90", "0.8800000000000000000001", true)]
    [InlineData("higher", "0.90", "0.8799999999999999999999", false)]
    [InlineData("lower", "0.90", "0.92", true)]
    [InlineData("lower", "0.90", "0.9199999999999999999999", true)]
    [InlineData("lower", "0.90", "0.9200000000000000000001", false)]
    public async Task EvaluateAsync_compares_decimal_regression_boundaries_exactly(
        string direction,
        string baselineValue,
        string candidateValue,
        bool shouldPass)
    {
        var metricName = direction == "higher"
            ? "Accuracy"
            : "AverageLatencyMs";
        var metricDirection = direction == "higher"
            ? EvaluationMetricDirection.HigherIsBetter
            : EvaluationMetricDirection.LowerIsBetter;
        var gate = CreateGate(
            CreateRun("baseline", $$"""{"{{metricName}}":{{baselineValue}}}"""),
            CreateRun("candidate", $$"""{"{{metricName}}":{{candidateValue}}}"""));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    metricName,
                    metricDirection,
                    0.02m)));

        Assert.Equal(
            shouldPass
                ? EvaluationAcceptanceStatus.Passed
                : EvaluationAcceptanceStatus.Failed,
            result.Status);
        Assert.Equal(
            shouldPass
                ? EvaluationMetricAcceptanceStatus.Passed
                : EvaluationMetricAcceptanceStatus.ThresholdExceeded,
            Assert.Single(result.Metrics).Status);
    }

    [Fact]
    public async Task EvaluateAsync_fails_closed_when_required_metric_is_missing_or_invalid()
    {
        var gate = CreateGate(
            CreateRun("baseline", """{"Accuracy":0.9}"""),
            CreateRun("candidate", """{"Accuracy":"unknown"}"""));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0m)));

        Assert.Equal(EvaluationAcceptanceStatus.Failed, result.Status);
        var metric = Assert.Single(result.Metrics);
        Assert.Equal(EvaluationMetricAcceptanceStatus.MissingOrInvalid, metric.Status);
        Assert.Equal(0.9m, metric.BaselineValue);
        Assert.Null(metric.CandidateValue);
        Assert.Equal(
            EvaluationAcceptanceIssueCode.MissingOrInvalidMetric,
            Assert.Single(result.Issues).Code);
    }

    [Fact]
    public async Task EvaluateAsync_blocks_when_baseline_is_missing()
    {
        var gate = CreateGate(CreateRun("candidate", """{"Accuracy":0.9}"""));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0m)));

        Assert.Equal(EvaluationAcceptanceStatus.Blocked, result.Status);
        Assert.Equal(
            EvaluationAcceptanceIssueCode.MissingBaseline,
            Assert.Single(result.Issues).Code);
        Assert.Equal(
            EvaluationMetricAcceptanceStatus.NotEvaluated,
            Assert.Single(result.Metrics).Status);
    }

    [Fact]
    public async Task EvaluateAsync_blocks_when_candidate_is_missing()
    {
        var gate = CreateGate(CreateRun("baseline", """{"Accuracy":0.9}"""));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0m)));

        Assert.Equal(EvaluationAcceptanceStatus.Blocked, result.Status);
        Assert.Equal(
            EvaluationAcceptanceIssueCode.MissingCandidate,
            Assert.Single(result.Issues).Code);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("candidate")]
    public async Task EvaluateAsync_blocks_when_named_experiment_is_ambiguous(
        string ambiguousExperiment)
    {
        var runs = new List<ExperimentRunRecord>
        {
            CreateRun("baseline", """{"Accuracy":0.9}"""),
            CreateRun("candidate", """{"Accuracy":0.9}""")
        };
        runs.Add(CreateRun(
            ambiguousExperiment,
            """{"Accuracy":0.9}"""));

        var gate = CreateGate(runs.ToArray());
        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0m)));

        Assert.Equal(EvaluationAcceptanceStatus.Blocked, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Code ==
                (ambiguousExperiment == "baseline"
                    ? EvaluationAcceptanceIssueCode.AmbiguousBaseline
                    : EvaluationAcceptanceIssueCode.AmbiguousCandidate));
    }

    [Theory]
    [InlineData("dataset name")]
    [InlineData("dataset version")]
    [InlineData("model type")]
    [InlineData("model name")]
    [InlineData("prompt version")]
    [InlineData("configuration prompt")]
    [InlineData("configuration")]
    [InlineData("provider")]
    [InlineData("model version")]
    public async Task EvaluateAsync_blocks_when_required_provenance_is_missing(
        string missingField)
    {
        var baseline = WithMissingMetadata(
            CreateRun("baseline", """{"Accuracy":0.9}"""),
            missingField);
        var candidate = WithMissingMetadata(
            CreateRun("candidate", """{"Accuracy":0.9}"""),
            missingField);
        var gate = CreateGate(baseline, candidate);

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0m)));

        Assert.Equal(EvaluationAcceptanceStatus.Blocked, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Code ==
                EvaluationAcceptanceIssueCode.MissingCompatibilityMetadata);
    }

    [Theory]
    [InlineData("{")]
    [InlineData("""{"Accuracy":0.9,"Accuracy":0.9}""")]
    [InlineData("""{"Accuracy":1e999}""")]
    public async Task EvaluateAsync_fails_closed_for_malformed_duplicate_or_non_finite_metric_json(
        string candidateMetrics)
    {
        var gate = CreateGate(
            CreateRun("baseline", """{"Accuracy":0.9}"""),
            CreateRun("candidate", candidateMetrics));

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0m)));

        Assert.Equal(EvaluationAcceptanceStatus.Failed, result.Status);
        Assert.Equal(
            EvaluationMetricAcceptanceStatus.MissingOrInvalid,
            Assert.Single(result.Metrics).Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Code ==
                EvaluationAcceptanceIssueCode.MissingOrInvalidMetric);
    }

    [Theory]
    [InlineData("dataset name")]
    [InlineData("dataset version")]
    [InlineData("sample count")]
    [InlineData("model")]
    [InlineData("provider")]
    [InlineData("prompt")]
    [InlineData("configuration")]
    public async Task EvaluateAsync_blocks_incompatible_runs(string mismatch)
    {
        var baseline = CreateRun("baseline", """{"Accuracy":0.9}""");
        var candidate = mismatch switch
        {
            "dataset name" => baseline with
            {
                Id = Guid.NewGuid(),
                ExperimentId = "candidate",
                DatasetName = "different-dataset"
            },
            "dataset version" => baseline with
            {
                Id = Guid.NewGuid(),
                ExperimentId = "candidate",
                DatasetVersion = "different-version"
            },
            "sample count" => baseline with
            {
                Id = Guid.NewGuid(),
                ExperimentId = "candidate",
                SampleCount = 11
            },
            "model" => baseline with
            {
                Id = Guid.NewGuid(),
                ExperimentId = "candidate",
                ModelName = "different-model"
            },
            "provider" => baseline with
            {
                Id = Guid.NewGuid(),
                ExperimentId = "candidate",
                ConfigJson = """{"Model":{"Type":"llm","Name":"test-model","PromptVersion":"prompt-v1","Provider":"different-provider","ModelVersion":"model-v1"},"Deterministic":true}"""
            },
            "prompt" => baseline with
            {
                Id = Guid.NewGuid(),
                ExperimentId = "candidate",
                PromptVersion = "different-prompt"
            },
            "configuration" => baseline with
            {
                Id = Guid.NewGuid(),
                ExperimentId = "candidate",
                ConfigJson = """{"Model":{"Type":"llm","Name":"test-model","PromptVersion":"prompt-v1","Provider":"test-provider","ModelVersion":"model-v1"},"Deterministic":false}"""
            },
            _ => throw new ArgumentOutOfRangeException(nameof(mismatch))
        };
        var gate = CreateGate(baseline, candidate);

        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    0m)));

        Assert.Equal(EvaluationAcceptanceStatus.Blocked, result.Status);
        Assert.Contains(
            result.Issues,
            issue => issue.Code ==
                EvaluationAcceptanceIssueCode.IncompatibleRuns);
        Assert.All(
            result.Metrics,
            metric => Assert.Equal(
                EvaluationMetricAcceptanceStatus.NotEvaluated,
                metric.Status));
    }

    [Fact]
    public async Task EvaluateAsync_rejects_invalid_policy()
    {
        var gate = CreateGate(
            CreateRun("baseline", """{"Accuracy":0.9}"""),
            CreateRun("candidate", """{"Accuracy":0.9}"""));
        var duplicatePolicy = CreatePolicy(
            new EvaluationMetricAcceptanceRule(
                "Accuracy",
                EvaluationMetricDirection.HigherIsBetter,
                0m),
            new EvaluationMetricAcceptanceRule(
                "accuracy",
                EvaluationMetricDirection.LowerIsBetter,
                0m));

        await Assert.ThrowsAsync<ArgumentException>(
            () => gate.EvaluateAsync(
                "baseline",
                "candidate",
                duplicatePolicy));

        Assert.Throws<ArgumentOutOfRangeException>(
            () => CreatePolicy(
                new EvaluationMetricAcceptanceRule(
                    "Accuracy",
                    EvaluationMetricDirection.HigherIsBetter,
                    -0.01m)).Validate());
    }

    [Fact]
    public async Task EvaluateAsync_returns_deterministic_result_for_same_inputs()
    {
        var gate = CreateGate(
            CreateRun("baseline", """{"Accuracy":0.9}"""),
            CreateRun("candidate", """{"Accuracy":0.89}"""));
        var policy = CreatePolicy(
            new EvaluationMetricAcceptanceRule(
                "Accuracy",
                EvaluationMetricDirection.HigherIsBetter,
                0.02m));

        var first = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            policy);
        var second = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            policy);

        Assert.Equal(
            JsonSerializer.Serialize(first),
            JsonSerializer.Serialize(second));
    }

    [Fact]
    public async Task EvaluateAsync_uses_validated_policy_snapshot_after_store_awaits()
    {
        var baseline = CreateRun("baseline", """{"Accuracy":0.9}""");
        var candidate = CreateRun("candidate", """{"Accuracy":0.9}""");
        var rules = new[]
        {
            new EvaluationMetricAcceptanceRule(
                "Accuracy",
                EvaluationMetricDirection.HigherIsBetter,
                0m)
        };
        var policy = CreatePolicy(rules);
        var store = new Mock<IEvaluationStore>();

        store.Setup(value => value.ListRunsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                string? experimentId,
                string? _,
                CancellationToken _) =>
            {
                if (experimentId == "baseline")
                {
                    rules[0] = new EvaluationMetricAcceptanceRule(
                        "UnvalidatedMetric",
                        EvaluationMetricDirection.HigherIsBetter,
                        -1m);
                    return Task.FromResult<IReadOnlyList<ExperimentRunRecord>>(
                        [baseline]);
                }

                return Task.FromResult<IReadOnlyList<ExperimentRunRecord>>(
                    [candidate]);
            });

        var gate = new EvaluationAcceptanceGate(store.Object);
        var result = await gate.EvaluateAsync(
            "baseline",
            "candidate",
            policy);

        Assert.Equal(EvaluationAcceptanceStatus.Passed, result.Status);
        var metric = Assert.Single(result.Metrics);
        Assert.Equal("Accuracy", metric.MetricName);
        Assert.Equal(0m, metric.AllowedRegression);
        Assert.Equal(0m, metric.Regression);
    }

    private static EvaluationAcceptancePolicy CreatePolicy(
        params EvaluationMetricAcceptanceRule[] rules)
    {
        return new EvaluationAcceptancePolicy(
            "phase15-acceptance",
            rules);
    }

    private static ExperimentRunRecord WithMissingMetadata(
        ExperimentRunRecord run,
        string field)
    {
        return field switch
        {
            "dataset name" => run with { DatasetName = " " },
            "dataset version" => run with { DatasetVersion = "" },
            "model type" => run with { ModelType = "" },
            "model name" => run with { ModelName = " " },
            "prompt version" => run with { PromptVersion = null },
            "configuration prompt" => run with
            {
                ConfigJson = """{"Model":{"Type":"llm","Name":"test-model","PromptVersion":"","Provider":"test-provider","ModelVersion":"model-v1"},"Deterministic":true}"""
            },
            "configuration" => run with { ConfigJson = "{}" },
            "provider" => run with
            {
                ConfigJson = """{"Model":{"Type":"llm","Name":"test-model","PromptVersion":"prompt-v1","Provider":" ","ModelVersion":"model-v1"},"Deterministic":true}"""
            },
            "model version" => run with
            {
                ConfigJson = """{"Model":{"Type":"llm","Name":"test-model","PromptVersion":"prompt-v1","Provider":"test-provider","ModelVersion":""},"Deterministic":true}"""
            },
            _ => throw new ArgumentOutOfRangeException(nameof(field))
        };
    }

    private static EvaluationAcceptanceGate CreateGate(
        params ExperimentRunRecord[] runs)
    {
        var store = new Mock<IEvaluationStore>();
        store.Setup(value => value.ListRunsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                string? experimentId,
                string? _,
                CancellationToken _) =>
            {
                IReadOnlyList<ExperimentRunRecord> matchingRuns = runs
                    .Where(run =>
                        experimentId is null ||
                        run.ExperimentId == experimentId)
                    .ToArray();
                return Task.FromResult(matchingRuns);
            });

        return new EvaluationAcceptanceGate(store.Object);
    }

    private static ExperimentRunRecord CreateRun(
        string experimentId,
        string metricsJson)
    {
        return new ExperimentRunRecord(
            Guid.NewGuid(),
            experimentId,
            "llm",
            "test-model",
            "prompt-v1",
            "golden",
            "v1",
            """{"Model":{"Type":"llm","Name":"test-model","PromptVersion":"prompt-v1","Provider":"test-provider","ModelVersion":"model-v1"},"Deterministic":true}""",
            10,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            metricsJson,
            null);
    }
}
