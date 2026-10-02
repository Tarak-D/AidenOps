using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace AIOps.Abstractions.Diagnostics;

/// <summary>Implemented by integration exceptions that expose a stable, safe category.</summary>
public interface IAIOpsSafeTelemetryFailure
{
    string TelemetryErrorType { get; }
}

/// <summary>
/// Local .NET diagnostics for existing AidenOps execution boundaries.
/// Tags are restricted to bounded identifiers; payloads, exception messages,
/// request URLs, and credentials are never recorded.
/// </summary>
public static class AIOpsDiagnostics
{
    public const string InstrumentationName = "AIOps.AgentSwarm";

    public static readonly ActivitySource ActivitySource = new(InstrumentationName);
    public static readonly Meter Meter = new(InstrumentationName);

    private static readonly Counter<long> OperationCounter = Meter.CreateCounter<long>(
        "aiops.operations",
        unit: "{operation}",
        description: "Count of completed AidenOps operations.");

    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(
        "aiops.operation.duration",
        unit: "ms",
        description: "Duration of completed AidenOps operations.");

    public static async Task<T> TrackAsync<T>(
        string category,
        string provider,
        string operation,
        Func<Task<T>> action,
        CancellationToken cancellationToken = default,
        Guid? correlationId = null,
        Func<T, string?>? failureType = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        using var activity = ActivitySource.StartActivity(
            $"{SafeIdentifier(category)}.{SafeIdentifier(operation)}",
            ActivityKind.Internal);
        var safeCategory = SafeIdentifier(category);
        var safeProvider = SafeIdentifier(provider);
        var safeOperation = SafeIdentifier(operation);
        activity?.SetTag("aiops.category", safeCategory);
        activity?.SetTag("aiops.provider", safeProvider);
        activity?.SetTag("aiops.operation", safeOperation);
        if (correlationId.HasValue && correlationId.Value != Guid.Empty)
        {
            activity?.SetTag("aiops.correlation_id", correlationId.Value.ToString("N"));
        }

        var started = Stopwatch.GetTimestamp();
        try
        {
            var result = await action().ConfigureAwait(false);
            var errorType = failureType?.Invoke(result);
            Record(
                safeCategory,
                safeProvider,
                safeOperation,
                errorType is null ? "success" : "failure",
                errorType,
                activity,
                started);
            return result;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            Record(safeCategory, safeProvider, safeOperation, "cancelled", "cancelled", activity, started);
            throw;
        }
        catch (OperationCanceledException)
        {
            Record(safeCategory, safeProvider, safeOperation, "failure", "timeout", activity, started);
            throw;
        }
        catch (Exception exception)
        {
            var errorType = exception is IAIOpsSafeTelemetryFailure safeFailure
                ? safeFailure.TelemetryErrorType
                : exception.GetType().Name;
            Record(safeCategory, safeProvider, safeOperation, "failure", errorType, activity, started);
            throw;
        }
    }

    public static async Task TrackAsync(
        string category,
        string provider,
        string operation,
        Func<Task> action,
        CancellationToken cancellationToken = default,
        Guid? correlationId = null)
    {
        ArgumentNullException.ThrowIfNull(action);
        _ = await TrackAsync(
            category,
            provider,
            operation,
            async () =>
            {
                await action().ConfigureAwait(false);
                return true;
            },
            cancellationToken,
            correlationId).ConfigureAwait(false);
    }

    private static void Record(
        string category,
        string provider,
        string operation,
        string outcome,
        string? errorType,
        Activity? activity,
        long started)
    {
        var safeOutcome = SafeIdentifier(outcome);
        var safeErrorType = errorType is null ? null : SafeIdentifier(errorType);
        activity?.SetTag("aiops.outcome", safeOutcome);
        if (safeErrorType is not null)
        {
            activity?.SetTag("error.type", safeErrorType);
            activity?.SetStatus(ActivityStatusCode.Error);
        }

        var tags = new TagList
        {
            { "aiops.category", category },
            { "aiops.provider", provider },
            { "aiops.operation", operation },
            { "aiops.outcome", safeOutcome }
        };
        if (safeErrorType is not null)
        {
            tags.Add("error.type", safeErrorType);
        }

        OperationCounter.Add(1, tags);
        OperationDuration.Record(Stopwatch.GetElapsedTime(started).TotalMilliseconds, tags);
    }

    private static string SafeIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > 96 ||
            value.Any(character =>
                !(character is >= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or ':' or '-')))
        {
            return "unclassified";
        }

        return value;
    }
}
