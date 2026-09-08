using AIOps.Domain;
using Xunit;

namespace AIOps.Domain.Tests;

public class TicketStatusTransitionsTests
{
    [Fact]
    public void Happy_path_is_allowed()
    {
        var path = new[]
        {
            (TicketStatus.New, TicketStatus.Triaging),
            (TicketStatus.Triaging, TicketStatus.Triaged),
            (TicketStatus.Triaged, TicketStatus.Investigating),
            (TicketStatus.Investigating, TicketStatus.KnowledgeRetrieval),
            (TicketStatus.KnowledgeRetrieval, TicketStatus.ResolutionProposed),
            (TicketStatus.ResolutionProposed, TicketStatus.AwaitingApproval),
            (TicketStatus.AwaitingApproval, TicketStatus.ActionExecuting),
            (TicketStatus.ActionExecuting, TicketStatus.Verifying),
            (TicketStatus.Verifying, TicketStatus.Resolved)
        };
        foreach (var (from, to) in path)
            Assert.True(TicketStatusTransitions.CanTransition(from, to), $"{from} -> {to} should be allowed");
    }

    [Theory]
    [InlineData(TicketStatus.New, TicketStatus.Resolved)]           // can't skip the pipeline
    [InlineData(TicketStatus.New, TicketStatus.ActionExecuting)]
    [InlineData(TicketStatus.Resolved, TicketStatus.Triaging)]     // terminal
    [InlineData(TicketStatus.Escalated, TicketStatus.New)]         // terminal
    [InlineData(TicketStatus.Triaging, TicketStatus.Verifying)]
    [InlineData(TicketStatus.AwaitingApproval, TicketStatus.Verifying)] // must go through execution
    public void Illegal_transitions_are_rejected(TicketStatus from, TicketStatus to)
    {
        Assert.False(TicketStatusTransitions.CanTransition(from, to));
        Assert.Throws<DomainInvariantViolationException>(() => TicketStatusTransitions.Ensure(from, to));
    }

    [Fact]
    public void Escalation_is_reachable_from_every_active_state()
    {
        var active = new[]
        {
            TicketStatus.New, TicketStatus.Triaging, TicketStatus.Triaged,
            TicketStatus.Investigating, TicketStatus.KnowledgeRetrieval,
            TicketStatus.ResolutionProposed, TicketStatus.AwaitingApproval, TicketStatus.Verifying
        };
        foreach (var s in active)
            Assert.True(TicketStatusTransitions.CanTransition(s, TicketStatus.Escalating),
                $"{s} must be able to escalate");
    }

    [Fact]
    public void Terminal_states_have_no_outgoing_transitions()
    {
        Assert.Empty(TicketStatusTransitions.AllowedFrom(TicketStatus.Resolved));
        Assert.Empty(TicketStatusTransitions.AllowedFrom(TicketStatus.Escalated));
    }
}
