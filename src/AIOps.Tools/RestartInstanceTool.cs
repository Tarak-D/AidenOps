using System.Text.Json;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;

namespace AIOps.Tools;

/// <summary>
/// Restarts a cloud instance.
/// This is a moderate-risk mutating operation and therefore requires approval.
/// </summary>
public sealed class RestartInstanceTool : ITool
{
    private readonly ICloudProvider _cloudProvider;

    public RestartInstanceTool(ICloudProvider cloudProvider)
    {
        ArgumentNullException.ThrowIfNull(cloudProvider);

        _cloudProvider = cloudProvider;
    }

    public string Name => "Cloud.RestartInstance";

    public string Description =>
        "Restarts a cloud instance. This operation modifies infrastructure.";

    public RiskLevel Risk => RiskLevel.Moderate;

    public bool RequiresApproval => true;

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

        if (ctx.ApprovalId is null)
        {
            return new ToolResult(
                false,
                "Cloud instance restart requires an approved action.",
                Error: "ApprovalRequired");
        }

        try
        {
            var result = await _cloudProvider.RestartInstanceAsync(
                instanceId,
                ct);

            return new ToolResult(
                true,
                $"Cloud instance '{instanceId}' restart requested successfully.",
                DetailsJson: result);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolResult(
                false,
                $"Failed to restart cloud instance '{instanceId}'.",
                Error: ex.Message);
        }
    }
}