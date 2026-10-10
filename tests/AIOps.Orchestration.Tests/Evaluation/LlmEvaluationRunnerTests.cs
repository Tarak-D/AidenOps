using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;
using AIOps.Orchestration.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class LlmEvaluationRunnerTests
{
    [Fact]
    public async Task RunAsync_Persists_llm_experiment_run()
    {
        var gateway = new FakeLlmEvaluationGateway();
        var store = new FakeEvaluationStore();
        var runner = new LlmEvaluationRunner(gateway, store);
        var dataset = CreateDataset();

        var configuration = new EvaluationRunConfiguration(
            "experiment-llm-001",
            new EvaluationModelMetadata(
                "llm",
                "test-model",
                Provider: "nvidia",
                ModelVersion: "model-version-2026-01",
                PromptVersion: "v1"),
            dataset.Name,
            dataset.Version);

        var run = await runner.RunAsync(
            "dataset-001",
            dataset,
            configuration);

        Assert.NotEqual(Guid.Empty, run.Id);
        Assert.Equal("experiment-llm-001", run.ExperimentId);
        Assert.Equal("llm", run.ModelType);
        Assert.Equal("test-model", run.ModelName);
        Assert.Equal("golden", run.DatasetName);
        Assert.Equal("1.0", run.DatasetVersion);
        Assert.Equal(1, run.SampleCount);
        Assert.Single(store.Runs);

        using var configDocument = JsonDocument.Parse(run.ConfigJson);
        var model = configDocument.RootElement.GetProperty("Model");

        Assert.Equal(
            "model-version-2026-01",
            model.GetProperty("ModelVersion").GetString());

        Assert.Equal("nvidia", model.GetProperty("Provider").GetString());
        Assert.Equal("test-model", model.GetProperty("Name").GetString());
        Assert.Equal("v1", model.GetProperty("PromptVersion").GetString());

        using var metricsDocument = JsonDocument.Parse(run.MetricsJson);
        var metrics = metricsDocument.RootElement;

        Assert.Equal(1, metrics.GetProperty("TriageAccuracy").GetDouble());
        Assert.Equal(25, metrics.GetProperty("AverageLatencyMs").GetDouble());
        Assert.Equal(25, metrics.GetProperty("TotalLatencyMs").GetDouble());
        Assert.Equal(40, metrics.GetProperty("PromptTokens").GetInt32());
        Assert.Equal(15, metrics.GetProperty("CompletionTokens").GetInt32());
        Assert.Equal(55, metrics.GetProperty("TotalTokens").GetInt32());
    }

    [Fact]
    public async Task RunAsync_MaxCases_limits_submitted_cases()
    {
        var gateway = new FakeLlmEvaluationGateway();
        var store = new FakeEvaluationStore();
        var runner = new LlmEvaluationRunner(gateway, store);

        var dataset = new EvaluationDataset(
            "golden",
            "1.0",
            [
                CreateCase("case-1"),
                CreateCase("case-2"),
                CreateCase("case-3")
            ]);

        var configuration = new EvaluationRunConfiguration(
            "experiment-llm-002",
            new EvaluationModelMetadata(
                "llm",
                "test-model",
                Provider: "openrouter",
                PromptVersion: "v2"),
            dataset.Name,
            dataset.Version,
            MaxCases: 2);

        var run = await runner.RunAsync(
            "dataset-002",
            dataset,
            configuration);

        Assert.Equal(2, run.SampleCount);
        Assert.Equal(2, gateway.LastRequest!.Cases.Count);
        Assert.Equal("case-1", gateway.LastRequest.Cases[0].CaseId);
        Assert.Equal("case-2", gateway.LastRequest.Cases[1].CaseId);
    }

    [Fact]
    public async Task RunAsync_rejects_non_llm_model()
    {
        var runner = new LlmEvaluationRunner(
            new FakeLlmEvaluationGateway(),
            new FakeEvaluationStore());

        var dataset = CreateDataset();

        var configuration = new EvaluationRunConfiguration(
            "experiment-classical",
            new EvaluationModelMetadata(
                "classical_ml",
                "tfidf-logreg-v1"),
            dataset.Name,
            dataset.Version);

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(
                "dataset-003",
                dataset,
                configuration));
    }

    [Fact]
    public async Task RunAsync_rejects_mismatched_dataset_version()
    {
        var runner = new LlmEvaluationRunner(
            new FakeLlmEvaluationGateway(),
            new FakeEvaluationStore());

        var dataset = CreateDataset();

        var configuration = new EvaluationRunConfiguration(
            "experiment-llm-003",
            new EvaluationModelMetadata(
                "llm",
                "test-model",
                Provider: "nvidia"),
            dataset.Name,
            "999.0");

        await Assert.ThrowsAsync<ArgumentException>(
            () => runner.RunAsync(
                "dataset-004",
                dataset,
                configuration));
    }

    [Fact]
    public async Task RunAsync_uses_configured_prompt_version_when_response_version_is_empty()
    {
        var gateway = new FakeLlmEvaluationGateway(
            responsePromptVersion: "");
        var store = new FakeEvaluationStore();
        var runner = new LlmEvaluationRunner(gateway, store);
        var dataset = CreateDataset();

        var configuration = new EvaluationRunConfiguration(
            "experiment-llm-prompt-fallback",
            new EvaluationModelMetadata(
                "llm",
                "test-model",
                Provider: "nvidia",
                ModelVersion: "model-version-1",
                PromptVersion: "configured-prompt-v2"),
            dataset.Name,
            dataset.Version);

        var run = await runner.RunAsync(
            "dataset-prompt-fallback",
            dataset,
            configuration);

        Assert.Equal("configured-prompt-v2", run.PromptVersion);

        using var document = JsonDocument.Parse(run.ConfigJson);
        var model = document.RootElement.GetProperty("Model");

        Assert.Equal(
            "configured-prompt-v2",
            model.GetProperty("PromptVersion").GetString());
    }

    [Fact]
    public async Task RunAsync_persists_estimated_cost_when_pricing_is_configured()
    {
        var gateway = new FakeLlmEvaluationGateway();
        var store = new FakeEvaluationStore();
        var runner = new LlmEvaluationRunner(gateway, store);
        var dataset = CreateDataset();

        var configuration = new EvaluationRunConfiguration(
            "experiment-llm-cost",
            new EvaluationModelMetadata(
                "llm",
                "test-model",
                Provider: "nvidia",
                InputPricePerMillionTokens: 2m,
                OutputPricePerMillionTokens: 8m),
            dataset.Name,
            dataset.Version);

        var run = await runner.RunAsync(
            "dataset-cost",
            dataset,
            configuration);

        using var metricsDocument = JsonDocument.Parse(run.MetricsJson);
        var metrics = metricsDocument.RootElement;

        Assert.Equal(
            0.0002m,
            metrics.GetProperty("EstimatedCost").GetDecimal());

        Assert.Equal(
            "USD",
            metrics.GetProperty("CostCurrency").GetString());

        using var configDocument = JsonDocument.Parse(run.ConfigJson);
        var pricing = configDocument.RootElement.GetProperty("Pricing");

        Assert.Equal(
            2m,
            pricing.GetProperty("InputPricePerMillionTokens").GetDecimal());

        Assert.Equal(
            8m,
            pricing.GetProperty("OutputPricePerMillionTokens").GetDecimal());
    }

    [Fact]
    public async Task RunAsync_leaves_estimated_cost_null_when_pricing_is_not_configured()
    {
        var gateway = new FakeLlmEvaluationGateway();
        var store = new FakeEvaluationStore();
        var runner = new LlmEvaluationRunner(gateway, store);
        var dataset = CreateDataset();

        var configuration = new EvaluationRunConfiguration(
            "experiment-llm-no-pricing",
            new EvaluationModelMetadata(
                "llm",
                "test-model",
                Provider: "nvidia"),
            dataset.Name,
            dataset.Version);

        var run = await runner.RunAsync(
            "dataset-no-pricing",
            dataset,
            configuration);

        using var metricsDocument = JsonDocument.Parse(run.MetricsJson);
        var metrics = metricsDocument.RootElement;

        Assert.Equal(
            JsonValueKind.Null,
            metrics.GetProperty("EstimatedCost").ValueKind);

        Assert.Equal(
            JsonValueKind.Null,
            metrics.GetProperty("CostCurrency").ValueKind);
    }

    private static EvaluationDataset CreateDataset()
    {
        return new EvaluationDataset(
            "golden",
            "1.0",
            [CreateCase("case-1")]);
    }

    private static EvaluationCase CreateCase(string id)
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

    private sealed class FakeLlmEvaluationGateway : ILlmEvaluationGateway
    {
        private readonly string _responsePromptVersion;

        public FakeLlmEvaluationGateway(
            string responsePromptVersion = "v1")
        {
            _responsePromptVersion = responsePromptVersion;
        }

        public LlmEvaluationRequest? LastRequest { get; private set; }

        public Task<LlmEvaluationResponse> EvaluateAsync(
            LlmEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            LastRequest = request;

            var predictions = request.Cases
                .Select(c => new LlmEvaluationPredictionResponse(
                    c.CaseId,
                    request.Provider,
                    "test-model",
                    _responsePromptVersion,
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

            var response = new LlmEvaluationResponse(
                "llm",
                new LlmEvaluationDatasetResponse(
                    request.DatasetId,
                    request.DatasetName,
                    request.DatasetVersion),
                new LlmEvaluationModelResponse(
                    request.Provider,
                    "test-model",
                    _responsePromptVersion),
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

    private sealed class FakeEvaluationStore : IEvaluationStore
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
            IReadOnlyList<ExperimentRunRecord> result = Runs
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