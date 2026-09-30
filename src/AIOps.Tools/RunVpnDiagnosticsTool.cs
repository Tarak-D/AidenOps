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
        "Runs VPN diagnostics for a user, device, or IP address. This is a read-only diagnostic operation.";

    public RiskLevel Risk => RiskLevel.Safe;

    public string InputSchemaJson => """
        {"oneOf":[{"type":"object","properties":{"target":{"type":"string","minLength":1},"targetType":{"type":"string","enum":["User","Device","Ip"]}},"required":["target","targetType"],"additionalProperties":false},{"type":"object","properties":{"userOrDeviceId":{"type":"string","minLength":1}},"required":["userOrDeviceId"],"additionalProperties":false}]}
        """;

    public bool RequiresApproval => false;

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        VpnDiagnosticRequest request;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);

            if (document.RootElement.TryGetProperty("target", out var targetElement) &&
                document.RootElement.TryGetProperty("targetType", out var targetTypeElement) &&
                targetElement.ValueKind == JsonValueKind.String &&
                targetTypeElement.ValueKind == JsonValueKind.String)
            {
                if (!Enum.TryParse<NetworkDiagnosticTargetType>(targetTypeElement.GetString(), true, out var targetType))
                {
                    return new ToolResult(false, "Argument 'targetType' must be User, Device, or Ip.", Error: "InvalidArguments");
                }

                request = new VpnDiagnosticRequest(targetElement.GetString()!, targetType);
            }
            else if (document.RootElement.TryGetProperty("userOrDeviceId", out var legacyTarget) &&
                     legacyTarget.ValueKind == JsonValueKind.String)
            {
                // Existing Python Gateway proposals pass the ticket reporter email through this legacy field.
                request = new VpnDiagnosticRequest(legacyTarget.GetString()!, NetworkDiagnosticTargetType.User);
            }
            else
            {
                return new ToolResult(
                    false,
                    "Required arguments 'target' and 'targetType' were not provided.",
                    Error: "InvalidArguments");
            }
        }
        catch (JsonException)
        {
            return new ToolResult(
                false,
                "Tool arguments must be valid JSON.",
                Error: "InvalidArguments");
        }

        catch (ArgumentException)
        {
            return new ToolResult(
                false,
                "Arguments 'target' and 'targetType' are invalid.",
                Error: "InvalidArguments");
        }

        try
        {
            var result = await _networkDiagnostics.RunVpnDiagnosticsAsync(
                request,
                ct);

            return new ToolResult(
                true,
                $"VPN diagnostics completed successfully for '{request.Target}'.",
                DetailsJson: JsonSerializer.Serialize(result));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new ToolResult(
                false,
                $"VPN diagnostics failed for '{request.Target}'.",
                Error: ex.Message);
        }
    }
}
