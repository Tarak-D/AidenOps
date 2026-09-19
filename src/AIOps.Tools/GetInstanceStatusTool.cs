using System.Text.Json;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;

namespace AIOps.Tools;

/// <summary>
/// Read-only cloud instance status lookup.
/// This tool is safe because it does not modify infrastructure.
/// </summary>
public sealed class GetInstanceStatusTool : ITool
{
    private readonly ICloudProvider _cloudProvider;

    public GetInstanceStatusTool(ICloudProvider cloudProvider)
    {
        ArgumentNullException.ThrowIfNull(cloudProvider);

        _cloudProvider = cloudProvider;
    }

    public string Name => "Cloud.GetInstanceStatus";

    public string Description =>
        "Gets the current status of a cloud instance without modifying it.";

    public RiskLevel Risk => RiskLevel.Safe;

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        string instanceId;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);

            if (!document.RootElement.TryGetProperty(
                    "instanceId",
                    out var instanceIdElement))
            {
                return new ToolResult(
                    false,
                    "Required argument 'instanceId' was not provided.",
                    Error: "InvalidArguments");
            }

            instanceId = instanceIdElement.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return new ToolResult(
                false,
                "Tool arguments must be valid JSON.",
                Error: "InvalidArguments");
        }

        if (string.IsNullOrWhiteSpace(instanceId))
        {
            return new ToolResult(
                false,
                "Argument 'instanceId' must not be empty.",
                Error: "InvalidArguments");
        }

        try
        {
            var status = await _cloudProvider.GetInstanceStatusAsync(
                instanceId,
                ct);

            return new ToolResult(
                true,
                $"Cloud instance '{instanceId}' status retrieved successfully.",
                DetailsJson: status);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolResult(
                false,
                $"Failed to retrieve status for cloud instance '{instanceId}'.",
                Error: ex.Message);
        }
    }
}