using System.Net;
using System.Text;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using AIOps.Infrastructure;
using AIOps.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class CiscoSecureAccessVpnProviderTests
{
    private sealed class MemorySelectionStore : INetworkDiagnosticProviderSelectionStore
    {
        private NetworkDiagnosticProviderSelection? _selection;

        public Task<NetworkDiagnosticProviderSelection?> GetSelectionAsync(CancellationToken ct = default) =>
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

    private const string Secret = "cisco-secret-must-not-leak";
    private const string Token = "opaque-access-token";
    private const string User = "vpn.user@example.test";

    [Fact]
    public async Task MissingCredentialsReportNotConfiguredWithoutCallingCisco()
    {
        var handler = new RecordingHandler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected HTTP call."));
        var provider = CreateProvider(handler, configured: false);

        var health = await provider.GetConnectionStatusAsync();
        Assert.Equal(NetworkDiagnosticConnectionStates.NotConfigured, health.State);
        Assert.Equal("Cisco Secure Access credentials are not configured.", health.Reason);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UserLookupMapsDocumentedFieldsAndDoesNotFabricateMeasurements()
    {
        var apiObservedAt = DateTimeOffset.Parse("2026-09-29T11:12:13Z");
        var handler = SuccessfulHandler(_ => JsonResponse(HttpStatusCode.OK, SessionPage(
            1,
            (User, "laptop-7", "10.1.2.3", "2001:db8::1", "203.0.113.8", "session-1", "2026-09-28T10:11:12Z", "Remote")),
            apiObservedAt));
        var provider = CreateProvider(handler);

        var result = await provider.RunVpnDiagnosticsAsync(
            new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User));

        Assert.Equal("Success", result.Status);
        Assert.Equal("Cisco Secure Access", result.Provider);
        Assert.Equal(NetworkDiagnosticObservationType.Session, result.ObservationType);
        Assert.Equal(apiObservedAt, result.ObservedAt);
        Assert.Equal(User, result.User);
        Assert.Equal("laptop-7", result.Device);
        Assert.Equal("10.1.2.3", result.Ip);
        Assert.Null(result.ConnectionState);
        Assert.Null(result.Latency);
        Assert.Null(result.PacketLoss);
        Assert.Equal("session-1", result.ProviderDetails!.Fields["sessionId"]);
        Assert.Equal("2026-09-28T10:11:12Z", result.ProviderDetails.Fields["loginTime"]);
        Assert.Equal("Remote", result.ProviderDetails.Fields["profileName"]);
        Assert.Equal("2001:db8::1", result.ProviderDetails.Fields["assignedIpv6"]);
        Assert.Equal("203.0.113.8", result.ProviderDetails.Fields["publicIp"]);
        Assert.Contains(handler.Requests, request => request.RequestUri!.AbsolutePath == "/auth/v2/token");
        var tokenRequest = Assert.Single(handler.Requests, request => request.RequestUri!.AbsolutePath == "/auth/v2/token");
        Assert.Equal("Basic", tokenRequest.AuthorizationScheme);
        Assert.Equal("grant_type=client_credentials", tokenRequest.Body);
        Assert.DoesNotContain(Secret, tokenRequest.Body!, StringComparison.Ordinal);
        var lookup = Assert.Single(handler.Requests,
            request => request.RequestUri!.AbsolutePath.EndsWith("/vpn/userConnections", StringComparison.Ordinal));
        Assert.Contains("usernames=vpn.user%40example.test", lookup.RequestUri!.Query, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("Bearer", lookup.AuthorizationScheme);
        Assert.Equal(Token, lookup.AuthorizationParameter);
    }

    [Fact]
    public async Task MultipleSessionsSelectsTheUniqueMostRecentDocumentedLoginTime()
    {
        var handler = SuccessfulHandler(_ => JsonResponse(HttpStatusCode.OK, SessionPage(
            2,
            (User, "old-device", "10.0.0.1", null, null, "old", "2026-01-01T00:00:00Z", null),
            (User, "new-device", "10.0.0.2", null, null, "new", "2026-02-01T00:00:00Z", null))));
        var provider = CreateProvider(handler);

        var result = await provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User));

        Assert.Equal("new-device", result.Device);
        Assert.Equal("new", result.ProviderDetails!.Fields["sessionId"]);
        Assert.Equal("2026-02-01T00:00:00Z", result.ProviderDetails.Fields["loginTime"]);
    }

    [Fact]
    public async Task MultipleMatchingSessionsArePaginatedBeforeSelectingMostRecent()
    {
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/auth/v2/token")
            {
                return Task.FromResult(JsonResponse(HttpStatusCode.OK, TokenResponse()));
            }

            var offset = request.RequestUri.Query.Contains("offset=1", StringComparison.Ordinal) ? 1 : 0;
            var body = offset == 0
                ? SessionPage(2, (User, "page-one", null, null, null, "one", "2026-01-01T00:00:00Z", null))
                : SessionPage(2, (User, "page-two", null, null, null, "two", "2026-02-01T00:00:00Z", null));
            return Task.FromResult(JsonResponse(HttpStatusCode.OK, body));
        });
        var provider = CreateProvider(handler);

        var result = await provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User));

        Assert.Equal("page-two", result.Device);
        Assert.Contains(handler.Requests, request => request.RequestUri!.Query.Contains("offset=0", StringComparison.Ordinal));
        Assert.Contains(handler.Requests, request => request.RequestUri!.Query.Contains("offset=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AmbiguousMultipleSessionsFailRatherThanSelectingArbitrarily()
    {
        var handler = SuccessfulHandler(_ => JsonResponse(HttpStatusCode.OK, SessionPage(
            2,
            (User, "device-a", null, null, null, "a", null, null),
            (User, "device-b", null, null, null, "b", null, null))));
        var provider = CreateProvider(handler);

        var error = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));

        Assert.Equal(CiscoSecureAccessError.ProviderFailure, error.Category);
    }

    [Fact]
    public async Task NoMatchingSessionReturnsStableTargetNotFound()
    {
        var provider = CreateProvider(SuccessfulHandler(_ => JsonResponse(HttpStatusCode.OK, SessionPage(0))));

        var error = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));

        Assert.Equal(CiscoSecureAccessError.TargetNotFound, error.Category);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(NetworkDiagnosticTargetType.Device)]
    [InlineData(NetworkDiagnosticTargetType.Ip)]
    public async Task UnsupportedTargetsAreRejectedWithoutCallingCisco(NetworkDiagnosticTargetType targetType)
    {
        var handler = new RecordingHandler((_, _) => throw new Xunit.Sdk.XunitException("Unexpected HTTP call."));
        var provider = CreateProvider(handler);

        var error = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest("unsupported-target", targetType)));

        Assert.Equal(CiscoSecureAccessError.UnsupportedTarget, error.Category);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task MalformedAndEmptyResponsesAreHandledWithoutReturningRawBody()
    {
        var malformedProvider = CreateProvider(SuccessfulHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            "sensitive raw response should not escape")));
        var malformed = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            malformedProvider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));
        Assert.Equal(CiscoSecureAccessError.ProviderFailure, malformed.Category);
        Assert.DoesNotContain("sensitive raw response", malformed.Message, StringComparison.Ordinal);

        var emptyProvider = CreateProvider(SuccessfulHandler(_ => JsonResponse(
            HttpStatusCode.OK,
            SessionPage(0))));
        var empty = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            emptyProvider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));
        Assert.Equal(CiscoSecureAccessError.TargetNotFound, empty.Category);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, CiscoSecureAccessError.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, CiscoSecureAccessError.PermissionDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, CiscoSecureAccessError.Throttled)]
    [InlineData(HttpStatusCode.InternalServerError, CiscoSecureAccessError.Unavailable)]
    [InlineData(HttpStatusCode.BadRequest, CiscoSecureAccessError.ProviderFailure)]
    public async Task CiscoHttpFailuresMapToStableSanitizedCategories(HttpStatusCode status, string expectedCategory)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/auth/v2/token"
                ? JsonResponse(HttpStatusCode.OK, TokenResponse())
                : JsonResponse(status, $"{Secret} {Token} Authorization: Bearer raw-provider-error")));
        var provider = CreateProvider(handler);

        var error = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));

        Assert.Equal(expectedCategory, error.Category);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("raw-provider-error", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("api.sse.cisco.com", error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("Basic", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TokenAuthenticationFailureIsSanitized()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(JsonResponse(
            HttpStatusCode.Unauthorized,
            $"{Secret} raw authentication response")));
        var provider = CreateProvider(handler);

        var error = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));

        Assert.Equal(CiscoSecureAccessError.AuthenticationFailed, error.Category);
        Assert.DoesNotContain(Secret, error.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("raw authentication response", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusRequiresSuccessfulTokenAndReadPermissionCheck()
    {
        var handler = SuccessfulHandler(_ => JsonResponse(HttpStatusCode.OK, SessionPage(0)));
        var provider = CreateProvider(handler);

        var health = await provider.GetConnectionStatusAsync();

        Assert.Equal(NetworkDiagnosticConnectionStates.Connected, health.State);
        Assert.Equal(2, handler.Requests.Count);
        Assert.Contains(handler.Requests, request => request.RequestUri!.Query.Contains("limit=1", StringComparison.Ordinal));
    }

    [Fact]
    public async Task InfrastructureRegistersTheCiscoAdapterForBothProviderSeams()
    {
        var services = new ServiceCollection();
        services.AddAIOpsInfrastructure(new ConfigurationBuilder().AddInMemoryCollection().Build());
        using var serviceProvider = services.BuildServiceProvider();

        var cisco = Assert.Single(serviceProvider.GetServices<IVpnDiagnosticsProvider>(),
            provider => provider.ProviderName == "Cisco");
        Assert.IsType<CiscoSecureAccessVpnProvider>(cisco);
        Assert.Same(cisco, serviceProvider.GetRequiredService<CiscoSecureAccessVpnProvider>());
        Assert.Same(cisco, Assert.Single(serviceProvider.GetServices<INetworkDiagnosticProvider>(),
            provider => provider.ProviderName == "Cisco"));
        Assert.Equal(NetworkDiagnosticConnectionStates.NotConfigured,
            (await cisco.GetConnectionStatusAsync()).State);

        var selectionStore = new MemorySelectionStore();
        await selectionStore.SetSelectionAsync(new NetworkDiagnosticProviderSelection("Vpn", "Cisco"), "test");
        var selection = new NetworkDiagnosticProviderSelectionService(
            selectionStore,
            serviceProvider.GetServices<INetworkDiagnosticProvider>());
        var router = new ConfiguredVpnDiagnostics(selection, serviceProvider.GetServices<IVpnDiagnosticsProvider>());
        var error = await Assert.ThrowsAsync<CiscoSecureAccessException>(() => router.RunVpnDiagnosticsAsync(
            new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));
        Assert.Equal(CiscoSecureAccessError.NotConfigured, error.Category);
    }

    [Fact]
    public async Task StatusTimeoutMapsToTimeoutWithoutRawExceptionDetails()
    {
        var handler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("raw timeout detail")));
        var provider = CreateProvider(handler);

        var health = await provider.GetConnectionStatusAsync();

        Assert.Equal(NetworkDiagnosticConnectionStates.Timeout, health.State);
        Assert.DoesNotContain("raw timeout detail", health.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StatusRejectsMalformedCiscoApiResponse()
    {
        var handler = SuccessfulHandler(_ => JsonResponse(HttpStatusCode.OK, "{\"unexpected\":true}"));
        var provider = CreateProvider(handler);

        var health = await provider.GetConnectionStatusAsync();

        Assert.Equal(NetworkDiagnosticConnectionStates.ProviderError, health.State);
        Assert.Equal("Cisco Secure Access provider request failed.", health.Reason);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, NetworkDiagnosticConnectionStates.AuthenticationFailed)]
    [InlineData(HttpStatusCode.Forbidden, NetworkDiagnosticConnectionStates.PermissionDenied)]
    [InlineData(HttpStatusCode.TooManyRequests, NetworkDiagnosticConnectionStates.ProviderError)]
    [InlineData(HttpStatusCode.ServiceUnavailable, NetworkDiagnosticConnectionStates.Unavailable)]
    public async Task StatusMapsCiscoFailureCategories(HttpStatusCode status, string expectedState)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/auth/v2/token"
                ? JsonResponse(HttpStatusCode.OK, TokenResponse())
                : JsonResponse(status, "raw secret provider response")));
        var provider = CreateProvider(handler);

        var health = await provider.GetConnectionStatusAsync();

        Assert.Equal(expectedState, health.State);
        Assert.DoesNotContain("raw secret", health.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CancellationIsPropagatedAndTimeoutIsSanitized()
    {
        using var cancellation = new CancellationTokenSource();
        var canceledHandler = new RecordingHandler(async (_, ct) =>
        {
            cancellation.Cancel();
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return JsonResponse(HttpStatusCode.OK, "{}");
        });
        var canceledProvider = CreateProvider(canceledHandler);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => canceledProvider.RunVpnDiagnosticsAsync(
            new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User), cancellation.Token));

        var timeoutHandler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("raw timeout detail")));
        var timeoutProvider = CreateProvider(timeoutHandler);
        var timeout = await Assert.ThrowsAsync<CiscoSecureAccessException>(() => timeoutProvider.RunVpnDiagnosticsAsync(
            new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));
        Assert.Equal(CiscoSecureAccessError.Timeout, timeout.Category);
        Assert.DoesNotContain("raw timeout detail", timeout.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NetworkFailureIsUnavailableAndNoSecretOrTokenEntersSerializedResult()
    {
        var handler = SuccessfulHandler(_ => JsonResponse(HttpStatusCode.OK, SessionPage(
            1, (User, null, null, null, null, "session-safe", "2026-01-01T00:00:00Z", null))));
        var provider = CreateProvider(handler);
        var result = await provider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User));
        var serialized = System.Text.Json.JsonSerializer.Serialize(result);
        Assert.DoesNotContain(Secret, serialized, StringComparison.Ordinal);
        Assert.DoesNotContain(Token, serialized, StringComparison.Ordinal);

        var failureProvider = CreateProvider(new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException($"{Secret} network failure"))));
        var failure = await Assert.ThrowsAsync<CiscoSecureAccessException>(() =>
            failureProvider.RunVpnDiagnosticsAsync(new VpnDiagnosticRequest(User, NetworkDiagnosticTargetType.User)));
        Assert.Equal(CiscoSecureAccessError.Unavailable, failure.Category);
        Assert.DoesNotContain(Secret, failure.Message, StringComparison.Ordinal);
    }

    private static CiscoSecureAccessVpnProvider CreateProvider(
        RecordingHandler handler,
        bool configured = true)
    {
        var options = new CiscoSecureAccessOptions
        {
            BaseUrl = "https://api.sse.cisco.com/",
            ClientId = configured ? "server-client-id" : null,
            ClientSecret = configured ? Secret : null,
            TimeoutSeconds = 30
        };
        var client = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://api.sse.cisco.com/"),
            Timeout = TimeSpan.FromSeconds(options.TimeoutSeconds)
        };
        return new CiscoSecureAccessVpnProvider(
            client,
            new OptionsWrapper<CiscoSecureAccessOptions>(options),
            TimeProvider.System);
    }

    private static RecordingHandler SuccessfulHandler(
        Func<HttpRequestMessage, HttpResponseMessage> apiResponse) =>
        new((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/auth/v2/token"
                ? JsonResponse(HttpStatusCode.OK, TokenResponse())
                : apiResponse(request)));

    private static string TokenResponse() => """{"access_token":"opaque-access-token","expires_in":3600,"token_type":"Bearer"}""";

    private static string SessionPage(
        int total,
        params (string User, string? Device, string? Ip, string? Ipv6, string? PublicIp, string? Session, string? Login, string? Profile)[] sessions) =>
        System.Text.Json.JsonSerializer.Serialize(new
        {
            offset = 0,
            limit = 1000,
            total,
            data = sessions.Select(session => new
            {
                username = session.User,
                deviceName = session.Device,
                assignedIp = session.Ip,
                assignedIpv6 = session.Ipv6,
                publicIp = session.PublicIp,
                sessionId = session.Session,
                loginTime = session.Login,
                profileName = session.Profile
            })
        });

    private static HttpResponseMessage JsonResponse(
        HttpStatusCode status,
        string body,
        DateTimeOffset? responseDate = null)
    {
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        if (responseDate.HasValue)
        {
            response.Headers.Date = responseDate;
        }

        return response;
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<RequestSnapshot> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RequestSnapshot(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter,
                request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
            return await send(request, cancellationToken);
        }
    }

    private sealed record RequestSnapshot(
        HttpMethod Method,
        Uri RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter,
        string? Body);
}
