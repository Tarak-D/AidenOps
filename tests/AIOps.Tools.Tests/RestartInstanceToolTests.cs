using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AIOps.Tools.Tests;

public sealed class RestartInstanceToolTests
{
    private sealed class FakeCloudProvider : ICloudProvider
    {
        public bool WasCalled { get; private set; }

        public string? LastInstanceId { get; private set; }

        public string ResultToReturn { get; set; } =
            """{"status":"restarted"}""";

        public Exception? ExceptionToThrow { get; set; }

        public Task<string> RestartInstanceAsync(
            string instanceId,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastInstanceId = instanceId;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResultToReturn);
        }

        public Task<string> GetInstanceStatusAsync(
            string instanceId,
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
    public void Metadata_IsModerateAndRequiresApproval()
    {
        var provider = new FakeCloudProvider();
        var sut = new RestartInstanceTool(provider);

        Assert.Equal("Cloud.RestartInstance", sut.Name);
        Assert.Equal(RiskLevel.Moderate, sut.Risk);
        Assert.True(sut.RequiresApproval);
    }

    [Fact]
    public async Task WithoutApproval_IsRejected()
    {
        var provider = new FakeCloudProvider();
        var sut = new RestartInstanceTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":"i-12345"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("ApprovalRequired", result.Error);
        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task WithApproval_RestartsInstance()
    {
        var provider = new FakeCloudProvider
        {
            ResultToReturn = """{"instanceId":"i-12345","status":"restarted"}"""
        };

        var sut = new RestartInstanceTool(provider);
        var approvalId = Guid.NewGuid();

        var result = await sut.ExecuteAsync(
            """{"instanceId":"i-12345"}""",
            CreateContext(approvalId));

        Assert.True(result.Success);
        Assert.Contains("restart requested successfully", result.Summary);
        Assert.Equal(
            """{"instanceId":"i-12345","status":"restarted"}""",
            result.DetailsJson);

        Assert.True(provider.WasCalled);
        Assert.Equal("i-12345", provider.LastInstanceId);
    }

    [Fact]
    public async Task MissingInstanceId_IsRejected()
    {
        var provider = new FakeCloudProvider();
        var sut = new RestartInstanceTool(provider);

        var result = await sut.ExecuteAsync(
            """{"other":"value"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task EmptyInstanceId_IsRejected()
    {
        var provider = new FakeCloudProvider();
        var sut = new RestartInstanceTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":"   "}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task InvalidJson_IsRejected()
    {
        var provider = new FakeCloudProvider();
        var sut = new RestartInstanceTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsFailedToolResult()
    {
        var provider = new FakeCloudProvider
        {
            ExceptionToThrow =
                new InvalidOperationException("Cloud provider unavailable.")
        };

        var sut = new RestartInstanceTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":"i-12345"}""",
            CreateContext(Guid.NewGuid()));

        Assert.False(result.Success);
        Assert.Equal(
            "Cloud provider unavailable.",
            result.Error);
        Assert.True(provider.WasCalled);
    }
}