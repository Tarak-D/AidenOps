using System.Diagnostics;
using AIOps.Abstractions.Diagnostics;

namespace AIOps.Abstractions.Tests;

public sealed class AIOpsDiagnosticsTests : IDisposable
{
    private readonly ActivityListener _listener;
    private readonly List<Activity> _activities = [];

    public AIOpsDiagnosticsTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source =>
                source.Name == AIOpsDiagnostics.InstrumentationName,

            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,

            ActivityStarted = activity =>
                _activities.Add(activity)
        };

        ActivitySource.AddActivityListener(_listener);
    }

    [Fact]
    public async Task TrackAsync_Success_RecordsSuccessfulActivity()
    {
        var correlationId = Guid.NewGuid();

        var result = await AIOpsDiagnostics.TrackAsync(
            "cloud",
            "AwsEc2",
            "get_instance_status",
            () => Task.FromResult("running"),
            correlationId: correlationId);

        Assert.Equal("running", result);

        var activity = Assert.Single(_activities);

        Assert.Equal(
            "cloud.get_instance_status",
            activity.OperationName);

        Assert.Equal(
            "cloud",
            activity.GetTagItem("aiops.category"));

        Assert.Equal(
            "AwsEc2",
            activity.GetTagItem("aiops.provider"));

        Assert.Equal(
            "get_instance_status",
            activity.GetTagItem("aiops.operation"));

        Assert.Equal(
            correlationId.ToString("N"),
            activity.GetTagItem("aiops.correlation_id"));

        Assert.Equal(
            "success",
            activity.GetTagItem("aiops.outcome"));
    }

    [Fact]
    public async Task TrackAsync_Failure_RecordsSafeErrorType()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AIOpsDiagnostics.TrackAsync(
                "cloud",
                "AwsEc2",
                "restart_instance",
                () => throw new InvalidOperationException(
                    "secret-credential-details")));

        var activity = Assert.Single(_activities);

        Assert.Equal(
            "failure",
            activity.GetTagItem("aiops.outcome"));

        Assert.Equal(
            nameof(InvalidOperationException),
            activity.GetTagItem("error.type"));

        Assert.DoesNotContain(
            "secret-credential-details",
            activity.Tags.Select(tag => tag.Value?.ToString()));
    }

    [Fact]
    public async Task TrackAsync_OperationCanceledByCaller_RecordsCancelled()
    {
        using var cancellationTokenSource = new CancellationTokenSource();
        cancellationTokenSource.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AIOpsDiagnostics.TrackAsync(
                "network",
                "CiscoThousandEyes",
                "run_diagnostics",
                async () =>
                {
                    await Task.Delay(
                        TimeSpan.FromSeconds(1),
                        cancellationTokenSource.Token);

                    return true;
                },
                cancellationTokenSource.Token));

        var activity = Assert.Single(_activities);

        Assert.Equal(
            "cancelled",
            activity.GetTagItem("aiops.outcome"));

        Assert.Equal(
            "cancelled",
            activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task TrackAsync_OperationCanceledWithoutRequestedToken_RecordsTimeout()
    {
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => AIOpsDiagnostics.TrackAsync(
                "network",
                "CiscoThousandEyes",
                "run_diagnostics",
                () => Task.FromCanceled<bool>(
                    new CancellationToken(canceled: true))));

        var activity = Assert.Single(_activities);

        Assert.Equal(
            "failure",
            activity.GetTagItem("aiops.outcome"));

        Assert.Equal(
            "timeout",
            activity.GetTagItem("error.type"));
    }

    [Fact]
    public async Task TrackAsync_UnsafeIdentifiers_AreSanitized()
    {
        await AIOpsDiagnostics.TrackAsync(
            "cloud/provider?secret=123",
            "Aws Ec2/ApiKey",
            "restart instance?token=abc",
            () => Task.CompletedTask);

        var activity = Assert.Single(_activities);

        Assert.Equal(
            "unclassified",
            activity.GetTagItem("aiops.category"));

        Assert.Equal(
            "unclassified",
            activity.GetTagItem("aiops.provider"));

        Assert.Equal(
            "unclassified",
            activity.GetTagItem("aiops.operation"));
    }

    [Fact]
    public async Task TrackAsync_LongIdentifier_IsSanitized()
    {
        var longProviderName = new string('a', 97);

        await AIOpsDiagnostics.TrackAsync(
            "cloud",
            longProviderName,
            "operation",
            () => Task.CompletedTask);

        var activity = Assert.Single(_activities);

        Assert.Equal(
            "unclassified",
            activity.GetTagItem("aiops.provider"));
    }

    [Fact]
    public async Task TrackAsync_CredentialLikeValues_AreNotRecorded()
    {
        const string secret = "super-secret-api-token";

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => AIOpsDiagnostics.TrackAsync(
                "network",
                "CiscoThousandEyes",
                "authenticate",
                () => throw new InvalidOperationException(secret)));

        var activity = Assert.Single(_activities);

        foreach (var tag in activity.Tags)
        {
            Assert.NotEqual(
                secret,
                tag.Value?.ToString());
        }

        Assert.DoesNotContain(
            secret,
            activity.DisplayName,
            StringComparison.Ordinal);

        Assert.DoesNotContain(
            secret,
            activity.OperationName,
            StringComparison.Ordinal);
    }

    public void Dispose()
    {
        _listener.Dispose();

        foreach (var activity in _activities)
        {
            activity.Dispose();
        }
    }
}