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
    public string ArgumentsJson { get; private set; } = "{}"; // validated before use
    public RiskLevel Risk { get; private set; }
    public bool RequiresApproval { get; private set; }
    public string ProposedBy { get; private set; } = default!; // agent name or human id
    public string? Reason { get; private set; }
    public ActionStatus Status { get; private set; } = ActionStatus.Proposed;
    public string? ResultSummary { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset? ExecutedAt { get; private set; }

    private ActionExecution() { }

#pragma warning disable CS8618
    public static ActionExecution Propose(
        Guid ticketId, string toolName, string argumentsJson, RiskLevel risk,
        string proposedBy, string? reason, DateTimeOffset now)
    {
        if (ticketId == Guid.Empty) throw new DomainInvariantViolationException("Action must reference a ticket.");
        if (string.IsNullOrWhiteSpace(toolName)) throw new DomainInvariantViolationException("Tool name is required.");
        if (string.IsNullOrWhiteSpace(proposedBy)) throw new DomainInvariantViolationException("ProposedBy is required.");

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
}
