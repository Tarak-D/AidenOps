using System.Text.Json;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;

namespace AIOps.Tools;

public sealed class RunVpnDiagnosticsTool : ITool
{
    private readonly INetworkDiagnostics _networkDiagnostics;

    public RunVpnDiagnosticsTool(INetworkDiagnostics networkDiagnostics)
    {
        ArgumentNullException.ThrowIfNull(networkDiagnostics);

        _networkDiagnostics = networkDiagnostics;
    }

    public string Name => "Network.RunVpnDiagnostics";

    public string Description =>
        "Runs VPN diagnostics for a user or device. This is a read-only diagnostic operation.";

    public RiskLevel Risk => RiskLevel.Safe;

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        string userOrDeviceId;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);

            if (!document.RootElement.TryGetProperty(
                    "userOrDeviceId",
                    out var idElement))
            {
                return new ToolResult(
                    false,
                    "Required argument 'userOrDeviceId' was not provided.",
                    Error: "InvalidArguments");
            }

            userOrDeviceId = idElement.GetString() ?? string.Empty;
        }
        catch (JsonException)
        {
            return new ToolResult(
                false,
                "Tool arguments must be valid JSON.",
                Error: "InvalidArguments");
        }

        if (string.IsNullOrWhiteSpace(userOrDeviceId))
        {
            return new ToolResult(
                false,
                "Argument 'userOrDeviceId' must not be empty.",
                Error: "InvalidArguments");
        }

        try
        {
            var result = await _networkDiagnostics.RunVpnDiagnosticsAsync(
                userOrDeviceId,
                ct);

            return new ToolResult(
                true,
                $"VPN diagnostics completed successfully for '{userOrDeviceId}'.",
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
                $"VPN diagnostics failed for '{userOrDeviceId}'.",
                Error: ex.Message);
        }
    }
}