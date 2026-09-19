using AIOps.Infrastructure.Integrations;
using Xunit;

namespace AIOps.Infrastructure.Tests;

public sealed class SimulatedCloudProviderTests
{
    [Fact]
    public async Task GetInstanceStatusAsync_ReturnsRunningStatus()
    {
        var provider = new SimulatedCloudProvider();

        var result = await provider.GetInstanceStatusAsync("i-12345");

        Assert.Contains("\"instanceId\":\"i-12345\"", result);
        Assert.Contains("\"status\":\"running\"", result);
        Assert.Contains("\"provider\":\"simulated\"", result);
    }

    [Fact]
    public async Task RestartInstanceAsync_ReturnsRestartedStatus()
    {
        var provider = new SimulatedCloudProvider();

        var result = await provider.RestartInstanceAsync("i-12345");

        Assert.Contains("\"instanceId\":\"i-12345\"", result);
        Assert.Contains("\"status\":\"restarted\"", result);
        Assert.Contains("\"provider\":\"simulated\"", result);
    }

    [Fact]
    public async Task GetInstanceStatusAsync_RejectsEmptyInstanceId()
    {
        var provider = new SimulatedCloudProvider();

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.GetInstanceStatusAsync(""));
    }

    [Fact]
    public async Task RestartInstanceAsync_RejectsEmptyInstanceId()
    {
        var provider = new SimulatedCloudProvider();

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.RestartInstanceAsync(""));
    }
}