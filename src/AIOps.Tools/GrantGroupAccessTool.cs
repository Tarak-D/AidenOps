using System.Text.Json;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;

namespace AIOps.Tools;

/// <summary>
/// Grants a directory user access to a group.
/// This is a sensitive authorization operation and requires approval.
/// </summary>
public sealed class GrantGroupAccessTool : ITool
{
    private readonly IDirectoryService _directoryService;

    public GrantGroupAccessTool(IDirectoryService directoryService)
    {
        ArgumentNullException.ThrowIfNull(directoryService);

        _directoryService = directoryService;
    }

    public string Name => "Directory.GrantGroupAccess";

    public string Description =>
        "Grants a directory user access to a group. This is a sensitive authorization operation.";

    public RiskLevel Risk => RiskLevel.Sensitive;

    public bool RequiresApproval => true;

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        string userPrincipalName;
        string groupId;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            var root = document.RootElement;

            if (!root.TryGetProperty(
                    "userPrincipalName",
                    out var userElement))
            {
                return new ToolResult(
                    false,
                    "Required argument 'userPrincipalName' was not provided.",
                    Error: "InvalidArguments");
            }

            if (!root.TryGetProperty(
                    "groupId",
                    out var groupElement))
            {
                return new ToolResult(
                    false,
                    "Required argument 'groupId' was not provided.",
                    Error: "InvalidArguments");
            }

            userPrincipalName = userElement.GetString() ?? string.Empty;
            groupId = groupElement.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return new ToolResult(
                false,
                "Tool arguments must be valid JSON.",
                Error: "InvalidArguments");
        }

        if (string.IsNullOrWhiteSpace(userPrincipalName))
        {
            return new ToolResult(
                false,
                "Argument 'userPrincipalName' must not be empty.",
                Error: "InvalidArguments");
        }

        if (string.IsNullOrWhiteSpace(groupId))
        {
            return new ToolResult(
                false,
                "Argument 'groupId' must not be empty.",
                Error: "InvalidArguments");
        }

        if (ctx.ApprovalId is null)
        {
            return new ToolResult(
                false,
                "Granting group access requires an approved action.",
                Error: "ApprovalRequired");
        }

        try
        {
            var result = await _directoryService.GrantGroupAccessAsync(
                userPrincipalName,
                groupId,
                ct);

            return new ToolResult(
                true,
                $"Group access granted successfully for '{userPrincipalName}'.",
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
                $"Failed to grant group access for '{userPrincipalName}'.",
                Error: ex.Message);
        }
    }
}