using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using Xunit;

namespace AIOps.Tools.Tests;

public sealed class RunVpnDiagnosticsToolTests
{
    private sealed class FakeNetworkDiagnostics : INetworkDiagnostics
    {
        public bool WasCalled { get; private set; }
        public string? LastUserOrDeviceId { get; private set; }

        public string ResultToReturn { get; set; } =
            """{"vpn":"healthy"}""";

        public Exception? ExceptionToThrow { get; set; }

        public Task<string> RunVpnDiagnosticsAsync(
            string userOrDeviceId,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastUserOrDeviceId = userOrDeviceId;

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(ResultToReturn);
        }
    }

    private static ToolExecutionContext CreateContext()
    {
        return new ToolExecutionContext(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "test-agent",
            null);
    }

    [Fact]
    public void Metadata_IsSafeAndDoesNotRequireApproval()
    {
        var diagnostics = new FakeNetworkDiagnostics();
        var sut = new RunVpnDiagnosticsTool(diagnostics);

        Assert.Equal("Network.RunVpnDiagnostics", sut.Name);
        Assert.Equal(RiskLevel.Safe, sut.Risk);
        Assert.False(sut.RequiresApproval);
    }

    [Fact]
    public async Task ExecutesWithoutApproval()
    {
        var diagnostics = new FakeNetworkDiagnostics();
        var sut = new RunVpnDiagnosticsTool(diagnostics);

        var result = await sut.ExecuteAsync(
            """{"userOrDeviceId":"device-123"}""",
            CreateContext());

        Assert.True(result.Success);
        Assert.Contains("completed successfully", result.Summary);
        Assert.True(diagnostics.WasCalled);
        Assert.Equal("device-123", diagnostics.LastUserOrDeviceId);
    }

    [Fact]
    public async Task MissingArgument_IsRejected()
    {
        var diagnostics = new FakeNetworkDiagnostics();
        var sut = new RunVpnDiagnosticsTool(diagnostics);

        var result = await sut.ExecuteAsync(
            """{"other":"value"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(diagnostics.WasCalled);
    }

    [Fact]
    public async Task InvalidJson_IsRejected()
    {
        var diagnostics = new FakeNetworkDiagnostics();
        var sut = new RunVpnDiagnosticsTool(diagnostics);

        var result = await sut.ExecuteAsync(
            """{"userOrDeviceId":""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(diagnostics.WasCalled);
    }

    [Fact]
    public async Task ProviderFailure_ReturnsFailedToolResult()
    {
        var diagnostics = new FakeNetworkDiagnostics
        {
            ExceptionToThrow =
                new InvalidOperationException("Diagnostics unavailable.")
        };

        var sut = new RunVpnDiagnosticsTool(diagnostics);

        var result = await sut.ExecuteAsync(
            """{"userOrDeviceId":"device-123"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("Diagnostics unavailable.", result.Error);
        Assert.True(diagnostics.WasCalled);
    }
}