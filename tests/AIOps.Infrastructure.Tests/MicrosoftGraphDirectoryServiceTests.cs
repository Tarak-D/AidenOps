using System.Net;
using System.Text;
using System.Text.Json;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Graph;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Infrastructure;
using AIOps.Infrastructure.Integrations;

namespace AIOps.Infrastructure.Tests;

public sealed class MicrosoftGraphDirectoryServiceTests
{
    private const string UserUpn = "alex@example.test";
    private const string UserId = "user-object-id";
    private const string GroupId = "group-object-id";

    [Fact]
    public void ProviderSelection_MissingProviderUsesSimulated()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>());

        Assert.IsType<SimulatedDirectoryService>(
            provider.GetRequiredService<IDirectoryService>());
    }

    [Fact]
    public void ProviderSelection_SimulatedUsesSimulated()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Integrations:Directory:Provider"] = "Simulated"
        });

        Assert.IsType<SimulatedDirectoryService>(
            provider.GetRequiredService<IDirectoryService>());
    }

    [Fact]
    public void ProviderSelection_MicrosoftGraphUsesGraphProviderWithoutAuthenticating()
    {
        using var provider = BuildProvider(new Dictionary<string, string?>
        {
            ["Integrations:Directory:Provider"] = "MicrosoftGraph"
        });

        Assert.IsType<MicrosoftGraphDirectoryService>(
            provider.GetRequiredService<IDirectoryService>());
    }

    [Theory]
    [InlineData("")]
    [InlineData("Unknown")]
    public void ProviderSelection_InvalidProviderFailsClearly(string value)
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildProvider(new Dictionary<string, string?>
            {
                ["Integrations:Directory:Provider"] = value
            }));

        Assert.Equal(
            "Integrations:Directory:Provider must be 'Simulated' or 'MicrosoftGraph'.",
            exception.Message);
    }

    [Fact]
    public void ProviderSelection_ExplicitNullFailsClearly()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            BuildProvider(new Dictionary<string, string?>
            {
                ["Integrations:Directory:Provider"] = null
            }));

        Assert.Equal(
            "Integrations:Directory:Provider must be 'Simulated' or 'MicrosoftGraph'.",
            exception.Message);
    }

    [Fact]
    public async Task GrantGroupAccess_PostsGraphUserReferenceAndReturnsSafeReceipt()
    {
        var captured = new List<(string Method, string Path, string? Body)>();
        var service = CreateService((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            var decodedPath = Uri.UnescapeDataString(path);
            var body = request.Content?.ReadAsStringAsync().GetAwaiter().GetResult();
            captured.Add((request.Method.Method, path, body));

            if (decodedPath.StartsWith($"/v1.0/users/{UserUpn}", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{UserId}}"}"""));
            }

            if (decodedPath.StartsWith($"/v1.0/groups/{GroupId}", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{GroupId}}","isAssignableToRole":false}"""));
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        });

        var result = await service.GrantGroupAccessAsync(UserUpn, GroupId);

        Assert.Equal(
            "{\"status\":\"access-granted\",\"provider\":\"microsoft-graph\"}",
            result);
        Assert.Equal(3, captured.Count);
        Assert.Contains("/v1.0/users/", captured[0].Path, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("/v1.0/groups/group-object-id", captured[1].Path, StringComparison.Ordinal);
        Assert.Contains("isAssignableToRole", captured[1].Path, StringComparison.Ordinal);
        Assert.Equal("POST", captured[2].Method);
        Assert.Contains("/v1.0/groups/group-object-id/members/$ref", captured[2].Path, StringComparison.Ordinal);
        Assert.Contains($"https://graph.microsoft.com/v1.0/users/{UserId}", captured[2].Body);
    }

    [Fact]
    public async Task GrantGroupAccess_RoleAssignableGroupIsRejectedWithoutAddingMembership()
    {
        var capturedPaths = new List<string>();
        var service = CreateService((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            capturedPaths.Add(path);
            var decodedPath = Uri.UnescapeDataString(path);

            if (decodedPath.StartsWith($"/v1.0/users/{UserUpn}", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{UserId}}"}"""));
            }

            return Task.FromResult(Json(
                HttpStatusCode.OK,
                $$"""{"id":"{{GroupId}}","isAssignableToRole":true}"""));
        });

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal("DirectoryRoleAssignableGroupNotSupported", exception.Code);
        Assert.Equal(
            "DirectoryRoleAssignableGroupNotSupported: Role-assignable groups are not supported by this directory operation.",
            exception.Message);
        Assert.DoesNotContain(GroupId + "/members/$ref", string.Join("\n", capturedPaths));
        Assert.Equal(2, capturedPaths.Count);
    }

    [Fact]
    public async Task GrantGroupAccess_MissingRoleAssignmentMetadataFailsClosed()
    {
        var capturedPaths = new List<string>();
        var service = CreateService((request, _) =>
        {
            var path = request.RequestUri!.PathAndQuery;
            capturedPaths.Add(path);
            var decodedPath = Uri.UnescapeDataString(path);

            if (decodedPath.StartsWith($"/v1.0/users/{UserUpn}", StringComparison.Ordinal))
            {
                return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{UserId}}"}"""));
            }

            return Task.FromResult(Json(HttpStatusCode.OK, $$"""{"id":"{{GroupId}}"}"""));
        });

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal("DirectoryGroupMetadataUnavailable", exception.Code);
        Assert.DoesNotContain("RAW", exception.Message);
        Assert.DoesNotContain(GroupId + "/members/$ref", string.Join("\n", capturedPaths));
        Assert.Equal(2, capturedPaths.Count);
    }

    [Fact]
    public async Task GrantGroupAccess_UserNotFoundIsSanitized()
    {
        var service = CreateService((_, _) => Task.FromResult(
            Json(HttpStatusCode.NotFound, GraphError("Request_ResourceNotFound", "RAW GRAPH ERROR"))));

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal("DirectoryUserNotFound", exception.Code);
        Assert.DoesNotContain("RAW GRAPH ERROR", exception.Message);
    }

    [Fact]
    public async Task GrantGroupAccess_GroupNotFoundIsSanitized()
    {
        var requestCount = 0;
        var service = CreateService((_, _) => Task.FromResult(
            ++requestCount == 1
                ? Json(HttpStatusCode.OK, $$"""{"id":"{{UserId}}"}""")
                : Json(HttpStatusCode.NotFound, GraphError("Request_ResourceNotFound", "RAW GRAPH ERROR"))));

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal("DirectoryGroupNotFound", exception.Code);
        Assert.DoesNotContain("RAW GRAPH ERROR", exception.Message);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "DirectoryAuthenticationFailed")]
    [InlineData(HttpStatusCode.Forbidden, "DirectoryPermissionDenied")]
    [InlineData((HttpStatusCode)429, "DirectoryThrottled")]
    [InlineData(HttpStatusCode.BadRequest, "DirectoryOperationFailed")]
    [InlineData(HttpStatusCode.InternalServerError, "DirectoryOperationFailed")]
    public async Task GrantGroupAccess_GraphFailuresAreSanitized(
        HttpStatusCode status,
        string expectedCode)
    {
        var requestCount = 0;
        var service = CreateService((_, _) => Task.FromResult(
            ++requestCount < 3
                ? Json(HttpStatusCode.OK, "{\"id\":\"object-id\",\"isAssignableToRole\":false}")
                : Json(status, GraphError("GraphFailure", "SECRET TOKEN AND RAW ERROR"))));

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal(expectedCode, exception.Code);
        Assert.DoesNotContain("SECRET", exception.Message);
        Assert.Equal(3, requestCount);
    }

    [Fact]
    public async Task GrantGroupAccess_AlreadyMemberIsExplicitAndDoesNotRetry()
    {
        var requestCount = 0;
        var service = CreateService((_, _) => Task.FromResult(
            ++requestCount < 3
                ? Json(HttpStatusCode.OK, "{\"id\":\"object-id\",\"isAssignableToRole\":false}")
                : Json(HttpStatusCode.BadRequest,
                    GraphError("Request_BadRequest", "One or more added object references already exist."))));

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal("DirectoryAlreadyMember", exception.Code);
        Assert.Contains("already a member", exception.Message);
        Assert.Equal(3, requestCount);
    }

    [Fact]
    public async Task GrantGroupAccess_AuthenticationFailureIsSanitized()
    {
        var service = CreateService(
            (_, _) => throw new InvalidOperationException("HTTP must not be called."),
            new ThrowingCredential());

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal("DirectoryAuthenticationFailed", exception.Code);
        Assert.DoesNotContain("raw credential detail", exception.Message);
    }

    [Fact]
    public async Task GrantGroupAccess_TimeoutIsSanitized()
    {
        var service = CreateService(
            async (_, ct) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(100));

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId));

        Assert.Equal("DirectoryTimeout", exception.Code);
    }

    [Fact]
    public async Task GrantGroupAccess_CancellationPropagates()
    {
        var service = CreateService(async (_, ct) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => service.GrantGroupAccessAsync(UserUpn, GroupId, cancellation.Token));
    }

    [Fact]
    public async Task ResetPassword_IsExplicitlyUnsupportedForMicrosoftGraph()
    {
        var service = CreateService((_, _) =>
            throw new InvalidOperationException("Graph must not be called."));

        var exception = await Assert.ThrowsAsync<DirectoryProviderException>(
            () => service.ResetPasswordAsync(UserUpn));

        Assert.Equal("DirectoryPasswordResetNotConfigured", exception.Code);
    }

    private static ServiceProvider BuildProvider(
        Dictionary<string, string?> settings)
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(settings)
            .Build();
        var services = new ServiceCollection();
        services.AddAIOpsInfrastructure(config);
        return services.BuildServiceProvider();
    }

    private static MicrosoftGraphDirectoryService CreateService(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send,
        TokenCredential? credential = null,
        TimeSpan? timeout = null)
    {
        var handler = new StubHttpMessageHandler(send);
        var httpClient = new HttpClient(handler)
        {
            Timeout = timeout ?? TimeSpan.FromSeconds(30)
        };
        var graph = new GraphServiceClient(
            httpClient,
            credential ?? new StaticCredential(),
            ["https://graph.microsoft.com/.default"]);
        return new MicrosoftGraphDirectoryService(graph);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string content) => new(status)
    {
        Content = new StringContent(content, Encoding.UTF8, "application/json")
    };

    private static string GraphError(string code, string message) =>
        JsonSerializer.Serialize(new { error = new { code, message } });

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class StaticCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            new("test-token", DateTimeOffset.UtcNow.AddHours(1));

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromResult(GetToken(requestContext, cancellationToken));
    }

    private sealed class ThrowingCredential : TokenCredential
    {
        public override AccessToken GetToken(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            throw new AuthenticationFailedException("raw credential detail");

        public override ValueTask<AccessToken> GetTokenAsync(
            TokenRequestContext requestContext,
            CancellationToken cancellationToken) =>
            ValueTask.FromException<AccessToken>(
                new AuthenticationFailedException("raw credential detail"));
    }
}
