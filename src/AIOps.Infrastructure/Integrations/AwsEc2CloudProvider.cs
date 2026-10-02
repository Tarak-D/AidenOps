using System.Text.Json;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Diagnostics;
using AIOps.Abstractions.Integrations;
using Amazon.EC2;
using Amazon.EC2.Model;
using Amazon.Runtime;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Integrations;

/// <summary>
/// AWS EC2 implementation of the existing cloud integration seam.
/// Uses the AWS SDK default credential chain and never includes SDK exception
/// messages in errors returned to tool execution.
/// </summary>
public sealed class AwsEc2CloudProvider : ICloudProvider
{
    private readonly Lazy<IAmazonEC2> _ec2;
    private readonly TimeSpan _timeout;

    public AwsEc2CloudProvider(
        Lazy<IAmazonEC2> ec2,
        IOptions<CloudIntegrationOptions> options)
    {
        ArgumentNullException.ThrowIfNull(ec2);
        ArgumentNullException.ThrowIfNull(options);

        _ec2 = ec2;
        _timeout = TimeSpan.FromSeconds(options.Value.TimeoutSeconds);

        if (_timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Cloud provider timeout must be greater than zero.");
        }
    }

    public Task<string> GetInstanceStatusAsync(
        string instanceId,
        CancellationToken ct = default) =>
        AIOpsDiagnostics.TrackAsync(
            "cloud", "AwsEc2", "get_instance_status",
            () => GetInstanceStatusCoreAsync(instanceId, ct),
            ct);

    private async Task<string> GetInstanceStatusCoreAsync(
        string instanceId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var request = new DescribeInstancesRequest
        {
            InstanceIds = [instanceId]
        };

        var response = await ExecuteAsync(
            token => _ec2.Value.DescribeInstancesAsync(request, token),
            ct);

        var instance = response.Reservations?
            .SelectMany(reservation => reservation.Instances ?? [])
            .FirstOrDefault(candidate =>
                string.Equals(
                    candidate.InstanceId,
                    instanceId,
                    StringComparison.Ordinal));

        if (instance is null)
        {
            throw new AwsCloudProviderException(
                "AwsInstanceNotFound",
                "AWS EC2 did not return the requested instance.");
        }

        var state = instance.State?.Name?.Value;
        if (string.IsNullOrWhiteSpace(state))
        {
            throw new AwsCloudProviderException(
                "AwsInstanceStateUnavailable",
                "AWS EC2 did not return the requested instance state.");
        }

        return JsonSerializer.Serialize(new
        {
            instanceId,
            status = state,
            provider = "aws-ec2"
        });
    }

    public Task<string> RestartInstanceAsync(
        string instanceId,
        CancellationToken ct = default) =>
        AIOpsDiagnostics.TrackAsync(
            "cloud", "AwsEc2", "restart_instance",
            () => RestartInstanceCoreAsync(instanceId, ct),
            ct);

    private async Task<string> RestartInstanceCoreAsync(
        string instanceId,
        CancellationToken ct)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(instanceId);

        var request = new RebootInstancesRequest
        {
            InstanceIds = [instanceId]
        };

        await ExecuteAsync(
            async token =>
            {
                await _ec2.Value.RebootInstancesAsync(request, token);
                return true;
            },
            ct);

        // RebootInstances accepts a request; it does not confirm that the
        // instance has completed a reboot.
        return JsonSerializer.Serialize(new
        {
            instanceId,
            status = "reboot-request-accepted",
            provider = "aws-ec2"
        });
    }

    private async Task<T> ExecuteAsync<T>(
        Func<CancellationToken, Task<T>> operation,
        CancellationToken callerToken)
    {
        using var timeoutSource =
            CancellationTokenSource.CreateLinkedTokenSource(callerToken);
        timeoutSource.CancelAfter(_timeout);

        try
        {
            return await operation(timeoutSource.Token);
        }
        catch (OperationCanceledException) when (callerToken.IsCancellationRequested)
        {
            throw;
        }
        catch (OperationCanceledException ex)
        {
            throw new AwsCloudProviderException(
                "AwsTimeout",
                "The AWS EC2 request exceeded its configured timeout.",
                ex);
        }
        catch (AmazonServiceException ex)
        {
            throw TranslateServiceException(ex);
        }
        catch (AmazonClientException ex)
        {
            // Client exceptions can contain credential provider details.
            throw new AwsCloudProviderException(
                "AwsCredentialsOrConfigurationUnavailable",
                "The AWS SDK could not establish a usable EC2 client configuration.",
                ex);
        }
        catch (TimeoutException ex)
        {
            throw new AwsCloudProviderException(
                "AwsTimeout",
                "The AWS EC2 request exceeded its configured timeout.",
                ex);
        }
        catch (Exception ex)
        {
            // Do not allow SDK or transport details to flow into ToolResult.Error.
            throw new AwsCloudProviderException(
                "AwsOperationFailed",
                "The AWS EC2 operation failed.",
                ex);
        }
    }

    private static AwsCloudProviderException TranslateServiceException(
        AmazonServiceException exception)
    {
        if (string.Equals(
                exception.ErrorCode,
                "InvalidInstanceID.NotFound",
                StringComparison.OrdinalIgnoreCase))
        {
            return new AwsCloudProviderException(
                "AwsInstanceNotFound",
                "AWS EC2 could not find the requested instance.",
                exception);
        }

        if (exception.StatusCode == System.Net.HttpStatusCode.Forbidden ||
            string.Equals(exception.ErrorCode, "UnauthorizedOperation", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exception.ErrorCode, "AccessDenied", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(exception.ErrorCode, "AccessDeniedException", StringComparison.OrdinalIgnoreCase))
        {
            return new AwsCloudProviderException(
                "AwsAccessDenied",
                "AWS denied the requested EC2 operation.",
                exception);
        }

        if (exception.StatusCode == System.Net.HttpStatusCode.TooManyRequests ||
            exception.ErrorCode?.Contains("throttl", StringComparison.OrdinalIgnoreCase) == true ||
            string.Equals(exception.ErrorCode, "RequestLimitExceeded", StringComparison.OrdinalIgnoreCase))
        {
            return new AwsCloudProviderException(
                "AwsThrottled",
                "AWS throttled the EC2 request.",
                exception);
        }

        if (exception.StatusCode == System.Net.HttpStatusCode.RequestTimeout ||
            exception.StatusCode == System.Net.HttpStatusCode.GatewayTimeout ||
            exception.ErrorCode?.Contains("timeout", StringComparison.OrdinalIgnoreCase) == true)
        {
            return new AwsCloudProviderException(
                "AwsTimeout",
                "The AWS EC2 request exceeded its configured timeout.",
                exception);
        }

        if ((int)exception.StatusCode >= 500)
        {
            return new AwsCloudProviderException(
                "AwsServiceUnavailable",
                "The AWS EC2 service could not complete the request.",
                exception);
        }

        return new AwsCloudProviderException(
            "AwsOperationFailed",
            "The AWS EC2 operation failed.",
            exception);
    }
}

/// <summary>A sanitized provider failure suitable for tool and audit output.</summary>
public sealed class AwsCloudProviderException : Exception, IAIOpsSafeTelemetryFailure
{
    public AwsCloudProviderException(
        string code,
        string message,
        Exception? innerException = null)
        : base($"{code}: {message}", innerException)
    {
        Code = code;
    }

    public string Code { get; }

    public string TelemetryErrorType => Code;
}
