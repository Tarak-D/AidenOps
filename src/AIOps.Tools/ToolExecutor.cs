using AIOps.Abstractions;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using AIOps.Domain.Entities;

namespace AIOps.Tools;

/// <summary>
/// Executes proposed tools through the server-side safety boundary.
/// AI agents may propose actions; they never directly execute tools.
/// </summary>
public sealed class ToolExecutor
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IApprovalValidator _approvalValidator;
    private readonly IClock _clock;

    public ToolExecutor(
        IToolRegistry toolRegistry,
        IApprovalValidator approvalValidator,
        IClock clock)
    {
        _toolRegistry = toolRegistry;
        _approvalValidator = approvalValidator;
        _clock = clock;
    }

    public async Task<ToolResult> ExecuteAsync(
        ActionExecution actionExecution,
        CancellationToken cancellationToken = default)
    {
        var tool = _toolRegistry.Get(actionExecution.ToolName);

        if (tool is null)
        {
            return new ToolResult(
                false,
                $"Tool '{actionExecution.ToolName}' was not found.",
                Error: "UnknownTool");
        }

        // The registered tool is authoritative for risk.
        // Never allow a proposal to downgrade a tool's server-side risk.
        if (actionExecution.Risk != tool.Risk)
        {
            return new ToolResult(
                false,
                $"Risk mismatch for tool '{actionExecution.ToolName}': " +
                $"registered={tool.Risk}, proposed={actionExecution.Risk}.",
                Error: "RiskMismatch");
        }

        if (tool.Risk == RiskLevel.Safe)
        {
            var context = new ToolExecutionContext(
                TicketId: actionExecution.TicketId,
                CorrelationId: actionExecution.Id,
                ActorId: actionExecution.ProposedBy,
                ApprovalId: null);

            return await tool.ExecuteAsync(
                actionExecution.ArgumentsJson,
                context,
                cancellationToken);
        }

        // Moderate and Sensitive tools require a real server-side approval.
        var approval = await _approvalValidator
            .GetApprovalForActionExecutionAsync(
                actionExecution.Id,
                cancellationToken);

        if (approval is null)
        {
            return new ToolResult(
                false,
                $"No approval exists for action execution {actionExecution.Id}.",
                Error: "MissingApproval");
        }

        if (approval.ActionExecutionId != actionExecution.Id)
        {
            return new ToolResult(
                false,
                $"Approval {approval.Id} is not bound to action execution " +
                $"{actionExecution.Id}.",
                Error: "ApprovalMismatchActionExecution");
        }

        if (approval.TicketId != actionExecution.TicketId)
        {
            return new ToolResult(
                false,
                $"Approval {approval.Id} is for ticket {approval.TicketId}, " +
                $"not {actionExecution.TicketId}.",
                Error: "ApprovalMismatchTicketId");
        }

        if (approval.Status != ApprovalStatus.Approved)
        {
            return new ToolResult(
                false,
                $"Approval {approval.Id} is not approved " +
                $"(status={approval.Status}).",
                Error: "ApprovalNotApproved");
        }

        if (approval.ExpiresAt <= _clock.UtcNow)
        {
            return new ToolResult(
                false,
                $"Approval {approval.Id} expired at {approval.ExpiresAt}.",
                Error: "ApprovalExpired");
        }

        var approvedContext = new ToolExecutionContext(
            TicketId: actionExecution.TicketId,
            CorrelationId: actionExecution.Id,
            ActorId: actionExecution.ProposedBy,
            ApprovalId: approval.Id);

        return await tool.ExecuteAsync(
            actionExecution.ArgumentsJson,
            approvedContext,
            cancellationToken);
    }
}
