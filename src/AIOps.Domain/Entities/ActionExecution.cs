namespace AIOps.Domain.Entities;

/// <summary>
/// A proposed/executed tool action on a ticket. AI agents can only *propose* these;
/// execution requires passing the guardrailed ToolExecutor in the .NET control plane.
/// </summary>
public sealed class ActionExecution
{
    public Guid Id { get; private set; }
    public Guid TicketId { get; private set; }
    public string ToolName { get; private set; } = default!;
    public string ArgumentsJson { get; private set; } = "{}";
    public RiskLevel Risk { get; private set; }
    public bool RequiresApproval { get; private set; }
    public string ProposedBy { get; private set; } = default!;
    public string? Reason { get; private set; }
    public ActionStatus Status { get; private set; } = ActionStatus.Proposed;
    public string? ResultSummary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }

    private ActionExecution() { }

#pragma warning disable CS8618
    public static ActionExecution Propose(
        Guid ticketId,
        string toolName,
        string argumentsJson,
        RiskLevel risk,
        string proposedBy,
        string? reason,
        DateTimeOffset now)
    {
        if (ticketId == Guid.Empty)
            throw new DomainInvariantViolationException("Action must reference a ticket.");

        if (string.IsNullOrWhiteSpace(toolName))
            throw new DomainInvariantViolationException("Tool name is required.");

        if (string.IsNullOrWhiteSpace(proposedBy))
            throw new DomainInvariantViolationException("ProposedBy is required.");

        return new ActionExecution
        {
            Id = Guid.NewGuid(),
            TicketId = ticketId,
            ToolName = toolName,
            ArgumentsJson = argumentsJson,
            Risk = risk,
            RequiresApproval = risk >= RiskLevel.Moderate,
            ProposedBy = proposedBy,
            Reason = reason,
            CreatedAt = now
        };
    }
#pragma warning restore CS8618

    public void MarkAwaitingApproval()
    {
        EnsureStatus(ActionStatus.Proposed);

        if (!RequiresApproval)
            throw new DomainInvariantViolationException(
                "Only actions requiring approval can enter AwaitingApproval.");

        Status = ActionStatus.AwaitingApproval;
    }

    public void MarkApproved()
    {
        EnsureStatus(ActionStatus.AwaitingApproval);
        Status = ActionStatus.Approved;
    }

    public void MarkRejected(string? reason = null)
    {
        if (Status is not (ActionStatus.Proposed or ActionStatus.AwaitingApproval))
            throw new DomainInvariantViolationException(
                $"Action cannot be rejected from status {Status}.");

        Status = ActionStatus.Rejected;

        if (!string.IsNullOrWhiteSpace(reason))
            ResultSummary = reason;
    }

    public void BeginExecution()
    {
        if (RequiresApproval && Status != ActionStatus.Approved)
            throw new DomainInvariantViolationException(
                "An approval-required action must be approved before execution.");

        if (!RequiresApproval && Status != ActionStatus.Proposed)
            throw new DomainInvariantViolationException(
                $"A non-approval action must be Proposed before execution (current: {Status}).");

        Status = ActionStatus.Executing;
    }

    public void MarkSucceeded(string? resultSummary, DateTimeOffset executedAt)
    {
        EnsureStatus(ActionStatus.Executing);

        Status = ActionStatus.Succeeded;
        ResultSummary = resultSummary;
        ExecutedAt = executedAt;
    }

    public void MarkFailed(string? resultSummary, DateTimeOffset executedAt)
    {
        EnsureStatus(ActionStatus.Executing);

        Status = ActionStatus.Failed;
        ResultSummary = resultSummary;
        ExecutedAt = executedAt;
    }

    private void EnsureStatus(ActionStatus expected)
    {
        if (Status != expected)
        {
            throw new DomainInvariantViolationException(
                $"Action must be {expected} before this transition (current: {Status}).");
        }
    }
}