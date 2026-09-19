using AIOps.Domain;
using AIOps.Domain.Entities;
using Xunit;

namespace AIOps.Domain.Tests;

public class ActionExecutionTests
{
    private static readonly Guid TicketId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Safe_action_can_execute_without_approval()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.GetInstanceStatus",
            """{"instanceId":"i-123"}""",
            RiskLevel.Safe,
            "agent.triage",
            "Check instance health.",
            Now);

        action.BeginExecution();

        Assert.Equal(ActionStatus.Executing, action.Status);
    }

    [Fact]
    public void Approval_required_action_enters_awaiting_approval()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.RestartInstance",
            """{"instanceId":"i-123"}""",
            RiskLevel.Moderate,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now);

        action.MarkAwaitingApproval();

        Assert.Equal(ActionStatus.AwaitingApproval, action.Status);
    }

    [Fact]
    public void Approval_required_action_cannot_execute_before_approval()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.RestartInstance",
            """{"instanceId":"i-123"}""",
            RiskLevel.Moderate,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now);

        Assert.Throws<DomainInvariantViolationException>(
            () => action.BeginExecution());
    }

    [Fact]
    public void Approval_required_action_must_be_approved_before_execution()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.RestartInstance",
            """{"instanceId":"i-123"}""",
            RiskLevel.Moderate,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now);

        action.MarkAwaitingApproval();
        action.MarkApproved();
        action.BeginExecution();

        Assert.Equal(ActionStatus.Executing, action.Status);
    }

    [Fact]
    public void Rejected_action_cannot_execute()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Directory.ResetPassword",
            """{"userPrincipalName":"user@example.com"}""",
            RiskLevel.Sensitive,
            "agent.remediation",
            "Reset compromised account password.",
            Now);

        action.MarkAwaitingApproval();
        action.MarkRejected("Human reviewer rejected the action.");

        Assert.Equal(ActionStatus.Rejected, action.Status);

        Assert.Throws<DomainInvariantViolationException>(
            () => action.BeginExecution());
    }

    [Fact]
    public void Successful_execution_records_result_and_timestamp()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.GetInstanceStatus",
            """{"instanceId":"i-123"}""",
            RiskLevel.Safe,
            "agent.diagnostics",
            "Check instance health.",
            Now);

        action.BeginExecution();

        var executedAt = Now.AddMinutes(2);

        action.MarkSucceeded(
            """{"status":"running"}""",
            executedAt);

        Assert.Equal(ActionStatus.Succeeded, action.Status);
        Assert.Equal("""{"status":"running"}""", action.ResultSummary);
        Assert.Equal(executedAt, action.ExecutedAt);
    }

    [Fact]
    public void Failed_execution_records_failure()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.GetInstanceStatus",
            """{"instanceId":"i-123"}""",
            RiskLevel.Safe,
            "agent.diagnostics",
            "Check instance health.",
            Now);

        action.BeginExecution();

        var executedAt = Now.AddMinutes(2);

        action.MarkFailed(
            "Cloud provider unavailable.",
            executedAt);

        Assert.Equal(ActionStatus.Failed, action.Status);
        Assert.Equal("Cloud provider unavailable.", action.ResultSummary);
        Assert.Equal(executedAt, action.ExecutedAt);
    }

    [Fact]
    public void Action_cannot_be_approved_before_awaiting_approval()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.RestartInstance",
            """{"instanceId":"i-123"}""",
            RiskLevel.Moderate,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now);

        Assert.Throws<DomainInvariantViolationException>(
            () => action.MarkApproved());
    }

    [Fact]
    public void Action_cannot_be_rejected_after_execution_begins()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.GetInstanceStatus",
            """{"instanceId":"i-123"}""",
            RiskLevel.Safe,
            "agent.diagnostics",
            "Check instance health.",
            Now);

        action.BeginExecution();

        Assert.Throws<DomainInvariantViolationException>(
            () => action.MarkRejected("Too late."));
    }
}