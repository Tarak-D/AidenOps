namespace AIOps.Domain.Entities;

/// <summary>
/// Human-in-the-loop approval for an action execution. Bound 1:1 to an ActionExecution;
/// a Sensitive/Moderate tool may never execute without a bound, unexpired, Approved request.
/// </summary>
public sealed class ApprovalRequest
{
    public Guid Id { get; private set; }
    public Guid ActionExecutionId { get; private set; }
    public Guid TicketId { get; private set; }
    public string RequestedBy { get; private set; } = default!; // typically an agent name
    public string Justification { get; private set; } = default!;
    public ApprovalStatus Status { get; private set; } = ApprovalStatus.Pending;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }
    public string? DecidedBy { get; private set; }
    public DateTimeOffset? DecidedAt { get; private set; }
    public string? DecisionComment { get; private set; }

    private ApprovalRequest() { }

#pragma warning disable CS8618
    public static ApprovalRequest Create(
        Guid actionExecutionId,
        Guid ticketId,
        string requestedBy,
        string justification,
        DateTimeOffset now,
        TimeSpan ttl)
    {
        if (actionExecutionId == Guid.Empty) throw new DomainInvariantViolationException("Approval must be bound to an action execution.");
        if (ticketId == Guid.Empty) throw new DomainInvariantViolationException("Approval must reference a ticket.");
        if (string.IsNullOrWhiteSpace(requestedBy)) throw new DomainInvariantViolationException("RequestedBy is required.");
        if (string.IsNullOrWhiteSpace(justification)) throw new DomainInvariantViolationException("A justification is required for approval requests.");
        if (ttl <= TimeSpan.Zero) throw new DomainInvariantViolationException("Approval TTL must be positive.");

        return new ApprovalRequest
        {
            Id = Guid.NewGuid(),
            ActionExecutionId = actionExecutionId,
            TicketId = ticketId,
            RequestedBy = requestedBy,
            Justification = justification,
            Status = ApprovalStatus.Pending,
            CreatedAt = now,
            ExpiresAt = now + ttl
        };
    }
#pragma warning restore CS8618

    public bool IsExpired(DateTimeOffset now) => Status == ApprovalStatus.Pending && now >= ExpiresAt;

    public void Decide(bool approve, string decidedBy, string? comment, DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
            throw new DomainInvariantViolationException($"Approval is already {Status}; it cannot be decided twice.");
        if (IsExpired(now))
        {
            Status = ApprovalStatus.Expired;
            throw new DomainInvariantViolationException("Approval request has expired.");
        }
        if (string.IsNullOrWhiteSpace(decidedBy))
            throw new DomainInvariantViolationException("DecidedBy is required — every decision must be attributable.");

        Status = approve ? ApprovalStatus.Approved : ApprovalStatus.Rejected;
        DecidedBy = decidedBy;
        DecidedAt = now;
        DecisionComment = comment;
    }

    public void Expire(DateTimeOffset now)
    {
        if (Status != ApprovalStatus.Pending)
            throw new DomainInvariantViolationException($"Only pending approvals can expire (current: {Status}).");
        Status = ApprovalStatus.Expired;
        DecidedAt = now;
    }
}
