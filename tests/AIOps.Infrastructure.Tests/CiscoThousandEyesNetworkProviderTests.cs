using System.Net;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class CiscoThousandEyesNetworkProviderTests
{
    private const string ApiToken = "thousandeyes-test-token";
    private const string AgentId = "12345";
    private const string Target = "example.com";

    [Fact]
    public async Task MissingCredentialsReportNotConfiguredWithoutCallingCisco()
    {
        var handler = new RecordingHandler(
            (_, _) => throw new Xunit.Sdk.XunitException(
                "Unexpected HTTP call."));

        var provider = CreateProvider(
            handler,
            configured: false);

        var health =
            await provider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.NotConfigured,
            health.State);

        Assert.Equal(
            "Cisco ThousandEyes credentials and agent configuration are not available.",
            health.Reason);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task HealthCheckSendsBearerTokenAndRecognizesConfiguredAgent()
    {
        var handler = new RecordingHandler(
            (request, _) =>
            {
                Assert.Equal(
                    HttpMethod.Get,
                    request.Method);

                Assert.Equal(
                    "/v7/agents",
                    request.RequestUri!.AbsolutePath);

                Assert.Equal(
                    "Bearer",
                    request.Headers.Authorization?.Scheme);

                Assert.Equal(
                    ApiToken,
                    request.Headers.Authorization?.Parameter);

                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "agents": [
                            {
                              "agentId": 12345,
                              "agentName": "agent-1",
                              "location": "Pune"
                            }
                          ]
                        }
                        """));
            });

        var provider = CreateProvider(handler);

        var health =
            await provider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.Connected,
            health.State);

        Assert.Equal(
            "Cisco ThousandEyes API is reachable and the configured agent is available.",
            health.Reason);

        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task HealthCheckReportsAgentMissing()
    {
        var handler = new RecordingHandler(
            (_, _) =>
                Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "agents": [
                            {
                              "agentId": 99999,
                              "agentName": "other-agent"
                            }
                          ]
                        }
                        """)));

        var provider = CreateProvider(handler);

        var health =
            await provider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.ProviderError,
            health.State);

        Assert.Equal(
            "Cisco ThousandEyes configured agent was not found.",
            health.Reason);
    }

    [Theory]
    [InlineData(
        HttpStatusCode.Unauthorized,
        NetworkDiagnosticConnectionStates.AuthenticationFailed)]
    [InlineData(
        HttpStatusCode.Forbidden,
        NetworkDiagnosticConnectionStates.PermissionDenied)]
    [InlineData(
        HttpStatusCode.TooManyRequests,
        NetworkDiagnosticConnectionStates.ProviderError)]
    [InlineData(
        HttpStatusCode.ServiceUnavailable,
        NetworkDiagnosticConnectionStates.Unavailable)]
    public async Task HealthCheckMapsProviderFailures(
        HttpStatusCode status,
        string expectedState)
    {
        var handler = new RecordingHandler(
            (_, _) =>
                Task.FromResult(
                    JsonResponse(
                        status,
                        "provider error details must not escape")));

        var provider = CreateProvider(handler);

        var health =
            await provider.GetConnectionStatusAsync();

        Assert.Equal(
            expectedState,
            health.State);

        Assert.DoesNotContain(
            "provider error details",
            health.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RunDiagnosticsCreatesInstantTestAndMapsNetworkResult()
    {
        var observedAt =
            DateTimeOffset.Parse(
                "2026-10-02T12:30:00Z");

        var handler = new RecordingHandler(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    Assert.Equal(
                        "/v7/tests/agent-to-server/instant",
                        request.RequestUri!.AbsolutePath);

                    Assert.Equal(
                        "Bearer",
                        request.Headers.Authorization?.Scheme);

                    Assert.Equal(
                        ApiToken,
                        request.Headers.Authorization?.Parameter);

                    return Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.OK,
                            """
                            {
                              "testId": "98765",
                              "_links": {
                                "testResults": [
                                  {
                                    "rel": "network",
                                    "href": "https://api.thousandeyes.com/v7/test-results/98765/network"
                                  }
                                ]
                              }
                            }
                            """));
                }

                Assert.Equal(
                    HttpMethod.Get,
                    request.Method);

                Assert.Equal(
                    "/v7/test-results/98765/network",
                    request.RequestUri!.AbsolutePath);

                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        $$"""
                        {
                          "results": [
                            {
                              "date": "{{observedAt:O}}",
                              "avgLatency": 42.5,
                              "loss": 1.25,
                              "server": "example.com",
                              "serverIp": "203.0.113.10",
                              "roundId": "round-1",
                              "agent": {
                                "agentName": "agent-1",
                                "location": "Pune"
                              }
                            }
                          ]
                        }
                        """));
            });

        var provider = CreateProvider(handler);

        var result =
            await provider.RunDiagnosticsAsync(
                new GeneralNetworkDiagnosticRequest(
                    Target,
                    NetworkDiagnosticTargetType.Host));

        Assert.Equal(
            "Success",
            result.Status);

        Assert.Equal(
            "Cisco ThousandEyes",
            result.Provider);

        Assert.Equal(
            NetworkDiagnosticObservationType.ActiveTest,
            result.ObservationType);

        Assert.Equal(
            observedAt,
            result.ObservedAt);

        Assert.Equal(
            Target,
            result.Target);

        Assert.Equal(
            "Completed",
            result.State);

        Assert.Equal(
            42.5,
            result.Latency);

        Assert.Equal(
            1.25,
            result.PacketLoss);

        Assert.Equal(
            "98765",
            result.ExecutionId);

        Assert.Equal(
            "98765",
            result.ProviderDetails!.Fields["testId"]);

        Assert.Equal(
            "example.com",
            result.ProviderDetails.Fields["hostName"]);

        Assert.Equal(
            "203.0.113.10",
            result.ProviderDetails.Fields["publicIp"]);

        Assert.Equal(
            "agent-1",
            result.ProviderDetails.Fields["agentName"]);

        Assert.Equal(
            "Pune",
            result.ProviderDetails.Fields["location"]);

        Assert.Equal(
            "round-1",
            result.ProviderDetails.Fields["observationId"]);

        Assert.Equal(
            2,
            handler.Requests.Count);
    }

    [Fact]
    public async Task RunDiagnosticsSendsConfiguredAgentId()
    {
        var handler = new RecordingHandler(
            async (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    var body =
                        await request.Content!
                            .ReadAsStringAsync();

                    using var document =
                        JsonDocument.Parse(body);

                    var root =
                        document.RootElement;

                    Assert.Equal(
                        Target,
                        root.GetProperty("server").GetString());

                    Assert.True(
                        root.GetProperty("networkMeasurements").GetBoolean());

                    var agents =
                        root.GetProperty("agents");

                    Assert.Single(agents.EnumerateArray());

                    Assert.Equal(
                        AgentId,
                        agents[0]
                            .GetProperty("agentId")
                            .ToString());

                    return JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "testId": "123",
                          "_links": {
                            "testResults": [
                              {
                                "href": "/v7/test-results/123/network"
                              }
                            ]
                          }
                        }
                        """);
                }

                return JsonResponse(
                    HttpStatusCode.OK,
                    """
                    {
                      "results": [
                        {
                          "date": "2026-10-02T12:30:00Z",
                          "avgLatency": 10,
                          "loss": 0
                        }
                      ]
                    }
                    """);
            });

        var provider = CreateProvider(handler);

        await provider.RunDiagnosticsAsync(
            new GeneralNetworkDiagnosticRequest(
                Target,
                NetworkDiagnosticTargetType.Host));

        Assert.Equal(
            2,
            handler.Requests.Count);
    }

    [Fact]
    public async Task AccountGroupIdIsAddedToRequests()
    {
        var handler = new RecordingHandler(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    Assert.Equal(
                        "42",
                        request.RequestUri!
                            .ParseQueryStringValue("aid"));

                    return Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.OK,
                            """
                            {
                              "testId": "123",
                              "_links": {
                                "testResults": [
                                  {
                                    "href": "https://api.thousandeyes.com/v7/test-results/123/network"
                                  }
                                ]
                              }
                            }
                            """));
                }

                Assert.Equal(
                    "42",
                    request.RequestUri!
                        .ParseQueryStringValue("aid"));

                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "results": [
                            {
                              "date": "2026-10-02T12:30:00Z",
                              "avgLatency": 1,
                              "loss": 0
                            }
                          ]
                        }
                        """));
            });

        var provider = CreateProvider(
            handler,
            accountGroupId: "42");

        var result =
            await provider.RunDiagnosticsAsync(
                new GeneralNetworkDiagnosticRequest(
                    Target,
                    NetworkDiagnosticTargetType.Host));

        Assert.Equal(
            "123",
            result.ExecutionId);
    }

    [Theory]
    [InlineData(NetworkDiagnosticTargetType.Url)]
    [InlineData(NetworkDiagnosticTargetType.Service)]
    [InlineData(NetworkDiagnosticTargetType.Monitor)]
    public async Task UnsupportedTargetTypesAreRejected(
        NetworkDiagnosticTargetType targetType)
    {
        var handler = new RecordingHandler(
            (_, _) => throw new Xunit.Sdk.XunitException(
                "Unexpected HTTP call."));

        var provider = CreateProvider(handler);

        var exception =
            await Assert.ThrowsAsync<ThousandEyesProviderException>(
                () => provider.RunDiagnosticsAsync(
                    new GeneralNetworkDiagnosticRequest(
                        Target,
                        targetType)));

        Assert.Equal(
            ThousandEyesError.UnsupportedTarget,
            exception.Category);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task CreateRequestUnauthorizedMapsToAuthenticationFailure()
    {
        var handler = new RecordingHandler(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    return Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.Unauthorized,
                            "secret provider response"));
                }

                throw new Xunit.Sdk.XunitException(
                    "Unexpected HTTP call.");
            });

        var provider = CreateProvider(handler);

        var exception =
            await Assert.ThrowsAsync<ThousandEyesProviderException>(
                () => provider.RunDiagnosticsAsync(
                    new GeneralNetworkDiagnosticRequest(
                        Target,
                        NetworkDiagnosticTargetType.Host)));

        Assert.Equal(
            ThousandEyesError.AuthenticationFailed,
            exception.Category);

        Assert.DoesNotContain(
            "secret provider response",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ResultNotFoundEventuallyMapsToTargetNotFound()
    {
        var handler = new RecordingHandler(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    return Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.OK,
                            """
                            {
                              "testId": "123",
                              "_links": {
                                "testResults": [
                                  {
                                    "href": "/v7/test-results/123/network"
                                  }
                                ]
                              }
                            }
                            """));
                }

                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.NotFound,
                        "not found"));
            });

        var provider =
            CreateProvider(
                handler,
                pollAttempts: 1);

        var exception =
            await Assert.ThrowsAsync<ThousandEyesProviderException>(
                () => provider.RunDiagnosticsAsync(
                    new GeneralNetworkDiagnosticRequest(
                        Target,
                        NetworkDiagnosticTargetType.Host)));

        Assert.Equal(
            ThousandEyesError.TargetNotFound,
            exception.Category);
    }

    [Fact]
    public async Task ResultWithoutDataReturnsPendingAfterPollingLimit()
    {
        var handler = new RecordingHandler(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    return Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.OK,
                            """
                            {
                              "testId": "123",
                              "_links": {
                                "testResults": [
                                  {
                                    "href": "/v7/test-results/123/network"
                                  }
                                ]
                              }
                            }
                            """));
                }

                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "results": []
                        }
                        """));
            });

        var provider =
            CreateProvider(
                handler,
                pollAttempts: 2,
                pollIntervalMilliseconds: 1);

        var result =
            await provider.RunDiagnosticsAsync(
                new GeneralNetworkDiagnosticRequest(
                    Target,
                    NetworkDiagnosticTargetType.Host));

        Assert.Equal(
            "Pending",
            result.Status);

        Assert.Equal(
            "Pending",
            result.State);

        Assert.Equal(
            "123",
            result.ExecutionId);

        Assert.Null(result.Latency);
        Assert.Null(result.PacketLoss);

        Assert.Equal(
            3,
            handler.Requests.Count);
    }

    [Fact]
    public async Task MalformedCreateResponseIsSanitized()
    {
        var handler = new RecordingHandler(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    return Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.OK,
                            "not-json-secret"));
                }

                throw new Xunit.Sdk.XunitException(
                    "Unexpected HTTP call.");
            });

        var provider = CreateProvider(handler);

        var exception =
            await Assert.ThrowsAsync<ThousandEyesProviderException>(
                () => provider.RunDiagnosticsAsync(
                    new GeneralNetworkDiagnosticRequest(
                        Target,
                        NetworkDiagnosticTargetType.Host)));

        Assert.Equal(
            ThousandEyesError.ProviderFailure,
            exception.Category);

        Assert.Equal(
            "Cisco ThousandEyes provider response was malformed.",
            exception.Message);
    }

    [Fact]
    public async Task CallerCancellationIsPropagated()
    {
        using var cancellation =
            new CancellationTokenSource();

        var handler = new RecordingHandler(
            async (_, ct) =>
            {
                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    ct);

                return JsonResponse(
                    HttpStatusCode.OK,
                    "{}");
            });

        var provider = CreateProvider(handler);

        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.RunDiagnosticsAsync(
                new GeneralNetworkDiagnosticRequest(
                    Target,
                    NetworkDiagnosticTargetType.Host),
                cancellation.Token));
    }

    [Fact]
    public async Task TimeoutIsSanitized()
    {
        var handler = new RecordingHandler(
            (_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new TaskCanceledException(
                        "raw timeout secret")));

        var provider = CreateProvider(handler);

        var exception =
            await Assert.ThrowsAsync<ThousandEyesProviderException>(
                () => provider.RunDiagnosticsAsync(
                    new GeneralNetworkDiagnosticRequest(
                        Target,
                        NetworkDiagnosticTargetType.Host)));

        Assert.Equal(
            ThousandEyesError.Timeout,
            exception.Category);

        Assert.Equal(
            "Cisco ThousandEyes provider request timed out.",
            exception.Message);

        Assert.DoesNotContain(
            "raw timeout secret",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SerializedResultDoesNotContainApiToken()
    {
        var handler = new RecordingHandler(
            (request, _) =>
            {
                if (request.Method == HttpMethod.Post)
                {
                    return Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.OK,
                            """
                            {
                              "testId": "123",
                              "_links": {
                                "testResults": [
                                  {
                                    "href": "/v7/test-results/123/network"
                                  }
                                ]
                              }
                            }
                            """));
                }

                return Task.FromResult(
                    JsonResponse(
                        HttpStatusCode.OK,
                        """
                        {
                          "results": [
                            {
                              "date": "2026-10-02T12:30:00Z",
                              "avgLatency": 10,
                              "loss": 0
                            }
                          ]
                        }
                        """));
            });

        var provider = CreateProvider(handler);

        var result =
            await provider.RunDiagnosticsAsync(
                new GeneralNetworkDiagnosticRequest(
                    Target,
                    NetworkDiagnosticTargetType.Host));

        var serialized =
            JsonSerializer.Serialize(result);

        Assert.DoesNotContain(
            ApiToken,
            serialized,
            StringComparison.Ordinal);
    }

    private static CiscoThousandEyesNetworkProvider CreateProvider(
        RecordingHandler handler,
        bool configured = true,
        string? accountGroupId = null,
        int pollAttempts = 2,
        int pollIntervalMilliseconds = 1)
    {
        var options = new CiscoThousandEyesOptions
        {
            BaseUrl = "https://api.thousandeyes.com/v7/",
            ApiToken = configured ? ApiToken : null,
            AgentId = configured ? AgentId : null,
            AccountGroupId = accountGroupId,
            TimeoutSeconds = 30,
            ResultPollAttempts = pollAttempts,
            ResultPollIntervalMilliseconds =
                pollIntervalMilliseconds
        };

        var httpClient = new HttpClient(handler)
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        return new CiscoThousandEyesNetworkProvider(
            httpClient,
            Options.Create(options),
            TimeProvider.System);
    }

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode statusCode,
        string body)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(
                body,
                Encoding.UTF8,
                "application/json")
        };
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handler)
        : HttpMessageHandler
    {
        public List<HttpRequestMessage> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(request);

            return await handler(
                request,
                cancellationToken);
        }
    }
}

internal static class UriTestExtensions
{
    public static string? ParseQueryStringValue(
        this Uri uri,
        string key)
    {
        var query = uri.Query.TrimStart('?');

        foreach (var pair in query.Split(
                     '&',
                     StringSplitOptions.RemoveEmptyEntries))
        {
            var parts = pair.Split(
                '=',
                2,
                StringSplitOptions.None);

            if (parts.Length != 2)
            {
                continue;
            }

            var name =
                Uri.UnescapeDataString(parts[0]);

            if (!string.Equals(
                    name,
                    key,
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return Uri.UnescapeDataString(parts[1]);
        }

        return null;
    }
}