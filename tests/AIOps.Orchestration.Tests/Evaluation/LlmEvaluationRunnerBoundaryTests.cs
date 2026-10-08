using System.Text.Json;

using AIOps.Abstractions.Evaluation;

using AIOps.Contracts.Evaluation;

using AIOps.Domain;

using AIOps.Orchestration.Evaluation;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class LlmEvaluationRunnerBoundaryTests
{
    [Fact]
    public async Task RunAsync_sends_dataset_to_gateway_and_persists_response()
    {
        var gateway = new FakeLlmEvaluationGateway();
        var store = new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset = CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "phase15.4-boundary-test",
                new EvaluationModelMetadata(
                    "llm",
                    "moonshotai/kimi-k3",
                    "nvidia",
                    "test-model-version",
                    "prompt-v15.4"),
                dataset.Name,
                dataset.Version,
                MaxCases: 1,
                Deterministic: true,
                ExecuteExternalTools: false,
                Notes: "Phase 15.4 boundary test.");

        var run =
            await runner.RunAsync(
                "golden-dataset-boundary",
                dataset,
                configuration);

        Assert.NotNull(gateway.ReceivedRequest);

        Assert.Equal(
            "golden-dataset-boundary",
            gateway.ReceivedRequest!.DatasetId);

        Assert.Equal(
            dataset.Name,
            gateway.ReceivedRequest.DatasetName);

        Assert.Equal(
            dataset.Version,
            gateway.ReceivedRequest.DatasetVersion);

        Assert.Equal(
            "nvidia",
            gateway.ReceivedRequest.Provider);

        Assert.Single(
            gateway.ReceivedRequest.Cases);

        Assert.Equal(
            dataset.Cases[0].Id,
            gateway.ReceivedRequest.Cases[0].CaseId);

        Assert.Equal(
            dataset.Cases[0].Ticket.Title,
            gateway.ReceivedRequest.Cases[0].Title);

        Assert.Equal(
            dataset.Cases[0].Ticket.Description,
            gateway.ReceivedRequest.Cases[0].Description);

        Assert.Equal(
            dataset.Cases[0].ExpectedDomain.ToString(),
            gateway.ReceivedRequest.Cases[0].ExpectedDomain);

        Assert.Equal(
            dataset.Cases[0].ExpectedSeverity.ToString(),
            gateway.ReceivedRequest.Cases[0].ExpectedSeverity);

        Assert.NotNull(store.RecordedRun);

        Assert.Equal(
            run.Id,
            store.RecordedRun!.Id);

        Assert.Equal(
            "llm",
            run.ModelType);

        Assert.Equal(
            "moonshotai/kimi-k3",
            run.ModelName);

        Assert.Equal(
            dataset.Name,
            run.DatasetName);

        Assert.Equal(
            dataset.Version,
            run.DatasetVersion);

        Assert.Equal(
            1,
            run.SampleCount);

        Assert.Contains(
            "Phase 15.4 boundary test.",
            run.Notes);
    }

    [Fact]
    public async Task RunAsync_rejects_response_with_different_provider()
    {
        var gateway = new FakeLlmEvaluationGateway
        {
            Response =
                new LlmEvaluationResponse(
                    "llm",
                    new LlmEvaluationDatasetResponse(
                        "golden-dataset-boundary",
                        "phase15-golden",
                        "v1"),
                    new LlmEvaluationModelResponse(
                        "openai",
                        "different-model",
                        "prompt-v15.4"),
                    1,
                    new LlmEvaluationMetricsResponse(
                        1.0,
                        1.0,
                        1.0,
                        0.90,
                        100.0,
                        100.0,
                        50,
                        10,
                        60),
                    new[]
                    {
                        new LlmEvaluationPredictionResponse(
                            "cpu-spike",
                            "openai",
                            "different-model",
                            "prompt-v15.4",
                            "Infrastructure",
                            "P1",
                            0.90,
                            true,
                            true,
                            true,
                            50,
                            10,
                            60,
                            100.0)
                    })
        };

        var store = new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset = CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "phase15.4-provider-mismatch",
                new EvaluationModelMetadata(
                    "llm",
                    "moonshotai/kimi-k3",
                    "nvidia",
                    PromptVersion: "prompt-v15.4"),
                dataset.Name,
                dataset.Version);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(
                "golden-dataset-boundary",
                dataset,
                configuration));

        Assert.Null(store.RecordedRun);
    }

    [Fact]
    public async Task RunAsync_rejects_response_with_different_dataset_id()
    {
        var gateway = new FakeLlmEvaluationGateway
        {
            Response =
                new LlmEvaluationResponse(
                    "llm",
                    new LlmEvaluationDatasetResponse(
                        "wrong-dataset-id",
                        "phase15-golden",
                        "v1"),
                    new LlmEvaluationModelResponse(
                        "nvidia",
                        "moonshotai/kimi-k3",
                        "prompt-v15.4"),
                    1,
                    new LlmEvaluationMetricsResponse(
                        1.0,
                        1.0,
                        1.0,
                        0.90,
                        100.0,
                        100.0,
                        50,
                        10,
                        60),
                    new[]
                    {
                        new LlmEvaluationPredictionResponse(
                            "cpu-spike",
                            "nvidia",
                            "moonshotai/kimi-k3",
                            "prompt-v15.4",
                            "Infrastructure",
                            "P1",
                            0.90,
                            true,
                            true,
                            true,
                            50,
                            10,
                            60,
                            100.0)
                    })
        };

        var store = new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset = CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "phase15.4-dataset-id-mismatch",
                new EvaluationModelMetadata(
                    "llm",
                    "moonshotai/kimi-k3",
                    "nvidia",
                    PromptVersion: "prompt-v15.4"),
                dataset.Name,
                dataset.Version);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(
                "golden-dataset-boundary",
                dataset,
                configuration));

        Assert.Null(store.RecordedRun);
    }

    [Fact]
    public async Task RunAsync_rejects_response_with_wrong_sample_count()
    {
        var gateway = new FakeLlmEvaluationGateway
        {
            Response =
                new LlmEvaluationResponse(
                    "llm",
                    new LlmEvaluationDatasetResponse(
                        "golden-dataset-boundary",
                        "phase15-golden",
                        "v1"),
                    new LlmEvaluationModelResponse(
                        "nvidia",
                        "moonshotai/kimi-k3",
                        "prompt-v15.4"),
                    2,
                    new LlmEvaluationMetricsResponse(
                        1.0,
                        1.0,
                        1.0,
                        0.90,
                        100.0,
                        100.0,
                        50,
                        10,
                        60),
                    new[]
                    {
                        new LlmEvaluationPredictionResponse(
                            "cpu-spike",
                            "nvidia",
                            "moonshotai/kimi-k3",
                            "prompt-v15.4",
                            "Infrastructure",
                            "P1",
                            0.90,
                            true,
                            true,
                            true,
                            50,
                            10,
                            60,
                            100.0)
                    })
        };

        var store = new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset = CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "phase15.4-sample-count-mismatch",
                new EvaluationModelMetadata(
                    "llm",
                    "moonshotai/kimi-k3",
                    "nvidia",
                    PromptVersion: "prompt-v15.4"),
                dataset.Name,
                dataset.Version);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(
                "golden-dataset-boundary",
                dataset,
                configuration));

        Assert.Null(store.RecordedRun);
    }

    [Fact]
    public async Task RunAsync_rejects_response_with_wrong_prediction_count()
    {
        var gateway = new FakeLlmEvaluationGateway
        {
            Response =
                new LlmEvaluationResponse(
                    "llm",
                    new LlmEvaluationDatasetResponse(
                        "golden-dataset-boundary",
                        "phase15-golden",
                        "v1"),
                    new LlmEvaluationModelResponse(
                        "nvidia",
                        "moonshotai/kimi-k3",
                        "prompt-v15.4"),
                    1,
                    new LlmEvaluationMetricsResponse(
                        1.0,
                        1.0,
                        1.0,
                        0.90,
                        100.0,
                        100.0,
                        50,
                        10,
                        60),
                    Array.Empty<LlmEvaluationPredictionResponse>())
        };

        var store = new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset = CreateDataset();

        var configuration =
            new EvaluationRunConfiguration(
                "phase15.4-prediction-count-mismatch",
                new EvaluationModelMetadata(
                    "llm",
                    "moonshotai/kimi-k3",
                    "nvidia",
                    PromptVersion: "prompt-v15.4"),
                dataset.Name,
                dataset.Version);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync(
                "golden-dataset-boundary",
                dataset,
                configuration));

        Assert.Null(store.RecordedRun);
    }

    [Fact]
    public async Task RunAsync_persists_llm_metrics_and_predictions()
    {
        var gateway = new FakeLlmEvaluationGateway
        {
            Response =
                new LlmEvaluationResponse(
                    "llm",
                    new LlmEvaluationDatasetResponse(
                        "golden-dataset-metrics",
                        "phase15-golden",
                        "v1"),
                    new LlmEvaluationModelResponse(
                        "nvidia",
                        "moonshotai/kimi-k3",
                        "prompt-v15.4"),
                    1,
                    new LlmEvaluationMetricsResponse(
                        DomainAccuracy: 1.0,
                        SeverityAccuracy: 1.0,
                        TriageAccuracy: 1.0,
                        AverageConfidence: 0.91,
                        TotalLatencyMs: 120.5,
                        AverageLatencyMs: 120.5,
                        PromptTokens: 80,
                        CompletionTokens: 20,
                        TotalTokens: 100),
                    new[]
                    {
                        new LlmEvaluationPredictionResponse(
                            "cpu-spike",
                            "nvidia",
                            "moonshotai/kimi-k3",
                            "prompt-v15.4",
                            "Infrastructure",
                            "P1",
                            0.91,
                            true,
                            true,
                            true,
                            80,
                            20,
                            100,
                            120.5)
                    })
        };

        var store = new FakeEvaluationStore();

        var runner =
            new LlmEvaluationRunner(
                gateway,
                store);

        var dataset =
            CreateDataset(
                datasetId: "golden-dataset-metrics");

        var configuration =
            new EvaluationRunConfiguration(
                "phase15.4-metrics-test",
                new EvaluationModelMetadata(
                    "llm",
                    "moonshotai/kimi-k3",
                    "nvidia",
                    "test-model-version",
                    "prompt-v15.4"),
                dataset.Name,
                dataset.Version,
                MaxCases: 1);

        var run =
            await runner.RunAsync(
                "golden-dataset-metrics",
                dataset,
                configuration);

        Assert.NotNull(store.RecordedRun);

        using var document =
            JsonDocument.Parse(
                run.MetricsJson);

        var root =
            document.RootElement;

        Assert.Equal(
            1,
            root.GetProperty("SampleCount").GetInt32());

        Assert.Equal(
            1.0,
            root.GetProperty("DomainAccuracy").GetDouble());

        Assert.Equal(
            1.0,
            root.GetProperty("SeverityAccuracy").GetDouble());

        Assert.Equal(
            1.0,
            root.GetProperty("TriageAccuracy").GetDouble());

        Assert.Equal(
            0.91,
            root.GetProperty("AverageConfidence").GetDouble());

        Assert.Equal(
            120.5,
            root.GetProperty("TotalLatencyMs").GetDouble());

        Assert.Equal(
            80,
            root.GetProperty("PromptTokens").GetInt32());

        Assert.Equal(
            20,
            root.GetProperty("CompletionTokens").GetInt32());

        Assert.Equal(
            100,
            root.GetProperty("TotalTokens").GetInt32());

        Assert.Single(
            root.GetProperty("Predictions").EnumerateArray());

        var prediction =
            root.GetProperty("Predictions")[0];

        Assert.Equal(
            "cpu-spike",
            prediction.GetProperty("CaseId").GetString());

        Assert.Equal(
            "moonshotai/kimi-k3",
            prediction.GetProperty("Model").GetString());

        Assert.True(
            prediction.GetProperty("TriageCorrect").GetBoolean());
    }

    private static EvaluationDataset CreateDataset(
        string datasetId = "golden-dataset-boundary")
    {
        var ticket =
            new AIOps.Contracts.AgentGateway.AgentTicketContext(
                Guid.NewGuid(),
                "cpu-spike",
                "CPU spike detected",
                "CPU usage is above 95 percent.",
                "test@example.com",
                TicketDomain.Infrastructure,
                Severity.P1,
                TicketStatus.New);

        var evaluationCase =
            new EvaluationCase(
                "cpu-spike",
                ticket,
                Array.Empty<AIOps.Contracts.AgentGateway.ToolManifestEntry>(),
                TicketDomain.Infrastructure,
                Severity.P1,
                AIOps.Contracts.AgentGateway.AgentRunOutcome.Resolved,
                null,
                "CPU spike resolved.",
                false);

        return new EvaluationDataset(
            "phase15-golden",
            "v1",
            new[] { evaluationCase });
    }

    private sealed class FakeLlmEvaluationGateway
        : ILlmEvaluationGateway
    {
        public LlmEvaluationRequest? ReceivedRequest { get; private set; }

        public LlmEvaluationResponse Response { get; set; } =
            new(
                "llm",
                new LlmEvaluationDatasetResponse(
                    "golden-dataset-boundary",
                    "phase15-golden",
                    "v1"),
                new LlmEvaluationModelResponse(
                    "nvidia",
                    "moonshotai/kimi-k3",
                    "prompt-v15.4"),
                1,
                new LlmEvaluationMetricsResponse(
                    1.0,
                    1.0,
                    1.0,
                    0.90,
                    100.0,
                    100.0,
                    50,
                    10,
                    60),
                new[]
                {
                    new LlmEvaluationPredictionResponse(
                        "cpu-spike",
                        "nvidia",
                        "moonshotai/kimi-k3",
                        "prompt-v15.4",
                        "Infrastructure",
                        "P1",
                        0.90,
                        true,
                        true,
                        true,
                        50,
                        10,
                        60,
                        100.0)
                });

        public Task<LlmEvaluationResponse> EvaluateAsync(
            LlmEvaluationRequest request,
            CancellationToken cancellationToken = default)
        {
            ReceivedRequest = request;

            return Task.FromResult(Response);
        }
    }

    private sealed class FakeEvaluationStore
        : IEvaluationStore
    {
        public ExperimentRunRecord? RecordedRun { get; private set; }

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken cancellationToken = default)
        {
            RecordedRun = run;

            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken cancellationToken = default)
        {
            IReadOnlyList<ExperimentRunRecord> result =
                RecordedRun is null
                    ? Array.Empty<ExperimentRunRecord>()
                    : new[] { RecordedRun };

            return Task.FromResult(result);
        }
    }
}