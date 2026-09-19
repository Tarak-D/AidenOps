using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using Xunit;

namespace AIOps.Tools.Tests;

public sealed class GrantGroupAccessToolTests
{
    private sealed class FakeDirectoryService : IDirectoryService
    {
        public bool WasCalled { get; private set; }

        public string? LastUserPrincipalName { get; private set; }

        public string? LastGroupId { get; private set; }

        public string ResultToReturn { get; set; } =
            """{"status":"access-granted"}""";

        public Exception? ExceptionToThrow { get; set; }

        public Task<string> ResetPasswordAsync(
            string userPrincipalName,
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<string> GrantGroupAccessAsync(
            string userPrincipalName,
            string groupId,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastUserPrincipalName = userPrincipalName;
            LastGroupId = groupId;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResultToReturn);
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
        var sut = new GrantGroupAccessTool(service);

        Assert.Equal(
            "Directory.GrantGroupAccess",
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
        var sut = new GrantGroupAccessTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example","groupId":"group-123"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal(
            "ApprovalRequired",
            result.Error);

        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task WithApproval_GrantsGroupAccess()
    {
        var service = new FakeDirectoryService
        {
            ResultToReturn =
                """{"userPrincipalName":"user@corp.example","groupId":"group-123","status":"access-granted"}"""
        };

        var sut = new GrantGroupAccessTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example","groupId":"group-123"}""",
            CreateContext(Guid.NewGuid()));

        Assert.True(result.Success);

        Assert.Contains(
            "Group access granted successfully",
            result.Summary);

        Assert.Equal(
            """{"userPrincipalName":"user@corp.example","groupId":"group-123","status":"access-granted"}""",
            result.DetailsJson);

        Assert.True(service.WasCalled);
        Assert.Equal(
            "user@corp.example",
            service.LastUserPrincipalName);
        Assert.Equal(
            "group-123",
            service.LastGroupId);
    }

    [Fact]
    public async Task MissingUserPrincipalName_IsRejected()
    {
        var service = new FakeDirectoryService();
        var sut = new GrantGroupAccessTool(service);

        var result = await sut.ExecuteAsync(
            """{"groupId":"group-123"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(
            "InvalidArguments",
            result.Error);

        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task MissingGroupId_IsRejected()
    {
        var service = new FakeDirectoryService();
        var sut = new GrantGroupAccessTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example"}""",
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
        var sut = new GrantGroupAccessTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"   ","groupId":"group-123"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(
            "InvalidArguments",
            result.Error);

        Assert.False(service.WasCalled);
    }

    [Fact]
    public async Task EmptyGroupId_IsRejected()
    {
        var service = new FakeDirectoryService();
        var sut = new GrantGroupAccessTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example","groupId":"   "}""",
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
        var sut = new GrantGroupAccessTool(service);

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

        var sut = new GrantGroupAccessTool(service);

        var result = await sut.ExecuteAsync(
            """{"userPrincipalName":"user@corp.example","groupId":"group-123"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);

        Assert.Equal(
            "Directory service unavailable.",
            result.Error);

        Assert.True(service.WasCalled);
    }
}