using System.Text.Json;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;

namespace AIOps.Tools;

/// <summary>
/// Resets a directory user's password.
/// This is a sensitive operation and requires approval.
/// </summary>
public sealed class ResetPasswordTool : ITool
{
    private readonly IDirectoryService _directoryService;

    public ResetPasswordTool(IDirectoryService directoryService)
    {
        ArgumentNullException.ThrowIfNull(directoryService);

        _directoryService = directoryService;
    }

    public string Name => "Directory.ResetPassword";

    public string Description =>
        "Resets a directory user's password. This is a sensitive identity operation.";

    public RiskLevel Risk => RiskLevel.Sensitive;

    public bool RequiresApproval => true;

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        string userPrincipalName;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);

            if (!document.RootElement.TryGetProperty(
                    "userPrincipalName",
                    out var userElement))
            {
                return new ToolResult(
                    false,
                    "Required argument 'userPrincipalName' was not provided.",
                    Error: "InvalidArguments");
            }

            userPrincipalName = userElement.GetString() ?? string.Empty;
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

        if (ctx.ApprovalId is null)
        {
            return new ToolResult(
                false,
                "Password reset requires an approved action.",
                Error: "ApprovalRequired");
        }

        try
        {
            var result = await _directoryService.ResetPasswordAsync(
                userPrincipalName,
                ct);

            return new ToolResult(
                true,
                $"Password reset requested successfully for '{userPrincipalName}'.",
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
                $"Failed to reset the password for '{userPrincipalName}'.",
                Error: ex.Message);
        }
    }
}