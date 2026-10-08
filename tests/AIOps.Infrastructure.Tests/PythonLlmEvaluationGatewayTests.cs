using System.Net.Http.Json;
using System.Net;
using System.Text;
using AIOps.Contracts.Evaluation;
using AIOps.Infrastructure.Evaluation;
using Microsoft.Extensions.Logging.Abstractions;

namespace AIOps.Infrastructure.Tests;

public sealed class PythonLlmEvaluationGatewayTests
{
    [Fact]
    public async Task EvaluateAsync_Posts_request_and_reads_response()
    {
        var responseJson =
            """
            {
              "modelType": "llm",
              "dataset": {
                "id": "dataset-1",
                "name": "golden",
                "version": "1.0"
              },
              "model": {
                "provider": "nvidia",
                "name": "test-model",
                "promptVersion": "v1-nvidia-triage"
              },
              "sampleCount": 1,
              "metrics": {
                "domainAccuracy": 1.0,
                "severityAccuracy": 1.0,
                "triageAccuracy": 1.0,
                "averageConfidence": 0.95,
                "totalLatencyMs": 25.0,
                "averageLatencyMs": 25.0,
                "promptTokens": 40,
                "completionTokens": 15,
                "totalTokens": 55
              },
              "predictions": [
                {
                  "caseId": "case-1",
                  "provider": "nvidia",
                  "model": "test-model",
                  "promptVersion": "v1-nvidia-triage",
                  "domain": "Network",
                  "severity": "P1",
                  "confidence": 0.95,
                  "domainCorrect": true,
                  "severityCorrect": true,
                  "triageCorrect": true,
                  "promptTokens": 40,
                  "completionTokens": 15,
                  "totalTokens": 55,
                  "latencyMs": 25.0
                }
              ]
            }
            """;

        LlmEvaluationRequest? capturedRequest = null;

        var handler = new StubHttpMessageHandler(
            request =>
            {
                capturedRequest =
                    request.Content is null
                        ? null
                        : request.Content
                            .ReadFromJsonAsync<LlmEvaluationRequest>()
                            .GetAwaiter()
                            .GetResult();

                return new HttpResponseMessage(
                    HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        responseJson,
                        Encoding.UTF8,
                        "application/json")
                };
            });

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress =
                new Uri("http://127.0.0.1:8000/")
        };

        var gateway =
            new PythonLlmEvaluationGateway(
                httpClient,
                NullLogger<PythonLlmEvaluationGateway>.Instance);

        var request = new LlmEvaluationRequest(
            "dataset-1",
            "golden",
            "1.0",
            "nvidia",
            [
                new LlmEvaluationCaseRequest(
                    "case-1",
                    "VPN outage",
                    "Corporate VPN is down.",
                    "Network",
                    "P1")
            ]);

        var result =
            await gateway.EvaluateAsync(request);

        Assert.NotNull(capturedRequest);
        Assert.Equal(
            "dataset-1",
            capturedRequest!.DatasetId);
        Assert.Equal(
            "nvidia",
            capturedRequest.Provider);
        Assert.Single(capturedRequest.Cases);

        Assert.Equal("llm", result.ModelType);
        Assert.Equal(
            "dataset-1",
            result.Dataset.Id);
        Assert.Equal(
            "nvidia",
            result.Model.Provider);
        Assert.Equal(
            "test-model",
            result.Model.Name);
        Assert.Equal(1, result.SampleCount);
        Assert.Equal(
            1.0,
            result.Metrics.TriageAccuracy);
        Assert.Single(result.Predictions);
    }

    [Fact]
    public async Task EvaluateAsync_throws_on_http_failure()
    {
        var handler = new StubHttpMessageHandler(
            _ =>
                new HttpResponseMessage(
                    HttpStatusCode.BadGateway));

        using var httpClient = new HttpClient(handler)
        {
            BaseAddress =
                new Uri("http://127.0.0.1:8000/")
        };

        var gateway =
            new PythonLlmEvaluationGateway(
                httpClient,
                NullLogger<PythonLlmEvaluationGateway>.Instance);

        var request = new LlmEvaluationRequest(
            "dataset-1",
            "golden",
            "1.0",
            "nvidia",
            [
                new LlmEvaluationCaseRequest(
                    "case-1",
                    "VPN outage",
                    "Corporate VPN is down.",
                    "Network",
                    "P1")
            ]);

        await Assert.ThrowsAsync<HttpRequestException>(
            () => gateway.EvaluateAsync(request));
    }

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(handler(request));
        }
    }
}