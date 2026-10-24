using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.Evaluation;
using AIOps.Host.Security;
using AIOps.Orchestration.Evaluation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace AIOps.Api.Tests;

public sealed class EvaluationAcceptanceApiTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public EvaluationAcceptanceApiTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task AcceptanceEndpoint_returns_persisted_result_id_and_outcome()
    {
        var acceptanceStore = new RecordingAcceptanceStore();
        using var client = CreateConfiguredClient(
            acceptanceStore,
            [
                CreateRun("baseline", """{"Accuracy":0.9}"""),
                CreateRun("candidate", """{"Accuracy":0.9}""")
            ]);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest("baseline", "candidate"));

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var record = Assert.Single(acceptanceStore.Records);
        Assert.Equal(
            record.Id,
            body.RootElement.GetProperty("resultId").GetGuid());
        Assert.Equal(
            "Passed",
            body.RootElement.GetProperty("outcome").GetString());
        Assert.Equal(
            "server-acceptance",
            body.RootElement.GetProperty("policyName").GetString());
    }

    [Fact]
    public async Task AcceptanceEndpoint_ignores_caller_supplied_policy_values()
    {
        using var client = CreateConfiguredClient(
            new RecordingAcceptanceStore(),
            [
                CreateRun("baseline", """{"Accuracy":0.9}"""),
                CreateRun("candidate", """{"Accuracy":0.8}""")
            ]);
        using var request = new StringContent(
            """
            {
              "baselineExperimentId": "baseline",
              "candidateExperimentId": "candidate",
              "policy": {
                "name": "caller-policy",
                "requiredMetrics": [
                  {
                    "metricName": "Accuracy",
                    "direction": "HigherIsBetter",
                    "allowedRegression": 1
                  }
                ]
              }
            }
            """,
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            "/api/v1/evaluations/acceptance",
            request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        Assert.Equal(
            "server-acceptance",
            body.RootElement.GetProperty("policyName").GetString());
        Assert.Equal(
            "Failed",
            body.RootElement.GetProperty("outcome").GetString());
    }

    [Fact]
    public async Task AcceptanceEndpoint_validates_required_experiment_ids()
    {
        using var client = CreateConfiguredClient(
            new RecordingAcceptanceStore(),
            []);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest(" ", null));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        var errors = body.RootElement.GetProperty("errors");
        Assert.True(errors.TryGetProperty("baselineExperimentId", out _));
        Assert.True(errors.TryGetProperty("candidateExperimentId", out _));
    }

    [Fact]
    public async Task AcceptanceEndpoint_returns_service_unavailable_when_policy_is_not_configured()
    {
        using var client = CreateConfiguredClient(
            new RecordingAcceptanceStore(),
            [],
            configurePolicy: false);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest("baseline", "candidate"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("RequiredMetrics", responseBody);
        Assert.DoesNotContain("Exception", responseBody);
    }

    [Theory]
    [InlineData("unsupported-direction")]
    [InlineData("negative-allowance")]
    [InlineData("missing-rules")]
    [InlineData("empty-rules")]
    public async Task AcceptanceEndpoint_returns_service_unavailable_for_invalid_server_policy(
        string invalidPolicy)
    {
        var acceptanceStore = new RecordingAcceptanceStore();
        using var client = CreateConfiguredClient(
            acceptanceStore,
            [
                CreateRun("baseline", """{"Accuracy":0.9}"""),
                CreateRun("candidate", """{"Accuracy":0.9}""")
            ],
            policyOptions: CreateInvalidPolicyOptions(invalidPolicy));

        using var response = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest("baseline", "candidate"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.Contains(
            "A valid server-side acceptance policy is not configured.",
            responseBody);
        Assert.DoesNotContain("Sideways", responseBody);
        Assert.DoesNotContain("-0.01", responseBody);
        Assert.Empty(acceptanceStore.Records);
    }

    [Fact]
    public async Task AcceptanceEndpoint_does_not_report_success_when_persistence_fails()
    {
        var acceptanceStore = new RecordingAcceptanceStore
        {
            Failure = new EvaluationAcceptancePersistenceException(
                "private database detail")
        };
        using var client = CreateConfiguredClient(
            acceptanceStore,
            [
                CreateRun("baseline", """{"Accuracy":0.9}"""),
                CreateRun("candidate", """{"Accuracy":0.9}""")
            ]);

        using var response = await client.PostAsJsonAsync(
            "/api/v1/evaluations/acceptance",
            new EvaluationAcceptanceRequest("baseline", "candidate"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        var responseBody = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("private database detail", responseBody);
        Assert.Contains("was not saved", responseBody);
    }

    [Fact]
    public void AcceptanceEndpoint_uses_existing_evaluation_authorization_policy()
    {
        using var client = _factory.CreateClient();
        using var scope = _factory.Services.CreateScope();
        var endpoints = scope.ServiceProvider
            .GetRequiredService<EndpointDataSource>()
            .Endpoints
            .OfType<RouteEndpoint>();
        var endpoint = Assert.Single(
            endpoints,
            item => item.RoutePattern.RawText ==
                "/api/v1/evaluations/acceptance");
        var authorization = endpoint.Metadata
            .GetOrderedMetadata<IAuthorizeData>();

        Assert.Contains(
            authorization,
            metadata => metadata.Policy == AuthPolicies.CanViewQueue);
    }

    private HttpClient CreateConfiguredClient(
        RecordingAcceptanceStore acceptanceStore,
        IReadOnlyList<ExperimentRunRecord> runs,
        bool configurePolicy = true,
        EvaluationAcceptancePolicyOptions? policyOptions = null)
    {
        return _factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Test");
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IEvaluationStore>();
                services.AddSingleton<IEvaluationStore>(
                    new FakeEvaluationStore(runs));
                services.RemoveAll<IEvaluationAcceptanceStore>();
                services.AddSingleton<IEvaluationAcceptanceStore>(
                    acceptanceStore);

                if (configurePolicy)
                {
                    services.Configure<EvaluationAcceptancePolicyOptions>(
                        options =>
                        {
                            options.Name = "server-acceptance";
                            var configuredPolicy = policyOptions ??
                                CreatePolicyOptions();
                            options.Name = configuredPolicy.Name;
                            options.RequiredMetrics =
                                configuredPolicy.RequiredMetrics;
                        });
                }
            });
        }).CreateClient();
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
            DateTimeOffset.UtcNow.AddMinutes(-1),
            DateTimeOffset.UtcNow,
            metricsJson,
            null);

    private sealed class FakeEvaluationStore(
        IReadOnlyList<ExperimentRunRecord> runs) : IEvaluationStore
    {
        public Task RecordRunAsync(
            ExperimentRunRecord run,
            CancellationToken ct = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<ExperimentRunRecord>> ListRunsAsync(
            string? experimentId = null,
            string? datasetName = null,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ExperimentRunRecord>>(
                runs.Where(run =>
                        (experimentId is null || run.ExperimentId == experimentId) &&
                        (datasetName is null || run.DatasetName == datasetName))
                    .ToArray());
    }

    private sealed class RecordingAcceptanceStore
        : IEvaluationAcceptanceStore
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
