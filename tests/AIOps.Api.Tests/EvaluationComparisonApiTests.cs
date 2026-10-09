using System.Net;
using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Orchestration.Evaluation;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIOps.Api.Tests;

public sealed class EvaluationComparisonApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EvaluationComparisonApiTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ComparisonEndpoint_ReturnsPersistedEvaluationReport()
    {
        var store = new FakeEvaluationStore();

        using var client = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");

            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEvaluationStore>();
                services.AddSingleton<IEvaluationStore>(store);

                services.RemoveAll<EvaluationComparisonService>();
                services.AddScoped<EvaluationComparisonService>();
            });
        }).CreateClient();

        using var response = await client.GetAsync(
            "/api/v1/evaluations/comparison?experimentId=experiment-1&datasetName=golden");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        var body = document.RootElement;

        Assert.Equal(1, body.GetProperty("runCount").GetInt32());

        var dataset = Assert.Single(
            body.GetProperty("datasets").EnumerateArray().ToArray());

        Assert.Equal("golden", dataset.GetProperty("datasetName").GetString());
        Assert.Equal("v1", dataset.GetProperty("datasetVersion").GetString());

        var run = Assert.Single(
            dataset.GetProperty("runs").EnumerateArray().ToArray());

        Assert.Equal("model-a", run.GetProperty("modelName").GetString());
        Assert.Equal(
            0.9,
            run.GetProperty("metrics")
                .GetProperty("TriageAccuracy")
                .GetDouble());

        Assert.Equal("experiment-1", store.LastExperimentId);
        Assert.Equal("golden", store.LastDatasetName);
    }

    private sealed class FakeEvaluationStore : IEvaluationStore
    {
        public string? LastExperimentId { get; private set; }

        public string? LastDatasetName { get; private set; }

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken cancellationToken = default)
        {
            LastExperimentId = experimentId;
            LastDatasetName = datasetName;

            IReadOnlyList<ExperimentRunRecord> runs =
                new List<ExperimentRunRecord>
                {
                    new(
                        Guid.NewGuid(),
                        "experiment-1",
                        "llm",
                        "model-a",
                        "prompt-v1",
                        "golden",
                        "v1",
                        """{"Model":{"Provider":"test-provider"}}""",
                        10,
                        DateTimeOffset.UtcNow,
                        DateTimeOffset.UtcNow,
                        """{"TriageAccuracy":0.9}""",
                        null)
                };

            IEnumerable<ExperimentRunRecord> filteredRuns = runs;

            if (experimentId is not null)
            {
                filteredRuns = filteredRuns.Where(
                    run => run.ExperimentId == experimentId);
            }

            if (datasetName is not null)
            {
                filteredRuns = filteredRuns.Where(
                    run => run.DatasetName == datasetName);
            }

            return Task.FromResult<IReadOnlyList<ExperimentRunRecord>>(
                filteredRuns.ToArray());
        }

        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException(
                "This API test only reads evaluation runs.");
        }
    }
}