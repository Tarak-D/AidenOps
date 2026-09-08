namespace AIOps.Domain;

/// <summary>
/// Authoritative allow-list of ticket status transitions. This is the single source of
/// truth for the ticket state machine; the Orleans Ticket grain enforces it, which gives
/// us a single-writer guarantee per ticket.
/// </summary>
public static class TicketStatusTransitions
{
    private static readonly IReadOnlyDictionary<TicketStatus, TicketStatus[]> Allowed =
        new Dictionary<TicketStatus, TicketStatus[]>
        {
            [TicketStatus.New] = [TicketStatus.Triaging, TicketStatus.Escalating],
            [TicketStatus.Triaging] = [TicketStatus.Triaged, TicketStatus.Escalating, TicketStatus.Failed],
            [TicketStatus.Triaged] = [TicketStatus.Investigating, TicketStatus.KnowledgeRetrieval, TicketStatus.Escalating],
            [TicketStatus.Investigating] = [TicketStatus.KnowledgeRetrieval, TicketStatus.ResolutionProposed, TicketStatus.Escalating, TicketStatus.Failed],
            [TicketStatus.KnowledgeRetrieval] = [TicketStatus.ResolutionProposed, TicketStatus.Escalating, TicketStatus.Failed],
            [TicketStatus.ResolutionProposed] = [TicketStatus.AwaitingApproval, TicketStatus.ActionExecuting, TicketStatus.Escalating, TicketStatus.Failed],
            [TicketStatus.AwaitingApproval] = [TicketStatus.ActionExecuting, TicketStatus.Escalating, TicketStatus.ResolutionProposed, TicketStatus.Failed],
            [TicketStatus.ActionExecuting] = [TicketStatus.Verifying, TicketStatus.Failed],
            [TicketStatus.Verifying] = [TicketStatus.Resolved, TicketStatus.ResolutionProposed, TicketStatus.Escalating, TicketStatus.Failed],
            [TicketStatus.Resolved] = [], // terminal
            [TicketStatus.Escalating] = [TicketStatus.Escalated, TicketStatus.Failed],
            [TicketStatus.Escalated] = [], // terminal (human engineer takes over)
            [TicketStatus.Failed] = [TicketStatus.Triaging, TicketStatus.Escalating] // manual retry allowed
        };

    public static bool CanTransition(TicketStatus from, TicketStatus to) =>
        Allowed.TryGetValue(from, out var targets) && targets.Contains(to);

    /// <summary>Throws <see cref="DomainInvariantViolationException"/> if the transition is not allowed.</summary>
    public static void Ensure(TicketStatus from, TicketStatus to)
    {
        if (!CanTransition(from, to))
        {
            throw new DomainInvariantViolationException(
                $"Illegal ticket status transition: {from} -> {to}.");
        }
    }

    public static IReadOnlyCollection<TicketStatus> AllowedFrom(TicketStatus from) =>
        Allowed.TryGetValue(from, out var targets) ? targets : Array.Empty<TicketStatus>();

    public static bool IsTerminal(TicketStatus status) =>
        status is TicketStatus.Resolved or TicketStatus.Escalated;
}
