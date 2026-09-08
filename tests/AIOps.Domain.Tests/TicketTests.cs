using AIOps.Domain;
using AIOps.Domain.Entities;
using Xunit;

namespace AIOps.Domain.Tests;

public class TicketTests
{
    [Fact]
    public void Create_valid_ticket_succeeds()
    {
        var t = Ticket.Create("VPN drops every morning", "Since Monday the VPN disconnects.",
            "jane@corp.example");
        Assert.Equal(TicketStatus.New, t.Status);
        Assert.Equal(TicketDomain.Unknown, t.Domain);
        Assert.False(string.IsNullOrWhiteSpace(t.ExternalRef));
    }

    [Theory]
    [InlineData("", "desc", "a@b.c")]      // no title
    [InlineData("t", "", "a@b.c")]         // no description
    [InlineData("t", "d", "not-an-email")] // invalid reporter
    public void Invalid_tickets_are_rejected(string title, string desc, string email)
        => Assert.Throws<DomainInvariantViolationException>(() => Ticket.Create(title, desc, email));

    [Fact]
    public void Triage_cannot_be_applied_after_resolution()
    {
        var t = Ticket.Create("t", "d", "a@b.c");
        TicketStatusTransitions.Ensure(t.Status, TicketStatus.Triaging); // guard example
        var resolved = Ticket.Create("t2", "d2", "a@b.c");
        // simulate resolved state via transitions is grain-enforced; here we verify the entity guard:
        Assert.Throws<DomainInvariantViolationException>(() =>
        {
            var r = typeof(Ticket).GetProperty(nameof(Ticket.Status))!;
            r.SetValue(resolved, TicketStatus.Resolved);
            resolved.ApplyTriage(TicketDomain.Network, Severity.P2, DateTimeOffset.UtcNow);
        });
    }
}

public class ApprovalRequestTests
{
    private static ApprovalRequest NewPending(DateTimeOffset now) =>
        ApprovalRequest.Create(Guid.NewGuid(), Guid.NewGuid(), "ActionAgent", "reset password for user", now, TimeSpan.FromHours(24));

    [Fact]
    public void Approval_can_be_approved_once()
    {
        var now = DateTimeOffset.UtcNow;
        var a = NewPending(now);
        a.Decide(true, "approver@corp.example", "looks safe", now.AddMinutes(5));
        Assert.Equal(ApprovalStatus.Approved, a.Status);
        Assert.Throws<DomainInvariantViolationException>(() =>
            a.Decide(false, "someone@corp.example", null, now.AddMinutes(6)));
    }

    [Fact]
    public void Expired_approval_cannot_be_decided()
    {
        var now = DateTimeOffset.UtcNow;
        var a = NewPending(now);
        var later = now.AddHours(25);
        Assert.True(a.IsExpired(later));
        Assert.Throws<DomainInvariantViolationException>(() =>
            a.Decide(true, "approver@corp.example", null, later));
    }
}
