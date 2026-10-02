using System.Net;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using AIOps.Infrastructure;
using AIOps.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class FortinetVpnProviderTests
{
    private sealed class MemorySelectionStore
        : INetworkDiagnosticProviderSelectionStore
    {
        private NetworkDiagnosticProviderSelection? _selection;

        public Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(
            CancellationToken ct = default) =>
            Task.FromResult(_selection);

        public Task SetSelectionAsync(
            NetworkDiagnosticProviderSelection selection,
            string changedBy,
            CancellationToken ct = default)
        {
            _selection = selection;
            return Task.CompletedTask;
        }
    }

    private const string Secret =
        "fortinet-secret-must-not-leak";

    private const string Token =
        "fortinet-api-token-must-not-leak";

    private const string User =
        "vpn.user@example.test";

    private const string BaseUrl =
        "https://fortigate.example.test/";

    [Fact]
    public async Task MissingCredentialsReportNotConfiguredWithoutCallingFortinet()
    {
        var handler = new RecordingHandler(
            (_, _) =>
                throw new Xunit.Sdk.XunitException(
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
            "Fortinet FortiGate credentials are not configured.",
            health.Reason);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UserLookupMapsSessionFieldsWithoutFabricatingMeasurements()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                SessionResponse(
                    User,
                    "session-123",
                    "198.51.100.25",
                    "10.10.20.30",
                    "web",
                    "ssl",
                    "employees")));

        var provider = CreateProvider(handler);

        var result =
            await provider.RunVpnDiagnosticsAsync(
                new VpnDiagnosticRequest(
                    User,
                    NetworkDiagnosticTargetType.User));

        Assert.Equal(
            "Success",
            result.Status);

        Assert.Equal(
            "Fortinet FortiGate",
            result.Provider);

        Assert.Equal(
            NetworkDiagnosticObservationType.Session,
            result.ObservationType);

        Assert.Equal(
            User,
            result.User);

        Assert.Null(result.Device);

        Assert.Equal(
            "10.10.20.30",
            result.Ip);

        Assert.Equal(
            NetworkDiagnosticConnectionStates.Connected,
            result.ConnectionState);

        Assert.Null(result.Latency);

        Assert.Null(result.PacketLoss);

        Assert.NotNull(result.ProviderDetails);

        Assert.Equal(
            "session-123",
            result.ProviderDetails!.Fields["sessionId"]);

        Assert.Equal(
            "SSL-VPN",
            result.ProviderDetails.Fields["vpnType"]);

        Assert.Equal(
            "web",
            result.ProviderDetails.Fields["connectionType"]);

        Assert.Equal(
            "198.51.100.25",
            result.ProviderDetails.Fields["source"]);

        Assert.Equal(
            "ssl",
            result.ProviderDetails.Fields["tunnelProtocol"]);

        Assert.Equal(
            "employees",
            result.ProviderDetails.Fields["networkName"]);

        Assert.Equal(
            "Connected",
            result.ProviderDetails.Fields["state"]);

        Assert.Equal(
            "FortiGate SSL-VPN",
            result.ProviderDetails.Fields["vpn"]);
    }

    [Fact]
    public async Task UsesSourceIpWhenTunnelIpIsUnavailable()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                SessionResponse(
                    User,
                    "session-source-only",
                    "198.51.100.50",
                    null,
                    "web",
                    "ssl",
                    null)));

        var provider = CreateProvider(handler);

        var result =
            await provider.RunVpnDiagnosticsAsync(
                new VpnDiagnosticRequest(
                    User,
                    NetworkDiagnosticTargetType.User));

        Assert.Equal(
            "198.51.100.50",
            result.Ip);

        Assert.Equal(
            "198.51.100.50",
            result.ProviderDetails!.Fields["source"]);
    }

    [Fact]
    public async Task RequestUsesSslVpnMonitorEndpoint()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                SessionResponse(
                    User,
                    "session-1",
                    "198.51.100.10",
                    "10.0.0.10",
                    "web",
                    "ssl",
                    "employees")));

        var provider = CreateProvider(handler);

        await provider.RunVpnDiagnosticsAsync(
            new VpnDiagnosticRequest(
                User,
                NetworkDiagnosticTargetType.User));

        var request = Assert.Single(handler.Requests);

        Assert.Equal(
            HttpMethod.Get,
            request.Method);

        Assert.Equal(
            "/api/v2/monitor/vpn/ssl",
            request.RequestUri.AbsolutePath);

        Assert.Contains(
            "vdom=root",
            request.RequestUri.Query,
            StringComparison.OrdinalIgnoreCase);

        Assert.Equal(
            "Bearer",
            request.AuthorizationScheme);

        Assert.Equal(
            Token,
            request.AuthorizationParameter);
    }

    [Fact]
    public async Task UsesConfiguredVdom()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                SessionResponse(
                    User,
                    "session-vdom",
                    "198.51.100.11",
                    "10.0.0.11",
                    "web",
                    "ssl",
                    "employees")));

        var provider =
            CreateProvider(
                handler,
                vdom: "production");

        await provider.RunVpnDiagnosticsAsync(
            new VpnDiagnosticRequest(
                User,
                NetworkDiagnosticTargetType.User));

        var request = Assert.Single(handler.Requests);

        Assert.Contains(
            "vdom=production",
            request.RequestUri.Query,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task NoMatchingSessionReturnsStableTargetNotFound()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                SessionResponse(
                    "different.user@example.test",
                    "session-other",
                    "198.51.100.20",
                    "10.0.0.20",
                    "web",
                    "ssl",
                    "employees")));

        var provider = CreateProvider(handler);

        var error =
            await Assert.ThrowsAsync<FortinetVpnException>(
                () => provider.RunVpnDiagnosticsAsync(
                    new VpnDiagnosticRequest(
                        User,
                        NetworkDiagnosticTargetType.User)));

        Assert.Equal(
            FortinetVpnError.TargetNotFound,
            error.Category);

        Assert.DoesNotContain(
            Secret,
            error.Message,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NetworkDiagnosticTargetType.Device)]
    [InlineData(NetworkDiagnosticTargetType.Ip)]
    public async Task UnsupportedTargetsAreRejectedWithoutCallingFortinet(
        NetworkDiagnosticTargetType targetType)
    {
        var handler = new RecordingHandler(
            (_, _) =>
                throw new Xunit.Sdk.XunitException(
                    "Unexpected HTTP call."));

        var provider = CreateProvider(handler);

        var error =
            await Assert.ThrowsAsync<FortinetVpnException>(
                () => provider.RunVpnDiagnosticsAsync(
                    new VpnDiagnosticRequest(
                        "unsupported-target",
                        targetType)));

        Assert.Equal(
            FortinetVpnError.UnsupportedTarget,
            error.Category);

        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MultipleMatchingSessionsFailClosed()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                MultipleSessionResponse(
                    User)));

        var provider = CreateProvider(handler);

        var error =
            await Assert.ThrowsAsync<FortinetVpnException>(
                () => provider.RunVpnDiagnosticsAsync(
                    new VpnDiagnosticRequest(
                        User,
                        NetworkDiagnosticTargetType.User)));

        Assert.Equal(
            FortinetVpnError.ProviderFailure,
            error.Category);

        Assert.DoesNotContain(
            Secret,
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task MalformedResponseReturnsProviderFailureWithoutRawBody()
    {
        const string rawResponse =
            "sensitive fortinet raw response";

        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                rawResponse));

        var provider = CreateProvider(handler);

        var error =
            await Assert.ThrowsAsync<FortinetVpnException>(
                () => provider.RunVpnDiagnosticsAsync(
                    new VpnDiagnosticRequest(
                        User,
                        NetworkDiagnosticTargetType.User)));

        Assert.Equal(
            FortinetVpnError.ProviderFailure,
            error.Category);

        Assert.DoesNotContain(
            rawResponse,
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task EmptySessionResponseReturnsTargetNotFound()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                EmptySessionResponse()));

        var provider = CreateProvider(handler);

        var error =
            await Assert.ThrowsAsync<FortinetVpnException>(
                () => provider.RunVpnDiagnosticsAsync(
                    new VpnDiagnosticRequest(
                        User,
                        NetworkDiagnosticTargetType.User)));

        Assert.Equal(
            FortinetVpnError.TargetNotFound,
            error.Category);
    }

    [Theory]
    [InlineData(
        HttpStatusCode.Unauthorized,
        FortinetVpnError.AuthenticationFailed)]
    [InlineData(
        HttpStatusCode.Forbidden,
        FortinetVpnError.PermissionDenied)]
    [InlineData(
        HttpStatusCode.NotFound,
        FortinetVpnError.TargetNotFound)]
    [InlineData(
        HttpStatusCode.TooManyRequests,
        FortinetVpnError.Throttled)]
    [InlineData(
        HttpStatusCode.InternalServerError,
        FortinetVpnError.Unavailable)]
    [InlineData(
        HttpStatusCode.BadRequest,
        FortinetVpnError.ProviderFailure)]
    public async Task HttpFailuresMapToStableSanitizedCategories(
        HttpStatusCode status,
        FortinetVpnError expectedCategory)
    {
        var handler = new RecordingHandler(
            (_, _) =>
                Task.FromResult(
                    JsonResponse(
                        status,
                        $"{Secret} {Token} raw-provider-error")));

        var provider = CreateProvider(handler);

        var error =
            await Assert.ThrowsAsync<FortinetVpnException>(
                () => provider.RunVpnDiagnosticsAsync(
                    new VpnDiagnosticRequest(
                        User,
                        NetworkDiagnosticTargetType.User)));

        Assert.Equal(
            expectedCategory,
            error.Category);

        Assert.DoesNotContain(
            Secret,
            error.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            Token,
            error.Message,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "raw-provider-error",
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusReportsConnectedForValidResponse()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                EmptySessionResponse()));

        var provider = CreateProvider(handler);

        var health =
            await provider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.Connected,
            health.State);

        Assert.Equal(
            "Fortinet FortiGate SSL-VPN monitor API is reachable.",
            health.Reason);

        Assert.Single(handler.Requests);

        var request = Assert.Single(handler.Requests);

        Assert.Equal(
            HttpMethod.Get,
            request.Method);

        Assert.Equal(
            "/api/v2/monitor/vpn/ssl",
            request.RequestUri.AbsolutePath);
    }

    [Fact]
    public async Task StatusRejectsMalformedResponse()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                "{\"unexpected\":true}"));

        var provider = CreateProvider(handler);

        var health =
            await provider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.ProviderError,
            health.State);

        Assert.Equal(
    "Fortinet FortiGate provider response was invalid.",
    health.Reason);
    }

    [Fact]
    public async Task StatusMapsAuthenticationAndPermissionFailures()
    {
        var authenticationHandler =
            new RecordingHandler(
                (_, _) =>
                    Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.Unauthorized,
                            $"{Secret} authentication failure")));

        var authenticationProvider =
            CreateProvider(authenticationHandler);

        var authenticationHealth =
            await authenticationProvider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.AuthenticationFailed,
            authenticationHealth.State);

        Assert.DoesNotContain(
            Secret,
            authenticationHealth.Reason,
            StringComparison.Ordinal);

        var permissionHandler =
            new RecordingHandler(
                (_, _) =>
                    Task.FromResult(
                        JsonResponse(
                            HttpStatusCode.Forbidden,
                            $"{Secret} permission failure")));

        var permissionProvider =
            CreateProvider(permissionHandler);

        var permissionHealth =
            await permissionProvider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.PermissionDenied,
            permissionHealth.State);

        Assert.DoesNotContain(
            Secret,
            permissionHealth.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusTimeoutMapsToTimeoutWithoutRawExceptionDetails()
    {
        var handler = new RecordingHandler(
            (_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new TaskCanceledException(
                        "raw timeout detail")));

        var provider = CreateProvider(handler);

        var health =
            await provider.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.Timeout,
            health.State);

        Assert.DoesNotContain(
            "raw timeout detail",
            health.Reason,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsPropagated()
    {
        using var cancellation =
            new CancellationTokenSource();

        var handler = new RecordingHandler(
            async (_, ct) =>
            {
                cancellation.Cancel();

                await Task.Delay(
                    Timeout.InfiniteTimeSpan,
                    ct);

                return JsonResponse(
                    HttpStatusCode.OK,
                    "{}");
            });

        var provider = CreateProvider(handler);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.RunVpnDiagnosticsAsync(
                new VpnDiagnosticRequest(
                    User,
                    NetworkDiagnosticTargetType.User),
                cancellation.Token));
    }

    [Fact]
    public async Task NetworkFailureMapsToUnavailableAndDoesNotLeakSecret()
    {
        var handler = new RecordingHandler(
            (_, _) =>
                Task.FromException<HttpResponseMessage>(
                    new HttpRequestException(
                        $"{Secret} network failure")));

        var provider = CreateProvider(handler);

        var error =
            await Assert.ThrowsAsync<FortinetVpnException>(
                () => provider.RunVpnDiagnosticsAsync(
                    new VpnDiagnosticRequest(
                        User,
                        NetworkDiagnosticTargetType.User)));

        Assert.Equal(
            FortinetVpnError.Unavailable,
            error.Category);

        Assert.DoesNotContain(
            Secret,
            error.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task SerializedResultDoesNotContainApiToken()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                SessionResponse(
                    User,
                    "safe-session",
                    "198.51.100.100",
                    "10.0.0.100",
                    "web",
                    "ssl",
                    "employees")));

        var provider = CreateProvider(handler);

        var result =
            await provider.RunVpnDiagnosticsAsync(
                new VpnDiagnosticRequest(
                    User,
                    NetworkDiagnosticTargetType.User));

        var serialized =
            JsonSerializer.Serialize(result);

        Assert.DoesNotContain(
            Secret,
            serialized,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            Token,
            serialized,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            "Authorization",
            serialized,
            StringComparison.OrdinalIgnoreCase);

        Assert.DoesNotContain(
            "Bearer",
            serialized,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ProviderDetailsContainOnlyAllowListedFields()
    {
        var handler = SuccessfulHandler(
            _ => JsonResponse(
                HttpStatusCode.OK,
                SessionResponse(
                    User,
                    "allow-list-session",
                    "198.51.100.101",
                    "10.0.0.101",
                    "web",
                    "ssl",
                    "employees")));

        var provider = CreateProvider(handler);

        var result =
            await provider.RunVpnDiagnosticsAsync(
                new VpnDiagnosticRequest(
                    User,
                    NetworkDiagnosticTargetType.User));

        var fields =
            result.ProviderDetails!.Fields;

        Assert.All(
            fields.Keys,
            key =>
                Assert.DoesNotContain(
                    key,
                    new[]
                    {
                        "password",
                        "secret",
                        "token",
                        "credential",
                        "authorization",
                        "apikey",
                        "api_key",
                        "privatekey",
                        "private_key",
                        "certificate",
                        "cookie",
                        "header"
                    },
                    StringComparer.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task InfrastructureRegistersFortinetAdapterForBothProviderSeams()
    {
        var services =
            new ServiceCollection();

        services.AddAIOpsInfrastructure(
            new ConfigurationBuilder()
                .AddInMemoryCollection()
                .Build());

        using var serviceProvider =
            services.BuildServiceProvider();

        var fortinet =
            Assert.Single(
                serviceProvider
                    .GetServices<IVpnDiagnosticsProvider>(),
                provider =>
                    provider.ProviderName == "Fortinet");

        Assert.IsType<FortinetVpnProvider>(
            fortinet);

        Assert.Same(
            fortinet,
            serviceProvider
                .GetRequiredService<FortinetVpnProvider>());

        var networkProvider =
            Assert.Single(
                serviceProvider
                    .GetServices<INetworkDiagnosticProvider>(),
                provider =>
                    provider.ProviderName == "Fortinet");

        Assert.Same(
            fortinet,
            networkProvider);

        var health =
            await fortinet.GetConnectionStatusAsync();

        Assert.Equal(
            NetworkDiagnosticConnectionStates.NotConfigured,
            health.State);
    }

    private static FortinetVpnProvider CreateProvider(
        RecordingHandler handler,
        bool configured = true,
        string vdom = "root")
    {
        var options =
            new FortinetVpnOptions
            {
                BaseUrl = configured
                    ? BaseUrl
                    : null,

                ApiToken = configured
                    ? Token
                    : null,

                Vdom = vdom,

                TimeoutSeconds = 30
            };

        var client =
            new HttpClient(handler)
            {
                BaseAddress =
                    new Uri(BaseUrl),

                Timeout =
                    TimeSpan.FromSeconds(
                        options.TimeoutSeconds)
            };

        return new FortinetVpnProvider(
            client,
            new OptionsWrapper<FortinetVpnOptions>(
                options),
            TimeProvider.System);
    }

    private static RecordingHandler SuccessfulHandler(
        Func<HttpRequestMessage, HttpResponseMessage> apiResponse) =>
        new(
            (request, _) =>
                Task.FromResult(
                    apiResponse(request)));

    private static string SessionResponse(
        string user,
        string sessionId,
        string? sourceIp,
        string? tunnelIp,
        string? connectionType,
        string? tunnelProtocol,
        string? group)
    {
        return JsonSerializer.Serialize(
            new
            {
                results = new[]
                {
                    new
                    {
                        username = user,
                        index = sessionId,
                        source_ip = sourceIp,
                        tunnel_ip = tunnelIp,
                        type = connectionType,
                        protocol = tunnelProtocol,
                        group
                    }
                }
            });
    }

    private static string MultipleSessionResponse(
        string user)
    {
        return JsonSerializer.Serialize(
            new
            {
                results = new[]
                {
                    new
                    {
                        username = user,
                        index = "session-a",
                        source_ip = "198.51.100.201",
                        tunnel_ip = "10.0.0.201",
                        type = "web",
                        protocol = "ssl",
                        group = "employees"
                    },
                    new
                    {
                        username = user,
                        index = "session-b",
                        source_ip = "198.51.100.202",
                        tunnel_ip = "10.0.0.202",
                        type = "web",
                        protocol = "ssl",
                        group = "employees"
                    }
                }
            });
    }

    private static string EmptySessionResponse() =>
        JsonSerializer.Serialize(
            new
            {
                results = Array.Empty<object>()
            });

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode status,
        string body)
    {
        return new HttpResponseMessage(status)
        {
            Content =
                new StringContent(
                    body,
                    Encoding.UTF8,
                    "application/json")
        };
    }

    private sealed class RecordingHandler(
        Func<
            HttpRequestMessage,
            CancellationToken,
            Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(
                new RequestSnapshot(
                    request.Method,
                    request.RequestUri!,
                    request.Headers.Authorization?.Scheme,
                    request.Headers.Authorization?.Parameter,
                    request.Content is null
                        ? null
                        : await request.Content.ReadAsStringAsync(
                            cancellationToken)));

            return await send(
                request,
                cancellationToken);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);
}