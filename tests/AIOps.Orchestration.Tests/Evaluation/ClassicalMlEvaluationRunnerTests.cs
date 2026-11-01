using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;
using AIOps.Infrastructure.Evaluation;
using AIOps.Orchestration.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class ClassicalMlEvaluationRunnerTests
{
    [Fact]
    public async Task RunAsync_persists_metadata_metrics_and_comparable_baseline_run()
    {
        var store = new RecordingEvaluationStore();
        var responseJson = JsonSerializer.Serialize(
            FakeClassicalMlEvaluationGateway.CreateResponse(),
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var responseHandler = new FixedResponseHandler(responseJson);
        using var httpClient = new HttpClient(responseHandler)
        {
            BaseAddress = new Uri("http://python-agent.test/")
        };
        var runner = new ClassicalMlEvaluationRunner(
            new PythonClassicalMlEvaluationGateway(
                httpClient,
                NullLogger<PythonClassicalMlEvaluationGateway>.Instance),
            store);

        var run = await runner.RunAsync("phase15-b6-baseline");

        Assert.Equal(
            "/api/v1/evaluation/classical-ml",
            responseHandler.RequestUri?.AbsolutePath);
        Assert.Equal("phase15-b6-baseline", run.ExperimentId);
        Assert.Equal("classical_ml", run.ModelType);
        Assert.Equal("tfidf-logistic-regression", run.ModelName);
        Assert.Equal("Phase 15 Classical ML Baseline", run.DatasetName);
        Assert.Equal("1.0.0", run.DatasetVersion);
        Assert.Equal(8, run.SampleCount);
        Assert.NotEqual(default, run.StartedAt);
        Assert.NotNull(run.FinishedAt);
        Assert.True(run.FinishedAt >= run.StartedAt);
        Assert.Single(store.Runs);

        using var configurationJson = JsonDocument.Parse(run.ConfigJson);
        var configuration = configurationJson.RootElement;
        Assert.Equal(
            "phase15-classical-ml-training",
            configuration.GetProperty("TrainingCorpus").GetProperty("Id").GetString());
        Assert.Equal(
            "1.0.0",
            configuration.GetProperty("TrainingCorpus").GetProperty("Version").GetString());
        Assert.Equal(
            "phase15-classical-ml-baseline",
            configuration.GetProperty("DatasetId").GetString());
        Assert.Equal(
            "python-classical-ml-baseline",
            configuration.GetProperty("ExecutionMode").GetString());
        Assert.True(configuration.GetProperty("Deterministic").GetBoolean());
        Assert.False(configuration.GetProperty("ExternalToolsExecuted").GetBoolean());

        using var metricsJson = JsonDocument.Parse(run.MetricsJson);
        var metrics = metricsJson.RootElement;
        Assert.Equal(8, metrics.GetProperty("SampleCount").GetInt32());
        Assert.Equal(8, metrics.GetProperty("CorrectCases").GetInt32());
        Assert.Equal(
            1.0,
            metrics.GetProperty("DomainClassificationAccuracy").GetDouble());
        var predictions = metrics.GetProperty("Predictions").EnumerateArray().ToArray();
        Assert.Equal(8, predictions.Length);
        Assert.Equal(
            "network-001",
            predictions[0].GetProperty("CaseId").GetString());
        Assert.Equal(
            "Network",
            predictions[0].GetProperty("PredictedDomain").GetString());
        Assert.Equal(
            "infrastructure-002",
            predictions[^1].GetProperty("CaseId").GetString());

        await store.RecordRunAsync(new ExperimentRunRecord(
            Guid.NewGuid(),
            "phase15-b6-compatible-candidate",
            "llm",
            "candidate-model",
            null,
            run.DatasetName,
            run.DatasetVersion,
            """{"Model":{"Provider":"test-provider"}}""",
            4,
            run.StartedAt.AddMinutes(1),
            run.FinishedAt!.Value.AddMinutes(1),
            """{"DomainClassificationAccuracy":0.8}""",
            "Compatible comparison fixture."));

        var report = await new EvaluationComparisonService(store)
            .CompareAsync(datasetName: run.DatasetName);
        var datasetReport = Assert.Single(report.Datasets);
        Assert.Equal(2, datasetReport.Runs.Count);
        var comparisonRun = Assert.Single(
            datasetReport.Runs,
            reportedRun => reportedRun.RunId == run.Id);
        Assert.Equal(run.Id, comparisonRun.RunId);
        Assert.Equal("Phase 15 Classical ML Baseline", comparisonRun.DatasetName);
        Assert.Equal(1.0, comparisonRun.Metrics["DomainClassificationAccuracy"]);
        var comparison = Assert.Single(datasetReport.Comparisons);
        var accuracyDelta = Assert.Single(comparison.Metrics);
        Assert.Equal("DomainClassificationAccuracy", accuracyDelta.MetricName);
        Assert.Equal(-0.2, accuracyDelta.Delta, precision: 10);
        Assert.Equal(EvaluationMetricTrend.Regressed, accuracyDelta.Trend);
    }

    [Fact]
    public async Task RunAsync_rejects_prediction_case_mismatch_before_persisting()
    {
        var store = new RecordingEvaluationStore();
        var response = FakeClassicalMlEvaluationGateway.CreateResponse() with
        {
            CaseResults =
            [
                new("network-001", "Network", "Network", 0.9),
                new("network-002", "Network", "Network", 0.8),
                new("identity-001", "Identity", "Identity", 0.7),
                new("identity-002", "Identity", "Identity", 0.6),
                new("database-001", "Database", "Database", 0.9),
                new("database-002", "Database", "Database", 0.8),
                new("infrastructure-001", "Infrastructure", "Infrastructure", 0.7),
                new("substituted-case", "Infrastructure", "Infrastructure", 0.6)
            ]
        };
        var runner = new ClassicalMlEvaluationRunner(
            new FakeClassicalMlEvaluationGateway(response),
            store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("phase15-b6-invalid"));
        Assert.Empty(store.Runs);
    }

    [Fact]
    public async Task RunAsync_rejects_unknown_expected_domain_before_persisting()
    {
        var store = new RecordingEvaluationStore();
        var response = FakeClassicalMlEvaluationGateway.CreateResponse() with
        {
            CaseResults =
            [
                new("network-001", "Unknown", "Unknown", 0.9),
                .. FakeClassicalMlEvaluationGateway.CreateResponse()
                    .CaseResults.Skip(1)
            ]
        };
        var runner = new ClassicalMlEvaluationRunner(
            new FakeClassicalMlEvaluationGateway(response),
            store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("phase15-b6-unknown-expected"));
        Assert.Empty(store.Runs);
    }

    [Fact]
    public async Task RunAsync_rejects_unknown_predicted_domain_before_persisting()
    {
        var store = new RecordingEvaluationStore();
        var response = FakeClassicalMlEvaluationGateway.CreateResponse() with
        {
            CorrectCases = 7,
            Accuracy = 0.875,
            CaseResults =
            [
                new("network-001", "Unknown", "Network", 0.9),
                .. FakeClassicalMlEvaluationGateway.CreateResponse()
                    .CaseResults.Skip(1)
            ]
        };
        var runner = new ClassicalMlEvaluationRunner(
            new FakeClassicalMlEvaluationGateway(response),
            store);

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => runner.RunAsync("phase15-b6-unknown-predicted"));
        Assert.Empty(store.Runs);
    }

    private sealed class RecordingEvaluationStore : IEvaluationStore
    {
        public List<ExperimentRunRecord> Runs { get; } = [];

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            Runs.Add(run);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();
            IReadOnlyList<ExperimentRunRecord> matches = Runs
                .Where(run =>
                    experimentId is null || run.ExperimentId == experimentId)
                .Where(run =>
                    datasetName is null || run.DatasetName == datasetName)
                .ToArray();
            return Task.FromResult(matches);
        }
    }

    private sealed class FixedResponseHandler(string responseJson)
        : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RequestUri = request.RequestUri;
            return Task.FromResult(new HttpResponseMessage(
                System.Net.HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson)
            });
        }
    }

    private sealed class FakeClassicalMlEvaluationGateway(
        ClassicalMlEvaluationResponse? response = null)
        : IClassicalMlEvaluationGateway
    {
        public Task<ClassicalMlEvaluationResponse> EvaluateAsync(
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                response ?? CreateResponse());
        }

        public static ClassicalMlEvaluationResponse CreateResponse() =>
            new(
                "phase15-classical-ml-training",
                "Phase 15 Classical ML Training Corpus",
                "1.0.0",
                "phase15-classical-ml-baseline",
                "Phase 15 Classical ML Baseline",
                "1.0.0",
                "classical_ml",
                "tfidf-logistic-regression",
                8,
                8,
                1.0,
                [
                    new("network-001", "Network", "Network", 0.9),
                    new("network-002", "Network", "Network", 0.8),
                    new("identity-001", "Identity", "Identity", 0.7),
                    new("identity-002", "Identity", "Identity", 0.6),
                    new("database-001", "Database", "Database", 0.9),
                    new("database-002", "Database", "Database", 0.8),
                    new("infrastructure-001", "Infrastructure", "Infrastructure", 0.7),
                    new("infrastructure-002", "Infrastructure", "Infrastructure", 0.6)
                ]);
    }
}
