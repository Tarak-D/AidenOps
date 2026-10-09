
using AIOps.Abstractions.Evaluation;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;

namespace AIOps.Orchestration.Tests.Evaluation;

public sealed class EvaluationMetricsTests
{
    [Fact]
    public void Calculate_returns_expected_phase8_metric_shape()
    {
        var results = new[]
        {
            CreateResult(
                id: "resolved",
                domain: TicketDomain.Network,
                severity: Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                decisionCorrect: true,
                approvalRequired: false,
                expectedApprovalRequired: false,
                executionSucceeded: true,
                finalOutcome: AgentRunOutcome.Resolved),

            CreateResult(
                id: "approval",
                domain: TicketDomain.Infrastructure,
                severity: Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                decisionCorrect: true,
                approvalRequired: true,
                expectedApprovalRequired: true,
                executionSucceeded: true,
                finalOutcome: AgentRunOutcome.Resolved),

            CreateResult(
                id: "escalated",
                domain: TicketDomain.Database,
                severity: Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                decisionCorrect: true,
                approvalRequired: false,
                expectedApprovalRequired: false,
                executionSucceeded: null,
                finalOutcome: AgentRunOutcome.Escalated)
        };

        var metrics = EvaluationMetrics.Calculate(results);

        Assert.Equal(3, metrics.SampleCount);
        Assert.Equal(1.0, metrics.TriageAccuracy);
        Assert.Equal(1.0, metrics.DecisionAccuracy);
        Assert.Equal(1.0, metrics.DomainClassificationAccuracy);
        Assert.Equal(1.0, metrics.SeverityClassificationAccuracy);
        Assert.Equal(1.0, metrics.ToolSelectionAccuracy);
        Assert.Equal(1.0, metrics.ToolArgumentValidity);
        Assert.Equal(1.0 / 3.0, metrics.ApprovalRate);
        Assert.Equal(1.0, metrics.ApprovalPolicyAccuracy);
        Assert.Equal(1.0, metrics.ExecutionSuccessRate);
        Assert.Equal(2.0 / 3.0, metrics.ResolutionRate);
        Assert.Equal(1.0 / 3.0, metrics.EscalationRate);
    }

    [Fact]
    public void Calculate_detects_approval_policy_mismatch()
    {
        var results = new[]
        {
            CreateResult(
                id: "correct",
                domain: TicketDomain.Infrastructure,
                severity: Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                decisionCorrect: true,
                approvalRequired: true,
                expectedApprovalRequired: true,
                executionSucceeded: true,
                finalOutcome: AgentRunOutcome.Resolved),

            CreateResult(
                id: "incorrect",
                domain: TicketDomain.Network,
                severity: Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                decisionCorrect: false,
                approvalRequired: true,
                expectedApprovalRequired: false,
                executionSucceeded: true,
                finalOutcome: AgentRunOutcome.Resolved)
        };

        var metrics = EvaluationMetrics.Calculate(results);

        Assert.Equal(1.0, metrics.ApprovalRate);
        Assert.Equal(0.5, metrics.ApprovalPolicyAccuracy);
        Assert.Equal(0.5, metrics.DecisionAccuracy);
    }

    private static EvaluationCaseResult CreateResult(
        string id,
        TicketDomain domain,
        Severity severity,
        bool domainCorrect,
        bool severityCorrect,
        bool decisionCorrect,
        bool approvalRequired,
        bool expectedApprovalRequired,
        bool? executionSucceeded,
        AgentRunOutcome finalOutcome)
    {
        var initialOutcome = approvalRequired
            ? AgentRunOutcome.AwaitingApproval
            : finalOutcome == AgentRunOutcome.Resolved
                ? AgentRunOutcome.Resolved
                : AgentRunOutcome.Escalated;

        return new EvaluationCaseResult(
            id,
            Guid.NewGuid(),
            Guid.NewGuid(),
            domain,
            domain,
            domainCorrect,
            severity,
            severity,
            severityCorrect,
            domainCorrect && severityCorrect,
            "Example.Tool",
            "Example.Tool",
            true,
            true,
            initialOutcome,
            decisionCorrect,
            finalOutcome,
            expectedApprovalRequired,
            approvalRequired,
            approvalRequired == expectedApprovalRequired,
            executionSucceeded,
            finalOutcome == AgentRunOutcome.Resolved,
            finalOutcome == AgentRunOutcome.Escalated,
            10,
            5,
            7,
            null,
            Array.Empty<StepTrace>());
    }
}