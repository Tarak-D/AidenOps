using System.Security.Cryptography;
using System.Text;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Time;
using AIOps.Orchestration.Evaluation;
using Microsoft.Extensions.Options;
using Moq;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationAcceptanceHarnessTests
{
    private static readonly byte[] DatasetBytes = Encoding.UTF8.GetBytes(
        """{"dataset":"reviewed-v1"}""");
    private static readonly byte[] CorpusBytes = Encoding.UTF8.GetBytes(
        """{"corpus":"reviewed-v1"}""");
    private const string BaselineRevision =
        "1111111111111111111111111111111111111111";
    private const string CandidateRevision =
        "2222222222222222222222222222222222222222";

    [Fact]
    public async Task EvaluateAsync_returns_passed_decision_after_verified_readback()
    {
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"),
             CreateRun("candidate", CandidateRevision, "0.9")]);

        var decision = await harness.EvaluateAsync(request);

        Assert.Equal(EvaluationAcceptanceStatus.Passed, decision.Result.Status);
        Assert.Equal(decision.ResultId, Assert.Single(acceptanceStore.Records).Id);
    }

    [Fact]
    public async Task EvaluateAsync_fails_when_gate_rejects_candidate()
    {
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"),
             CreateRun("candidate", CandidateRevision, "0.8")]);

        var exception =
            await Assert.ThrowsAsync<EvaluationAcceptanceHarnessException>(
                () => harness.EvaluateAsync(request));

        Assert.Contains("Failed", exception.Message);
        Assert.Equal(
            EvaluationAcceptanceStatus.Failed,
            Assert.Single(acceptanceStore.Records).Status);
    }

    [Theory]
    [InlineData("baseline", 0)]
    [InlineData("candidate", 0)]
    [InlineData("baseline", 2)]
    [InlineData("candidate", 2)]
    public async Task EvaluateAsync_fails_closed_unless_each_experiment_has_exactly_one_run(
        string experimentId,
        int runCount)
    {
        var runs = new List<ExperimentRunRecord>
        {
            CreateRun("baseline", BaselineRevision, "0.9"),
            CreateRun("candidate", CandidateRevision, "0.9")
        };

        if (runCount == 0)
        {
            runs.RemoveAll(run => run.ExperimentId == experimentId);
        }
        else
        {
            runs.Add(CreateRun(experimentId, experimentId == "baseline"
                ? BaselineRevision
                : CandidateRevision, "0.9"));
        }

        var (harness, acceptanceStore, _, request) = CreateHarness(runs);

        var exception =
            await Assert.ThrowsAsync<EvaluationAcceptanceHarnessException>(
                () => harness.EvaluateAsync(request));

        Assert.Contains("exactly one", exception.Message);
        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task EvaluateAsync_fails_closed_for_incompatible_metadata()
    {
        var candidate = CreateRun("candidate", CandidateRevision, "0.9") with
        {
            DatasetVersion = "different-version"
        };
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"), candidate]);

        var exception =
            await Assert.ThrowsAsync<EvaluationAcceptanceHarnessException>(
                () => harness.EvaluateAsync(request));

        Assert.Contains("Blocked", exception.Message);
        Assert.Equal(
            EvaluationAcceptanceStatus.Blocked,
            Assert.Single(acceptanceStore.Records).Status);
    }

    [Fact]
    public async Task EvaluateAsync_fails_when_policy_is_unavailable()
    {
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"),
             CreateRun("candidate", CandidateRevision, "0.9")],
            policy: new EvaluationAcceptancePolicyOptions());

        await Assert.ThrowsAsync<EvaluationAcceptancePolicyConfigurationException>(
            () => harness.EvaluateAsync(request));
        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task EvaluateAsync_fails_when_evaluation_data_is_unavailable()
    {
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"),
             CreateRun("candidate", CandidateRevision, "0.9")],
            evaluationStoreFailure:
                new EvaluationStoreUnavailableException("store unavailable"));

        await Assert.ThrowsAsync<EvaluationStoreUnavailableException>(
            () => harness.EvaluateAsync(request));
        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task EvaluateAsync_fails_when_acceptance_persistence_fails()
    {
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"),
             CreateRun("candidate", CandidateRevision, "0.9")],
            acceptanceStoreFailure:
                new EvaluationAcceptancePersistenceException("write failed"));

        await Assert.ThrowsAsync<EvaluationAcceptancePersistenceException>(
            () => harness.EvaluateAsync(request));
        Assert.Empty(acceptanceStore.Records);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("mismatch")]
    public async Task EvaluateAsync_fails_when_acceptance_readback_fails(
        string failure)
    {
        var (harness, _, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"),
             CreateRun("candidate", CandidateRevision, "0.9")],
            readBackFailure: failure);

        var exception =
            await Assert.ThrowsAsync<EvaluationAcceptanceHarnessException>(
                () => harness.EvaluateAsync(request));

        Assert.Contains("persisted acceptance result", exception.Message);
    }

    [Fact]
    public async Task EvaluateAsync_rejects_run_provenance_that_does_not_match_bytes()
    {
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [CreateRun("baseline", BaselineRevision, "0.9"),
             CreateRun("candidate", CandidateRevision, "0.9")]);
        var changedRequest = request with
        {
            DatasetBytes = Encoding.UTF8.GetBytes("different dataset bytes")
        };

        await Assert.ThrowsAsync<EvaluationAcceptanceHarnessException>(
            () => harness.EvaluateAsync(changedRequest));
        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task EvaluateAsync_rejects_run_from_another_revision()
    {
        var baseline = CreateRun("baseline", CandidateRevision, "0.9");
        var (harness, acceptanceStore, _, request) = CreateHarness(
            [baseline, CreateRun("candidate", CandidateRevision, "0.9")]);

        await Assert.ThrowsAsync<EvaluationAcceptanceHarnessException>(
            () => harness.EvaluateAsync(request));
        Assert.Empty(acceptanceStore.Records);
    }

    [Theory]
    [InlineData("baseline")]
    [InlineData("candidate")]
    public async Task EvaluateAsync_rejects_failed_decision_for_different_requested_run_ids(
        string side)
    {
        var runs = new[]
        {
            CreateRun("baseline", BaselineRevision, "0.9"),
            CreateRun("candidate", CandidateRevision, "0.8")
        };
        var gateRuns = new[]
        {
            side == "baseline"
                ? runs[0] with
                {
                    Id = Guid.Parse("33333333-3333-3333-3333-333333333333")
                }
                : runs[0],
            side == "candidate"
                ? runs[1] with
                {
                    Id = Guid.Parse("44444444-4444-4444-4444-444444444444")
                }
                : runs[1]
        };
        var (harness, acceptanceStore, _, request) = CreateHarness(
            runs,
            gateRuns: gateRuns);

        var exception =
            await Assert.ThrowsAsync<EvaluationAcceptanceHarnessException>(
                () => harness.EvaluateAsync(request));

        Assert.Contains("requested", exception.Message);
        var persisted = Assert.Single(acceptanceStore.Records);
        Assert.Equal(EvaluationAcceptanceStatus.Failed, persisted.Status);
        Assert.NotEqual(
            side == "baseline"
                ? request.Baseline.RunId
                : request.Candidate.RunId,
            side == "baseline"
                ? persisted.BaselineRunId
                : persisted.CandidateRunId);
    }

    private static (
        EvaluationAcceptanceHarness Harness,
        RecordingAcceptanceStore AcceptanceStore,
        FakeEvaluationStore EvaluationStore,
        EvaluationAcceptanceHarnessRequest Request) CreateHarness(
        IReadOnlyList<ExperimentRunRecord> runs,
        EvaluationAcceptancePolicyOptions? policy = null,
        Exception? evaluationStoreFailure = null,
        Exception? acceptanceStoreFailure = null,
        string? readBackFailure = null,
        IReadOnlyList<ExperimentRunRecord>? gateRuns = null)
    {
        var options = policy ?? CreatePolicy();
        var evaluationStore = new FakeEvaluationStore(
            runs,
            evaluationStoreFailure,
            gateRuns);
        var acceptanceStore = new RecordingAcceptanceStore(
            acceptanceStoreFailure,
            readBackFailure);
        var clock = new Mock<IClock>();
        clock.SetupGet(value => value.UtcNow)
            .Returns(DateTimeOffset.UnixEpoch);
        var service = new EvaluationAcceptanceService(
            new EvaluationAcceptanceGate(evaluationStore),
            acceptanceStore,
            Options.Create(options),
            clock.Object);
        var harness = new EvaluationAcceptanceHarness(
            evaluationStore,
            acceptanceStore,
            service,
            Options.Create(options));

        return (
            harness,
            acceptanceStore,
            evaluationStore,
            CreateRequest());
    }

    private static EvaluationAcceptancePolicyOptions CreatePolicy() =>
        new()
        {
            Name = "reviewed-policy-v1",
            RequiredMetrics =
            [
                new EvaluationAcceptanceMetricRuleOptions
                {
                    MetricName = "Accuracy",
                    Direction = "HigherIsBetter",
                    AllowedRegression = 0.05m
                }
            ]
        };

    private static EvaluationAcceptanceHarnessRequest CreateRequest() =>
        new(
            new EvaluationRevisionRunInput(
                BaselineRevision,
                "baseline",
                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                Hash(DatasetBytes),
                Hash(CorpusBytes)),
            new EvaluationRevisionRunInput(
                CandidateRevision,
                "candidate",
                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                Hash(DatasetBytes),
                Hash(CorpusBytes)),
            DatasetBytes,
            CorpusBytes);

    private static ExperimentRunRecord CreateRun(
        string experimentId,
        string revision,
        string accuracy) =>
        new(
            experimentId == "baseline"
                ? Guid.Parse("11111111-1111-1111-1111-111111111111")
                : Guid.Parse("22222222-2222-2222-2222-222222222222"),
            experimentId,
            "classical_ml",
            "fixed-model",
            "prompt-v1",
            "reviewed-dataset",
            "1.0.0",
            """{"ExecutionMode":"deterministic-evaluation","Deterministic":true}""",
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch,
            $$"""{"Accuracy":{{accuracy}}}""",
            null,
            revision,
            Hash(DatasetBytes),
            Hash(CorpusBytes));

    private static string Hash(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private sealed class FakeEvaluationStore(
        IReadOnlyList<ExperimentRunRecord> runs,
        Exception? failure,
        IReadOnlyList<ExperimentRunRecord>? gateRuns) : IEvaluationStore
    {
        private int _listCalls;

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default)
        {
            if (failure is not null)
            {
                throw failure;
            }

            var sourceRuns = _listCalls++ < 2 || gateRuns is null
                ? runs
                : gateRuns;
            return Task.FromResult<IReadOnlyList<ExperimentRunRecord>>(
                sourceRuns.Where(run =>
                        (experimentId is null ||
                         run.ExperimentId == experimentId) &&
                        (datasetName is null ||
                         run.DatasetName == datasetName))
                    .ToArray());
        }
    }

    private sealed class RecordingAcceptanceStore(
        Exception? failure,
        string? readBackFailure) : IEvaluationAcceptanceStore
    {
        public List<EvaluationAcceptanceRecord> Records { get; } = [];

        public Task RecordAsync(
            EvaluationAcceptanceRecord result,
            CancellationToken cancellationToken = default)
        {
            if (failure is not null)
            {
                throw failure;
            }

            Records.Add(result);
            return Task.CompletedTask;
        }

        public Task<EvaluationAcceptanceRecord?> GetAsync(
            Guid resultId,
            CancellationToken cancellationToken = default)
        {
            if (readBackFailure == "missing")
            {
                return Task.FromResult<EvaluationAcceptanceRecord?>(null);
            }

            var result = Records.SingleOrDefault(record => record.Id == resultId);
            if (readBackFailure == "mismatch" && result is not null)
            {
                result = result with { Status = EvaluationAcceptanceStatus.Failed };
            }

            return Task.FromResult(result);
        }
    }
}
