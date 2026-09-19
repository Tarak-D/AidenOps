using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using System;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AIOps.Tools.Tests;

public sealed class GetInstanceStatusToolTests
{
    private sealed class FakeCloudProvider : ICloudProvider
    {
        public string? LastInstanceId { get; private set; }

        public bool WasCalled { get; private set; }

        public string StatusToReturn { get; set; } =
            """{"status":"running"}""";

        public Exception? ExceptionToThrow { get; set; }

        public Task<string> RestartInstanceAsync(
            string instanceId,
            CancellationToken ct = default)
        {
            throw new NotImplementedException();
        }

        public Task<string> GetInstanceStatusAsync(
            string instanceId,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastInstanceId = instanceId;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(StatusToReturn);
        }
    }

    private static ToolExecutionContext CreateContext()
    {
        return new ToolExecutionContext(
            TicketId: Guid.NewGuid(),
            CorrelationId: Guid.NewGuid(),
            ActorId: "test-agent",
            ApprovalId: null);
    }

    [Fact]
    public async Task Metadata_IsSafeAndDoesNotRequireApproval()
    {
        var provider = new FakeCloudProvider();
        var sut = new GetInstanceStatusTool(provider);

        Assert.Equal(
            "Cloud.GetInstanceStatus",
            sut.Name);

        Assert.Equal(
            "Gets the current status of a cloud instance without modifying it.",
            sut.Description);

        Assert.Equal(
            RiskLevel.Safe,
            sut.Risk);

        Assert.False(sut.RequiresApproval);
    }

    [Fact]
    public async Task ValidArguments_CallsCloudProviderAndReturnsStatus()
    {
        var provider = new FakeCloudProvider
        {
            StatusToReturn = """{"status":"running","region":"test"}"""
        };

        var sut = new GetInstanceStatusTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":"i-12345"}""",
            CreateContext());

        Assert.True(result.Success);
        Assert.Equal(
            "Cloud instance 'i-12345' status retrieved successfully.",
            result.Summary);
        Assert.Equal(
            """{"status":"running","region":"test"}""",
            result.DetailsJson);

        Assert.True(provider.WasCalled);
        Assert.Equal(
            "i-12345",
            provider.LastInstanceId);
    }

    [Fact]
    public async Task MissingInstanceId_IsRejected()
    {
        var provider = new FakeCloudProvider();
        var sut = new GetInstanceStatusTool(provider);

        var result = await sut.ExecuteAsync(
            """{"other":"value"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.Contains(
            "instanceId",
            result.Summary);

        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task EmptyInstanceId_IsRejected()
    {
        var provider = new FakeCloudProvider();
        var sut = new GetInstanceStatusTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":"   "}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.Contains(
            "must not be empty",
            result.Summary);

        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task InvalidJson_IsRejected()
    {
        var provider = new FakeCloudProvider();
        var sut = new GetInstanceStatusTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.Contains(
            "valid JSON",
            result.Summary);

        Assert.False(provider.WasCalled);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsFailedToolResult()
    {
        var provider = new FakeCloudProvider
        {
            ExceptionToThrow = new InvalidOperationException(
                "Cloud provider unavailable.")
        };

        var sut = new GetInstanceStatusTool(provider);

        var result = await sut.ExecuteAsync(
            """{"instanceId":"i-12345"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal(
            "Cloud provider unavailable.",
            result.Error);
        Assert.Contains(
            "Failed to retrieve status",
            result.Summary);

        Assert.True(provider.WasCalled);
        Assert.Equal(
            "i-12345",
            provider.LastInstanceId);
    }
}