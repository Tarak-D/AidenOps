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
        public VpnDiagnosticRequest? LastRequest { get; private set; }

        public VpnDiagnosticResult ResultToReturn { get; set; } = new(
            "Healthy", "Simulated", NetworkDiagnosticObservationType.Session, DateTimeOffset.UnixEpoch,
            null, null, null, "Connected", null, null, null);

        public Exception? ExceptionToThrow { get; set; }

        public Task<VpnDiagnosticResult> RunVpnDiagnosticsAsync(
            VpnDiagnosticRequest request,
            CancellationToken ct = default)
        {
            WasCalled = true;
            LastRequest = request;

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
            """{"target":"device-123","targetType":"Device"}""",
            CreateContext());

        Assert.True(result.Success);
        Assert.Contains("completed successfully", result.Summary);
        Assert.True(diagnostics.WasCalled);
        Assert.Equal(new VpnDiagnosticRequest("device-123", NetworkDiagnosticTargetType.Device), diagnostics.LastRequest);
    }

    [Theory]
    [InlineData("User", NetworkDiagnosticTargetType.User)]
    [InlineData("Device", NetworkDiagnosticTargetType.Device)]
    [InlineData("Ip", NetworkDiagnosticTargetType.Ip)]
    public async Task ExecutesWithExplicitTargetType(string targetTypeName, NetworkDiagnosticTargetType expectedType)
    {
        var diagnostics = new FakeNetworkDiagnostics();
        var sut = new RunVpnDiagnosticsTool(diagnostics);

        var result = await sut.ExecuteAsync(
            $$"""{"target":"target-value","targetType":"{{targetTypeName}}"}""",
            CreateContext());

        Assert.True(result.Success);
        Assert.Equal("target-value", diagnostics.LastRequest?.Target);
        Assert.Equal(expectedType, diagnostics.LastRequest?.TargetType);
    }

    [Fact]
    public async Task LegacyPythonProposalIsMappedToTypedUserRequest()
    {
        var diagnostics = new FakeNetworkDiagnostics();
        var sut = new RunVpnDiagnosticsTool(diagnostics);

        var result = await sut.ExecuteAsync(
            """{"userOrDeviceId":"reporter@example.com"}""",
            CreateContext());

        Assert.True(result.Success);
        Assert.Equal("reporter@example.com", diagnostics.LastRequest?.Target);
        Assert.Equal(NetworkDiagnosticTargetType.User, diagnostics.LastRequest?.TargetType);
    }

    [Fact]
    public async Task InvalidTargetType_IsRejected()
    {
        var diagnostics = new FakeNetworkDiagnostics();
        var sut = new RunVpnDiagnosticsTool(diagnostics);

        var result = await sut.ExecuteAsync(
            """{"target":"value","targetType":"Host"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("InvalidArguments", result.Error);
        Assert.False(diagnostics.WasCalled);
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
            """{"target":"","targetType":"Device"}""",
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
            """{"target":"device-123","targetType":"Device"}""",
            CreateContext());

        Assert.False(result.Success);
        Assert.Equal("Diagnostics unavailable.", result.Error);
        Assert.True(diagnostics.WasCalled);
    }
}
