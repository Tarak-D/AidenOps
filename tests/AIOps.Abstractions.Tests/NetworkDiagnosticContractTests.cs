using AIOps.Abstractions.Integrations;

namespace AIOps.Abstractions.Tests;

public sealed class NetworkDiagnosticContractTests
{
    [Theory]
    [InlineData(NetworkDiagnosticTargetType.User)]
    [InlineData(NetworkDiagnosticTargetType.Device)]
    [InlineData(NetworkDiagnosticTargetType.Ip)]
    public void VpnRequestAcceptsSupportedExplicitTargets(NetworkDiagnosticTargetType targetType)
    {
        var request = new VpnDiagnosticRequest("target-value", targetType);

        Assert.Equal("target-value", request.Target);
        Assert.Equal(targetType, request.TargetType);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void VpnRequestRejectsBlankTarget(string? target) =>
        Assert.ThrowsAny<ArgumentException>(() => new VpnDiagnosticRequest(target!, NetworkDiagnosticTargetType.User));

    [Fact]
    public void VpnRequestRejectsUnsupportedTargetType()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new VpnDiagnosticRequest("target-value", NetworkDiagnosticTargetType.Host));
    }

    [Fact]
    public void ProviderDetailsRejectCredentialBearingFieldNames()
    {
        Assert.Throws<ArgumentException>(() =>
            new NetworkDiagnosticProviderDetails(new Dictionary<string, string?>
            {
                ["accessToken"] = "secret-value"
            }));
    }

    [Fact]
    public void ProviderDetailsRejectUnnormalizedKeysAndAuthorizationValues()
    {
        Assert.Throws<ArgumentException>(() =>
            new NetworkDiagnosticProviderDetails(new Dictionary<string, string?> { ["rawResponse"] = "value" }));
        Assert.Throws<ArgumentException>(() =>
            new NetworkDiagnosticProviderDetails(new Dictionary<string, string?> { ["sessionId"] = "Bearer sensitive-value" }));
    }

    [Fact]
    public void ProviderDetailsAcceptAllowListedNamespacedNormalizedFields()
    {
        var details = new NetworkDiagnosticProviderDetails(new Dictionary<string, string?>
        {
            ["vendor.sessionId"] = "session-123",
            ["vendor.region"] = "us-east"
        });

        Assert.Equal("session-123", details.Fields["vendor.sessionId"]);
        Assert.Equal("us-east", details.Fields["vendor.region"]);
    }

    [Fact]
    public void GeneralRequestAcceptsExplicitMonitorTarget()
    {
        var request = new GeneralNetworkDiagnosticRequest("checkout-synthetic", NetworkDiagnosticTargetType.Monitor);

        Assert.Equal("checkout-synthetic", request.Target);
        Assert.Equal(NetworkDiagnosticTargetType.Monitor, request.TargetType);
    }

    [Theory]
    [InlineData(NetworkDiagnosticObservationType.ActiveTest, "execution-123")]
    [InlineData(NetworkDiagnosticObservationType.Monitoring, null)]
    [InlineData(NetworkDiagnosticObservationType.Session, null)]
    public void GeneralResultRepresentsActiveAndObservedResults(
        NetworkDiagnosticObservationType observationType,
        string? executionId)
    {
        var result = new GeneralNetworkDiagnosticResult(
            "Success", "ServerSelectedProvider", observationType, DateTimeOffset.UnixEpoch,
            "target-value", "Available", null, null, executionId, null);

        Assert.Equal(observationType, result.ObservationType);
        Assert.Equal(executionId, result.ExecutionId);
        Assert.Null(result.Latency);
        Assert.Null(result.PacketLoss);
        Assert.Equal("ServerSelectedProvider", result.Provider);
    }

    [Fact]
    public void VpnResultSupportsSessionAndNullMeasurements()
    {
        var result = new VpnDiagnosticResult(
            "Connected", "ServerSelectedProvider", NetworkDiagnosticObservationType.Session,
            DateTimeOffset.UnixEpoch, "user@example.com", null, null, "Connected", null, null, null);

        Assert.Equal(NetworkDiagnosticObservationType.Session, result.ObservationType);
        Assert.Null(result.Latency);
        Assert.Null(result.PacketLoss);
        Assert.Equal("ServerSelectedProvider", result.Provider);
    }
}
