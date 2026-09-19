using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using Xunit;

namespace AIOps.Tools.Tests;

public sealed class ResetPasswordToolTests
{
    private sealed class FakeDirectoryService : IDirectoryService
    {
        public bool WasCalled { get; private set; }

        public string? LastUserPrincipalName { get; private set; }

        public string ResultToReturn { get; set; } =
            """{"status":"password-reset"}""";

        public Exception? ExceptionToThrow { get; set; }

        public Task<string> ResetPasswordAsync(
            string userPrincipalName,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastUserPrincipalName = userPrincipalName;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResultToReturn);
        }

        public Task<string> GrantGroupAccessAsync(
            string userPrincipalName,
            string groupId,
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }
    }

    private static ToolExecutionContext CreateContext(Guid? approvalId = null)
    {
        return new ToolExecutionContext(
            TicketId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            ActorId: "test-agent",
            ApprovalId: approvalId);
    }

    [Fact]
    public void Metadata_IsSensitiveAndRequiresApproval()
    {
        var service = new FakeDirectoryService();
        var sut = new ResetPasswordTool(service);

        Assert.Equal(
            "Directory.ResetPassword",
            sut.Name);

        Assert.Equal(
            RiskLevel.Sensitive,
            sut.Risk);

        Assert.True(sut.RequiresApproval);
    }

    [Fact]
    public async Task WithoutApproval_IsRejected()
    {
        var service = new FakeDirectoryService();
        var sut = new ResetPasswordTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal(
            "ApprovalRequired",
            result.Error);

        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task WithApproval_ResetsPassword()
    {
        var service = new FakeDirectoryService
        {
            ResultToReturn =
                """{"user":"user@corp.example","status":"password-reset"}"""
        };

        var sut = new ResetPasswordTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example"}""",
            CreateContext(Guid.NewGuid()));

        Assert.True(result.Success);

        Assert.Contains(
            "Password reset requested successfully",
            result.Summary);

        Assert.Equal(
            """{"user":"user@corp.example","status":"password-reset"}""",
            result.DetailsJson);

        Assert.True(service.WasCalled);

        Assert.Equal(
            "user@corp.example",
            service.LastUserPrincipalName);
    }

    [Fact]
    public async Task MissingUserPrincipalName_IsRejected()
    {
        var service = new FakeDirectoryService();
        var sut = new ResetPasswordTool(service);

        var result = await sut.ExecuteAsync(
            """{"other":"value"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(
            "InvalidArguments",
            result.Error);

        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task EmptyUserPrincipalName_IsRejected()
    {
        var service = new FakeDirectoryService();
        var sut = new ResetPasswordTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"   "}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(
            "InvalidArguments",
            result.Error);

        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task InvalidJson_IsRejected()
    {
        var service = new FakeDirectoryService();
        var sut = new ResetPasswordTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(
            "InvalidArguments",
            result.Error);

        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsFailedToolResult()
    {
        var service = new FakeDirectoryService
        {
            ExceptionToThrow =
                new InvalidOperationException(
                    "Directory service unavailable.")
        };

        var sut = new ResetPasswordTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);

        Assert.Equal(
            "Directory service unavailable.",
            result.Error);

        Assert.True(service.WasCalled);
    }
}