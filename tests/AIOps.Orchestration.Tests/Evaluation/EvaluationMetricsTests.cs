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
                "resolved",
                TicketDomain.Network,
                Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                approvalRequired: false,
                executionSucceeded: true,
                finalOutcome: AgentRunOutcome.Resolved),

            CreateResult(
                "approval",
                TicketDomain.Infrastructure,
                Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                approvalRequired: true,
                executionSucceeded: true,
                finalOutcome: AgentRunOutcome.Resolved),

            CreateResult(
                "escalated",
                TicketDomain.Database,
                Severity.P1,
                domainCorrect: true,
                severityCorrect: true,
                approvalRequired: false,
                executionSucceeded: null,
                finalOutcome: AgentRunOutcome.Escalated)
        };

        var metrics = EvaluationMetrics.Calculate(results);

        Assert.Equal(3, metrics.SampleCount);
        Assert.Equal(1.0, metrics.TriageAccuracy);
        Assert.Equal(1.0, metrics.DomainClassificationAccuracy);
        Assert.Equal(1.0, metrics.SeverityClassificationAccuracy);
        Assert.Equal(1.0, metrics.ToolSelectionAccuracy);
        Assert.Equal(1.0, metrics.ToolArgumentValidity);
        Assert.Equal(1.0 / 3.0, metrics.ApprovalRate);
        Assert.Equal(1.0, metrics.ExecutionSuccessRate);
        Assert.Equal(2.0 / 3.0, metrics.ResolutionRate);
        Assert.Equal(1.0 / 3.0, metrics.EscalationRate);
    }

    private static EvaluationCaseResult CreateResult(
        string id,
        TicketDomain domain,
        Severity severity,
        bool domainCorrect,
        bool severityCorrect,
        bool approvalRequired,
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
            finalOutcome,
            approvalRequired,
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