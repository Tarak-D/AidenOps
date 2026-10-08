using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class LlmEvaluationRunnerTests
{
    [Fact]
    public async Task RunAsync_Persists_llm_experiment_run()
    {
        var gateway =
            new FakeLlmEvaluationGateway();

        var store =
            new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset =
            CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "experiment-llm-001",
                new EvaluationModelMetadata(
                    "llm",
                    "test-model",
                    Provider: "nvidia",
                    PromptVersion: "v1"),
                dataset.Name,
                dataset.Version);

        var run =
            await runner.RunAsync(
                "dataset-001",
                dataset,
                configuration);

        Assert.NotEqual(Guid.Empty, run.Id);
        Assert.Equal(
            "experiment-llm-001",
            run.ExperimentId);
        Assert.Equal(
            "llm",
            run.ModelType);
        Assert.Equal(
            "test-model",
            run.ModelName);
        Assert.Equal(
            "golden",
            run.DatasetName);
        Assert.Equal(
            "1.0",
            run.DatasetVersion);
        Assert.Equal(
            1,
            run.SampleCount);

        Assert.Single(store.Runs);

        Assert.Contains(
            "\"DatasetId\":\"dataset-001\"",
            run.ConfigJson);

        Assert.Contains(
            "\"TriageAccuracy\":1",
            run.MetricsJson);
    }

    [Fact]
    public async Task RunAsync_MaxCases_limits_submitted_cases()
    {
        var gateway =
            new FakeLlmEvaluationGateway();

        var store =
            new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset =
            new EvaluationDataset(
                "golden",
                "1.0",
                [
                    CreateCase("case-1"),
                    CreateCase("case-2"),
                    CreateCase("case-3")
                ]);

        var configuration =
            new EvaluationRunConfiguration(
                "experiment-llm-002",
                new EvaluationModelMetadata(
                    "llm",
                    "test-model",
                    Provider: "openrouter",
                    PromptVersion: "v2"),
                dataset.Name,
                dataset.Version,
                MaxCases: 2);

        var run =
            await runner.RunAsync(
                "dataset-002",
                dataset,
                configuration);

        Assert.Equal(2, run.SampleCount);
        Assert.Equal(2, gateway.LastRequest!.Cases.Count);
        Assert.Equal(
            "case-1",
            gateway.LastRequest.Cases[0].CaseId);
        Assert.Equal(
            "case-2",
            gateway.LastRequest.Cases[1].CaseId);
    }

    [Fact]
    public async Task RunAsync_rejects_non_llm_model()
    {
        var runner =
            new LlmEvaluationRunner(
                new FakeLlmEvaluationGateway(),
                new FakeEvaluationStore());

        var dataset =
            CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "experiment-classical",
                new EvaluationModelMetadata(
                    "classical_ml",
                    "tfidf-logreg-v1"),
                dataset.Name,
                dataset.Version);

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                runner.RunAsync(
                    "dataset-003",
                    dataset,
                    configuration));
    }

    [Fact]
    public async Task RunAsync_rejects_mismatched_dataset_version()
    {
        var runner =
            new LlmEvaluationRunner(
                new FakeLlmEvaluationGateway(),
                new FakeEvaluationStore());

        var dataset =
            CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "experiment-llm-003",
                new EvaluationModelMetadata(
                    "llm",
                    "test-model",
                    Provider: "nvidia"),
                dataset.Name,
                "999.0");

        await Assert.ThrowsAsync<ArgumentException>(
            () =>
                runner.RunAsync(
                    "dataset-004",
                    dataset,
                    configuration));
    }

    private static EvaluationDataset CreateDataset()
    {
        return new EvaluationDataset(
            "golden",
            "1.0",
            [
                CreateCase("case-1")
            ]);
    }

    private static EvaluationCase CreateCase(
        string id)
    {
        var ticket =
            new AIOps.Contracts.AgentGateway.AgentTicketContext(
                Guid.NewGuid(),
                id,
                "VPN failure",
                "Corporate VPN is unavailable.",
                "test@example.com",
                AIOps.Domain.TicketDomain.Network,
                AIOps.Domain.Severity.P1,
                AIOps.Domain.TicketStatus.New);

        return new EvaluationCase(
            id,
            ticket,
            [],
            AIOps.Domain.TicketDomain.Network,
            AIOps.Domain.Severity.P1,
            AIOps.Contracts.AgentGateway.AgentRunOutcome.Resolved,
            null,
            "VPN issue evaluated.",
            false);
    }

    private sealed class FakeLlmEvaluationGateway
        : ILlmEvaluationGateway
    {
        public LlmEvaluationRequest? LastRequest { get; private set; }

        public Task<LlmEvaluationResponse> EvaluateAsync(
            LlmEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            var predictions =
                request.Cases
                    .Select(c =>
                        new LlmEvaluationPredictionResponse(
                            c.CaseId,
                            request.Provider,
                            "test-model",
                            "v1",
                            c.ExpectedDomain,
                            c.ExpectedSeverity,
                            0.95,
                            true,
                            true,
                            true,
                            40,
                            15,
                            55,
                            25))
                    .ToArray();

            var response =
                new LlmEvaluationResponse(
                    "llm",
                    new LlmEvaluationDatasetResponse(
                        request.DatasetId,
                        request.DatasetName,
                        request.DatasetVersion),
                    new LlmEvaluationModelResponse(
                        request.Provider,
                        "test-model",
                        "v1"),
                    predictions.Length,
                    new LlmEvaluationMetricsResponse(
                        1.0,
                        1.0,
                        1.0,
                        0.95,
                        predictions.Length * 25.0,
                        25.0,
                        predictions.Length * 40,
                        predictions.Length * 15,
                        predictions.Length * 55),
                    predictions);

            return Task.FromResult(response);
        }
    }

    private sealed class FakeEvaluationStore
        : IEvaluationStore
    {
        public List<ExperimentRunRecord> Runs { get; } = [];

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default)
        {
            Runs.Add(run);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default)
        {
            IReadOnlyList<ExperimentRunRecord> result =
                Runs
                    .Where(r =>
                        experimentId is null ||
                        r.ExperimentId == experimentId)
                    .Where(r =>
                        datasetName is null ||
                        r.DatasetName == datasetName)
                    .ToArray();

            return Task.FromResult(result);
        }
    }
}