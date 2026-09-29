using System.Net;
using System.Text;
using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Infrastructure.Integrations;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class ServiceNowItsmConnectorTests
{
    private const string ClientSecret = "test-client-secret-never-return";
    private const string AccessToken = "test-access-token-never-return";

    [Fact]
    public async Task MissingConfiguration_IsNotConfigured_AndUpdateFailsClosed()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, new ServiceNowItsmOptions());

        Assert.Equal("NotConfigured", await connector.GetConnectionStatusAsync());
        var exception = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => connector.UpdateTicketAsync("INC0012345", "note"));
        Assert.Equal("ServiceNowNotConfigured", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("http://servicenow.example")]
    [InlineData("not-a-url")]
    [InlineData("https://user:pass@servicenow.example")]
    [InlineData("https://servicenow.example?token=bad")]
    public async Task InvalidBaseUrl_IsRejectedWithoutRequest(string baseUrl)
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions(baseUrl));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.UpdateTicketAsync("INC0012345", "note"));

        Assert.Contains("BaseUrl", exception.Message);
        Assert.DoesNotContain(ClientSecret, exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("BaseUrl")]
    [InlineData("ClientId")]
    [InlineData("ClientSecret")]
    public async Task MissingConfigurationValue_IsReportedClearly(string missingSetting)
    {
        var options = new ServiceNowItsmOptions
        {
            BaseUrl = "https://servicenow.example/",
            ClientId = "client-id",
            ClientSecret = ClientSecret
        };
        switch (missingSetting)
        {
            case "BaseUrl": options.BaseUrl = ""; break;
            case "ClientId": options.ClientId = ""; break;
            case "ClientSecret": options.ClientSecret = ""; break;
        }
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => connector.UpdateTicketAsync("INC0012345", "note"));

        Assert.Contains(missingSetting, exception.Message);
        Assert.DoesNotContain(ClientSecret, exception.Message);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task OAuthUsesClientCredentials_AndSuccessfulUpdateMapsWorkNotesAndResolvedState()
    {
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal))
            {
                var form = await request.Content!.ReadAsStringAsync(ct);
                Assert.Contains("grant_type=client_credentials", form);
                Assert.Contains("client_id=client-id", form);
                Assert.Contains("client_secret=test-client-secret-never-return", form);
                Assert.Equal("application/x-www-form-urlencoded", request.Content.Headers.ContentType!.MediaType);
                return Json(HttpStatusCode.OK, Token());
            }

            if (request.Method == HttpMethod.Get)
            {
                Assert.Contains("number%3DINC0012345", request.RequestUri.Query);
                Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
                Assert.Equal(AccessToken, request.Headers.Authorization?.Parameter);
                return Json(HttpStatusCode.OK, """{"result":[{"sys_id":"record-1","number":"INC0012345"}]}""");
            }

            Assert.Equal(HttpMethod.Patch, request.Method);
            Assert.Equal("record-1", request.RequestUri!.Segments[^1]);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            var body = await request.Content!.ReadAsStringAsync(ct);
            using var update = JsonDocument.Parse(body);
            Assert.Equal("work note", update.RootElement.GetProperty("work_notes").GetString());
            Assert.Equal("6", update.RootElement.GetProperty("state").GetString());
            return Json(HttpStatusCode.OK, $"{{\"result\":{{\"number\":\"INC0012345\",\"work_notes\":\"{ClientSecret}\"}}}}");
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        var result = await connector.UpdateTicketAsync("INC0012345", "work note", "Resolved");

        Assert.Contains("INC0012345", result);
        Assert.Contains("\"updated\":true", result);
        Assert.DoesNotContain(ClientSecret, result);
        Assert.DoesNotContain(AccessToken, result);
        Assert.Equal(3, handler.Requests.Count);
    }

    [Fact]
    public async Task TokenIsCachedUntilNearExpiryThenRefreshed()
    {
        var clock = new ManualTimeProvider();
        var tokensIssued = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal))
            {
                tokensIssued++;
                return Task.FromResult(Json(HttpStatusCode.OK, Token("token-" + tokensIssued)));
            }

            if (request.Method == HttpMethod.Get)
            {
                return Task.FromResult(Json(HttpStatusCode.OK, """{"result":[{"sys_id":"id","number":"INC0012345"}]}"""));
            }

            return Task.FromResult(Json(HttpStatusCode.NoContent));
        });
        var connector = CreateConnector(handler, ConfiguredOptions(), clock);

        await connector.UpdateTicketAsync("INC0012345", "one");
        await connector.UpdateTicketAsync("INC0012345", "two");
        Assert.Equal(1, tokensIssued);

        clock.Advance(TimeSpan.FromSeconds(3541));
        await connector.UpdateTicketAsync("INC0012345", "three");

        Assert.Equal(2, tokensIssued);
    }

    [Fact]
    public async Task MalformedTokenResponse_IsSanitizedAuthenticationFailure()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(
            HttpStatusCode.OK,
            """{"access_token":"secret-token-without-expiry"}""")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => connector.UpdateTicketAsync("INC0012345", "note"));

        Assert.Equal("ServiceNowAuthenticationFailed", exception.Category);
        Assert.DoesNotContain("secret-token-without-expiry", exception.Message);
        Assert.DoesNotContain(ClientSecret, exception.Message);
    }

    [Fact]
    public async Task OAuthFailure_IsSanitizedAuthenticationFailure()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(
            Json(HttpStatusCode.Unauthorized, ClientSecret + AccessToken)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => connector.UpdateTicketAsync("INC0012345", "note"));

        Assert.Equal("ServiceNowAuthenticationFailed", exception.Category);
        Assert.DoesNotContain(ClientSecret, exception.Message);
        Assert.DoesNotContain(AccessToken, exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "ServiceNowAuthenticationFailed")]
    [InlineData(HttpStatusCode.Forbidden, "ServiceNowPermissionDenied")]
    [InlineData(HttpStatusCode.NotFound, "ServiceNowTicketNotFound")]
    [InlineData(HttpStatusCode.TooManyRequests, "ServiceNowThrottled")]
    [InlineData(HttpStatusCode.InternalServerError, "ServiceNowProviderFailure")]
    public async Task IncidentLookupMapsHttpFailuresToStableCategories(
        HttpStatusCode statusCode,
        string expectedCategory)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, Token())
                : Json(statusCode, ClientSecret + AccessToken)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => connector.UpdateTicketAsync("INC0012345", "note"));

        Assert.Equal(expectedCategory, exception.Category);
        Assert.DoesNotContain(ClientSecret, exception.Message);
        Assert.DoesNotContain(AccessToken, exception.Message);
    }

    [Fact]
    public async Task MissingIncident_ReturnsTicketNotFound()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, Token())
                : Json(HttpStatusCode.OK, """{"result":[]}""")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => connector.UpdateTicketAsync("INC0099999", "note"));

        Assert.Equal("ServiceNowTicketNotFound", exception.Category);
    }

    [Fact]
    public async Task NullStateOmitsServiceNowStateField()
    {
        string? capturedBody = null;
        var handler = new RecordingHandler(async (request, ct) =>
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal))
            {
                return Json(HttpStatusCode.OK, Token());
            }

            if (request.Method == HttpMethod.Get)
            {
                return Json(HttpStatusCode.OK, """{"result":[{"sys_id":"id","number":"INC0012345"}]}""");
            }

            capturedBody = await request.Content!.ReadAsStringAsync(ct);
            return Json(HttpStatusCode.NoContent);
        });
        var connector = CreateConnector(handler, ConfiguredOptions());

        await connector.UpdateTicketAsync("INC0012345", "work note", state: null);

        using var body = JsonDocument.Parse(capturedBody!);
        Assert.Equal("work note", body.RootElement.GetProperty("work_notes").GetString());
        Assert.False(body.RootElement.TryGetProperty("state", out _));
    }

    [Fact]
    public async Task InvalidStateIsRejectedBeforeAnyHttpRequest()
    {
        var handler = new RecordingHandler((_, _) => Task.FromResult(Json(HttpStatusCode.OK)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        var exception = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => connector.UpdateTicketAsync("INC0012345", "note", "arbitrary state"));

        Assert.Equal("ServiceNowInvalidState", exception.Category);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task ConnectionStatusRequiresSuccessfulAuthenticatedApiCall()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, Token())
                : Json(HttpStatusCode.OK, """{"result":[]}""")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        Assert.Equal("Connected", await connector.GetConnectionStatusAsync());
        Assert.Equal(2, handler.Requests.Count);
        Assert.Equal(HttpMethod.Get, handler.Requests[1].Method);
        Assert.Equal("Bearer", handler.Requests[1].AuthorizationScheme);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    [InlineData(HttpStatusCode.RequestTimeout)]
    public async Task ConnectionStatusReturnsUnavailableWhenAuthenticatedCheckFails(HttpStatusCode statusCode)
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, Token())
                : Json(statusCode)));
        var connector = CreateConnector(handler, ConfiguredOptions());

        Assert.Equal("Unavailable", await connector.GetConnectionStatusAsync());
    }

    [Fact]
    public async Task ConnectionStatusReturnsUnavailableOnTransportTimeout()
    {
        var handler = new RecordingHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath.EndsWith("/oauth_token.do", StringComparison.Ordinal)
                ? Json(HttpStatusCode.OK, Token())
                : throw new TaskCanceledException("raw timeout detail")));
        var connector = CreateConnector(handler, ConfiguredOptions());

        Assert.Equal("Unavailable", await connector.GetConnectionStatusAsync());
    }

    [Fact]
    public async Task CancellationPropagatesAndTimeoutUsesStableCategory()
    {
        var cancellationHandler = new RecordingHandler(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return Json(HttpStatusCode.OK);
        });
        var cancellationConnector = CreateConnector(cancellationHandler, ConfiguredOptions());
        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(20));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => cancellationConnector.UpdateTicketAsync("INC0012345", "note", ct: cancellation.Token));

        var timeoutHandler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new TaskCanceledException("raw timeout detail")));
        var timeoutConnector = CreateConnector(timeoutHandler, ConfiguredOptions());
        var timeout = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => timeoutConnector.UpdateTicketAsync("INC0012345", "note"));

        Assert.Equal("ServiceNowTimeout", timeout.Category);
        Assert.DoesNotContain("raw timeout detail", timeout.Message);

        var wrappedTimeoutHandler = new RecordingHandler((_, _) =>
            Task.FromException<HttpResponseMessage>(new HttpRequestException(
                "raw request detail",
                new TimeoutException("timeout detail"))));
        var wrappedTimeoutConnector = CreateConnector(wrappedTimeoutHandler, ConfiguredOptions());
        var wrappedTimeout = await Assert.ThrowsAsync<ServiceNowConnectorException>(
            () => wrappedTimeoutConnector.UpdateTicketAsync("INC0012345", "note"));
        Assert.Equal("ServiceNowTimeout", wrappedTimeout.Category);
        Assert.DoesNotContain("raw request detail", wrappedTimeout.Message);
        Assert.DoesNotContain("timeout detail", wrappedTimeout.Message);
    }

    private static string Token(string token = AccessToken) =>
        $$"""{"access_token":"{{token}}","token_type":"Bearer","expires_in":3600}""";

    private static HttpResponseMessage Json(HttpStatusCode status, string? body = null) =>
        new(status)
        {
            Content = new StringContent(body ?? "{}", Encoding.UTF8, "application/json")
        };

    private static ServiceNowItsmOptions ConfiguredOptions(string baseUrl = "https://servicenow.example/") => new()
    {
        BaseUrl = baseUrl,
        ClientId = "client-id",
        ClientSecret = ClientSecret
    };

    private static ServiceNowItsmConnector CreateConnector(
        HttpMessageHandler handler,
        ServiceNowItsmOptions serviceNow,
        TimeProvider? timeProvider = null)
    {
        var options = new ItsmIntegrationOptions
        {
            TimeoutSeconds = 2,
            ServiceNow = serviceNow
        };
        return new ServiceNowItsmConnector(
            new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(2) },
            Options.Create(options),
            timeProvider ?? new ManualTimeProvider());
    }

    private sealed class RecordingHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        public List<RecordedRequest> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Requests.Add(new RecordedRequest(
                request.Method,
                request.RequestUri!,
                request.Headers.Authorization?.Scheme,
                request.Headers.Authorization?.Parameter));
            return await send(request, cancellationToken);
        }
    }

    private sealed record RecordedRequest(
        HttpMethod Method,
        Uri RequestUri,
        string? AuthorizationScheme,
        string? AuthorizationParameter);

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow = DateTimeOffset.Parse("2026-01-01T00:00:00Z");

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration) => _utcNow += duration;
    }
}
