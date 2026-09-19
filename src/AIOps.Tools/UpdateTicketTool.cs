using System.Text.Json;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Tools;
using AIOps.Domain;

namespace AIOps.Tools;

/// <summary>
/// Updates an ITSM ticket.
/// This is a moderate-risk mutating operation and requires approval.
/// </summary>
public sealed class UpdateTicketTool : ITool
{
    private readonly IItsmConnector _itsmConnector;

    public UpdateTicketTool(IItsmConnector itsmConnector)
    {
        ArgumentNullException.ThrowIfNull(itsmConnector);

        _itsmConnector = itsmConnector;
    }

    public string Name => "ITSM.UpdateTicket";

    public string Description =>
        "Updates an ITSM ticket with a note and optional state.";

    public RiskLevel Risk => RiskLevel.Moderate;

    public bool RequiresApproval => true;

    public async Task<ToolResult> ExecuteAsync(
        string argumentsJson,
        ToolExecutionContext ctx,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        string externalRef;
        string note;
        string? state;

        try
        {
            using var document = JsonDocument.Parse(argumentsJson);
            var root = document.RootElement;

            if (!root.TryGetProperty("externalRef", out var externalRefElement))
            {
                return new ToolResult(
                    false,
                    "Required argument 'externalRef' was not provided.",
                    Error: "InvalidArguments");
            }

            if (!root.TryGetProperty("note", out var noteElement))
            {
                return new ToolResult(
                    false,
                    "Required argument 'note' was not provided.",
                    Error: "InvalidArguments");
            }

            externalRef = externalRefElement.GetString() ?? string.Empty;
            note = noteElement.GetString() ?? string.Empty;

            state = root.TryGetProperty("state", out var stateElement)
                ? stateElement.GetString()
                : null;
        }
        catch (JsonException)
        {
            return new ToolResult(
                false,
                "Tool arguments must be valid JSON.",
                Error: "InvalidArguments");
        }

        if (string.IsNullOrWhiteSpace(externalRef))
        {
            return new ToolResult(
                false,
                "Argument 'externalRef' must not be empty.",
                Error: "InvalidArguments");
        }

        if (string.IsNullOrWhiteSpace(note))
        {
            return new ToolResult(
                false,
                "Argument 'note' must not be empty.",
                Error: "InvalidArguments");
        }

        if (ctx.ApprovalId is null)
        {
            return new ToolResult(
                false,
                "ITSM ticket update requires an approved action.",
                Error: "ApprovalRequired");
        }

        try
        {
            var result = await _itsmConnector.UpdateTicketAsync(
                externalRef,
                note,
                state,
                ct);

            return new ToolResult(
                true,
                $"ITSM ticket '{externalRef}' updated successfully.",
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
                $"Failed to update ITSM ticket '{externalRef}'.",
                Error: ex.Message);
        }
    }
}