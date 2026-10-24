using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Time;
using AIOps.Orchestration.Evaluation;
using Microsoft.Extensions.Options;
using Moq;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationAcceptanceServiceTests
{
    private static readonly DateTimeOffset EvaluatedAt =
        new(2026, 10, 24, 18, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task EvaluateAndPersistAsync_persists_accepted_candidate_and_policy_snapshot()
    {
        var baseline = CreateRun("baseline", """{"Accuracy":0.9}""");
        var candidate = CreateRun("candidate", """{"Accuracy":0.9}""");
        var store = new RecordingAcceptanceStore();
        var service = CreateService([baseline, candidate], store);

        var decision = await service.EvaluateAndPersistAsync(
            "baseline",
            "candidate");

        var record = Assert.Single(store.Records);
        Assert.Equal(decision.ResultId, record.Id);
        Assert.Equal(EvaluatedAt, record.EvaluatedAt);
        Assert.Equal(EvaluationAcceptanceStatus.Passed, record.Status);
        Assert.Equal(baseline.Id, record.BaselineRunId);
        Assert.Equal(candidate.Id, record.CandidateRunId);

        using var policy = JsonDocument.Parse(record.PolicyJson);
        Assert.Equal(
            "server-acceptance",
            policy.RootElement.GetProperty("Name").GetString());
        var rule = Assert.Single(
            policy.RootElement.GetProperty("RequiredMetrics")
                .EnumerateArray()
                .ToArray());
        Assert.Equal("Accuracy", rule.GetProperty("MetricName").GetString());
        Assert.Equal(
            "HigherIsBetter",
            rule.GetProperty("Direction").GetString());

        using var metrics = JsonDocument.Parse(record.MetricsJson);
        Assert.Equal(
            "Passed",
            metrics.RootElement[0].GetProperty("Status").GetString());
    }

    [Fact]
    public async Task EvaluateAndPersistAsync_persists_rejected_candidate_and_failure_reason()
    {
        var store = new RecordingAcceptanceStore();
        var service = CreateService(
            [
                CreateRun("baseline", """{"Accuracy":0.9}"""),
                CreateRun("candidate", """{"Accuracy":0.8}""")
            ],
            store);

        var decision = await service.EvaluateAndPersistAsync(
            "baseline",
            "candidate");

        var record = Assert.Single(store.Records);
        Assert.Equal(EvaluationAcceptanceStatus.Failed, decision.Result.Status);
        Assert.Equal(EvaluationAcceptanceStatus.Failed, record.Status);
        using var issues = JsonDocument.Parse(record.IssuesJson);
        Assert.Equal(
            "ThresholdExceeded",
            issues.RootElement[0].GetProperty("Code").GetString());
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("incompatible")]
    public async Task EvaluateAndPersistAsync_persists_fail_closed_results(
        string condition)
    {
        var baseline = CreateRun("baseline", """{"Accuracy":0.9}""");
        var candidate = condition == "incompatible"
            ? CreateRun("candidate", """{"Accuracy":0.9}""") with
            {
                DatasetVersion = "v2"
            }
            : CreateRun("candidate", """{"Accuracy":0.9}""");
        var runs = condition == "missing"
            ? [candidate]
            : new[] { baseline, candidate };
        var store = new RecordingAcceptanceStore();
        var service = CreateService(runs, store);

        var decision = await service.EvaluateAndPersistAsync(
            "baseline",
            "candidate");

        var record = Assert.Single(store.Records);
        Assert.Equal(EvaluationAcceptanceStatus.Blocked, decision.Result.Status);
        Assert.Equal(EvaluationAcceptanceStatus.Blocked, record.Status);
        Assert.NotEmpty(decision.Result.Issues);
        Assert.NotEmpty(record.IssuesJson);
    }

    [Fact]
    public async Task EvaluateAndPersistAsync_does_not_return_success_when_persistence_fails()
    {
        var store = new RecordingAcceptanceStore
        {
            Failure = new EvaluationAcceptancePersistenceException(
                "database write failed")
        };
        var service = CreateService(
            [
                CreateRun("baseline", """{"Accuracy":0.9}"""),
                CreateRun("candidate", """{"Accuracy":0.9}""")
            ],
            store);

        var exception =
            await Assert.ThrowsAsync<EvaluationAcceptancePersistenceException>(
                () => service.EvaluateAndPersistAsync(
                    "baseline",
                    "candidate"));

        Assert.Equal("database write failed", exception.Message);
    }

    [Fact]
    public async Task EvaluateAndPersistAsync_requires_server_configured_policy()
    {
        var store = new RecordingAcceptanceStore();
        var service = CreateService(
            [CreateRun("baseline", """{"Accuracy":0.9}""")],
            store,
            new EvaluationAcceptancePolicyOptions());

        await Assert.ThrowsAsync<EvaluationAcceptancePolicyConfigurationException>(
            () => service.EvaluateAndPersistAsync("baseline", "candidate"));

        Assert.Empty(store.Records);
    }

    [Theory]
    [InlineData("unsupported-direction")]
    [InlineData("negative-allowance")]
    [InlineData("missing-rules")]
    [InlineData("empty-rules")]
    public async Task EvaluateAndPersistAsync_rejects_invalid_server_policy_without_persisting(
        string invalidPolicy)
    {
        var store = new RecordingAcceptanceStore();
        var service = CreateService(
            [
                CreateRun("baseline", """{"Accuracy":0.9}"""),
                CreateRun("candidate", """{"Accuracy":0.9}""")
            ],
            store,
            CreateInvalidPolicyOptions(invalidPolicy));

        await Assert.ThrowsAsync<EvaluationAcceptancePolicyConfigurationException>(
            () => service.EvaluateAndPersistAsync("baseline", "candidate"));

        Assert.Empty(store.Records);
    }

    private static EvaluationAcceptanceService CreateService(
        IReadOnlyList<ExperimentRunRecord> runs,
        RecordingAcceptanceStore acceptanceStore,
        EvaluationAcceptancePolicyOptions? policyOptions = null)
    {
        var evaluationStore = new Mock<IEvaluationStore>();
        evaluationStore.Setup(store => store.ListRunsAsync(
                It.IsAny<string?>(),
                It.IsAny<string?>(),
                It.IsAny<CancellationToken>()))
            .Returns((
                string? experimentId,
                string? _,
                CancellationToken _) =>
                Task.FromResult<IReadOnlyList<ExperimentRunRecord>>(
                    runs.Where(run => run.ExperimentId == experimentId)
                        .ToArray()));

        var clock = new Mock<IClock>();
        clock.SetupGet(value => value.UtcNow).Returns(EvaluatedAt);

        return new EvaluationAcceptanceService(
            new EvaluationAcceptanceGate(evaluationStore.Object),
            acceptanceStore,
            Options.Create(policyOptions ?? CreatePolicyOptions()),
            clock.Object);
    }

    private static EvaluationAcceptancePolicyOptions CreatePolicyOptions() =>
        new()
        {
            Name = "server-acceptance",
            RequiredMetrics =
            [
                new EvaluationAcceptanceMetricRuleOptions
                {
                    MetricName = "Accuracy",
                    Direction = "HigherIsBetter",
                    AllowedRegression = 0.01m
                }
            ]
        };

    private static EvaluationAcceptancePolicyOptions CreateInvalidPolicyOptions(
        string invalidPolicy)
    {
        var options = CreatePolicyOptions();
        switch (invalidPolicy)
        {
            case "unsupported-direction":
                options.RequiredMetrics![0].Direction = "Sideways";
                break;
            case "negative-allowance":
                options.RequiredMetrics![0].AllowedRegression = -0.01m;
                break;
            case "missing-rules":
                options.RequiredMetrics = null;
                break;
            case "empty-rules":
                options.RequiredMetrics = [];
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(invalidPolicy));
        }

        return options;
    }

    private static ExperimentRunRecord CreateRun(
        string experimentId,
        string metricsJson) =>
        new(
            Guid.NewGuid(),
            experimentId,
            "classical_ml",
            "model-a",
            "prompt-v1",
            "golden",
            "v1",
            """{"Deterministic":true}""",
            10,
            EvaluatedAt.AddMinutes(-1),
            EvaluatedAt,
            metricsJson,
            null);

    private sealed class RecordingAcceptanceStore : IEvaluationAcceptanceStore
    {
        public List<EvaluationAcceptanceRecord> Records { get; } = [];

        public Exception? Failure { get; init; }

        public Task RecordAsync(
            EvaluationAcceptanceRecord result,
            CancellationToken cancellationToken = default)
        {
            if (Failure is not null)
            {
                throw Failure;
            }

            Records.Add(result);
            return Task.CompletedTask;
        }
    }
}
