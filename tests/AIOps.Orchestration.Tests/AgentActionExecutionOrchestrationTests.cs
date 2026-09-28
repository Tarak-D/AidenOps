using System.Text.Json;
using AIOps.Abstractions;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Grains;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Orchestration;
using AIOps.Orchestration.Agents;
using AIOps.Orchestration.Approvals;
using AIOps.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Orleans;

namespace AIOps.Orchestration.Tests;

public sealed class AgentActionExecutionOrchestrationTests
{
    [Fact]
    public async Task Safe_proposal_executes_through_tool_executor_and_persists_success()
    {
        var harness = CreateHarness(RiskLevel.Safe);

        var result = await harness.Orchestrator.StartAgentRunAsync(
            TicketId,
            CreateRequest());

        var action = Assert.Single(harness.Store.Actions.Values);
        Assert.Equal(AgentRunOutcome.Resolved, result.Outcome);
        Assert.Equal(ActionStatus.Succeeded, action.Status);
        Assert.Equal("Simulated tool completed.", action.ResultSummary);
        Assert.Equal(1, harness.Tool.ExecutionCount);
        var resume = Assert.Single(harness.ResumeRequests);
        Assert.Equal(action.Id, resume.ActionExecutionId);
        Assert.Equal("Succeeded", resume.ActionStatus);
        Assert.Equal("Simulated tool completed.", JsonDocument.Parse(resume.ToolResultJson!).RootElement.GetProperty("resultSummary").GetString());
        Assert.True(resume.ToolExecutionSucceeded);
        Assert.False(resume.ApprovalGranted);
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ActionExecutionStarted");
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ActionExecutionSucceeded");
    }

    [Fact]
    public async Task Failed_safe_execution_persists_failure_and_audits_it()
    {
        var harness = CreateHarness(RiskLevel.Safe, succeeds: false);

        var result = await harness.Orchestrator.StartAgentRunAsync(
            TicketId,
            CreateRequest());

        var action = Assert.Single(harness.Store.Actions.Values);
        Assert.Equal(AgentRunOutcome.Escalated, result.Outcome);
        Assert.Equal(ActionStatus.Failed, action.Status);
        Assert.Contains("Simulated tool failure", action.ResultSummary);
        Assert.Equal(1, harness.Tool.ExecutionCount);
        var resume = Assert.Single(harness.ResumeRequests);
        Assert.Equal("Failed", resume.ActionStatus);
        Assert.False(resume.ToolExecutionSucceeded);
        Assert.Equal(action.ResultSummary, resume.ToolExecutionError);
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ActionExecutionFailed");
    }

    [Fact]
    public async Task Approval_required_proposal_waits_and_approved_action_executes()
    {
        var harness = CreateHarness(RiskLevel.Moderate);

        var proposalResult = await harness.Orchestrator.StartAgentRunAsync(
            TicketId,
            CreateRequest());

        var action = Assert.Single(harness.Store.Actions.Values);
        var approval = Assert.Single(harness.Store.Approvals.Values);
        Assert.Equal(AgentRunOutcome.AwaitingApproval, proposalResult.Outcome);
        Assert.Equal(ActionStatus.AwaitingApproval, action.Status);
        Assert.Equal(ApprovalStatus.Pending, approval.Status);
        Assert.Equal(0, harness.Tool.ExecutionCount);
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ApprovalRequested");

        await harness.Orchestrator.DecideApprovalAsync(
            approval.Id,
            approve: true,
            decidedBy: "approver@example.com",
            comment: "Approved.");

        Assert.Equal(ApprovalStatus.Approved, approval.Status);
        Assert.Equal(ActionStatus.Succeeded, action.Status);
        Assert.Equal(1, harness.Tool.ExecutionCount);
        var resume = Assert.Single(harness.ResumeRequests);
        Assert.True(resume.ApprovalGranted);
        Assert.Equal("Approved", resume.ApprovalStatus);
        Assert.Equal("approver@example.com", resume.ApprovalDecidedBy);
        Assert.Equal("Succeeded", resume.ActionStatus);
        Assert.Contains("Simulated tool completed", resume.ToolResultJson);
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ApprovalApproved");
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ActionExecutionStarted");
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ActionExecutionSucceeded");
    }

    [Fact]
    public async Task Rejected_action_is_not_executed_and_rejection_is_persisted()
    {
        var harness = CreateHarness(RiskLevel.Sensitive);
        await harness.Orchestrator.StartAgentRunAsync(TicketId, CreateRequest());
        var action = Assert.Single(harness.Store.Actions.Values);
        var approval = Assert.Single(harness.Store.Approvals.Values);

        await harness.Orchestrator.DecideApprovalAsync(
            approval.Id,
            approve: false,
            decidedBy: "approver@example.com",
            comment: "Do not perform this action.");

        Assert.Equal(ApprovalStatus.Rejected, approval.Status);
        Assert.Equal(ActionStatus.Rejected, action.Status);
        Assert.Equal("Do not perform this action.", action.ResultSummary);
        Assert.Equal(0, harness.Tool.ExecutionCount);
        var resume = Assert.Single(harness.ResumeRequests);
        Assert.False(resume.ApprovalGranted);
        Assert.Equal("Rejected", resume.ApprovalStatus);
        Assert.Equal("approver@example.com", resume.ApprovalDecidedBy);
        Assert.Equal("Rejected", resume.ActionStatus);
        Assert.Equal("Do not perform this action.", resume.ToolExecutionError);
        Assert.DoesNotContain(harness.Store.AuditRecords, record => record.EventType == "ActionExecutionStarted");
        Assert.Contains(harness.Store.AuditRecords, record => record.EventType == "ApprovalRejected");
    }

    [Fact]
    public async Task Approved_action_without_persisted_approved_request_is_blocked_by_executor()
    {
        var harness = CreateHarness(RiskLevel.Moderate);
        await harness.Orchestrator.StartAgentRunAsync(TicketId, CreateRequest());
        var action = Assert.Single(harness.Store.Actions.Values);
        var approval = Assert.Single(harness.Store.Approvals.Values);
        approval.Decide(true, "approver@example.com", "Approved.", Now);
        action.MarkApproved();
        await harness.Store.UpdateAsync(action);
        harness.Store.Approvals.Clear();

        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => harness.Orchestrator.ExecuteApprovedActionAsync(action.Id));

        Assert.Equal(ActionStatus.Approved, action.Status);
        Assert.Equal(0, harness.Tool.ExecutionCount);
        Assert.Empty(harness.ResumeRequests);
    }

    [Fact]
    public async Task Expired_approval_is_rejected_and_resumed_from_persisted_state()
    {
        var harness = CreateHarness(RiskLevel.Moderate);
        await harness.Orchestrator.StartAgentRunAsync(TicketId, CreateRequest());
        var action = Assert.Single(harness.Store.Actions.Values);
        var approval = Assert.Single(harness.Store.Approvals.Values);
        harness.Clock.UtcNow = Now.AddHours(25);

        await harness.Orchestrator.ListPendingApprovalsAsync();

        Assert.Equal(ApprovalStatus.Expired, approval.Status);
        Assert.Equal(ActionStatus.Rejected, action.Status);
        Assert.Equal(0, harness.Tool.ExecutionCount);
        var resume = Assert.Single(harness.ResumeRequests);
        Assert.False(resume.ApprovalGranted);
        Assert.Equal("Expired", resume.ApprovalStatus);
        Assert.Equal("Rejected", resume.ActionStatus);
        Assert.Equal("Approval expired.", resume.ToolExecutionError);
    }

    [Fact]
    public async Task Forged_resume_values_are_ignored_in_favor_of_persisted_action_state()
    {
        var harness = CreateHarness(RiskLevel.Safe);
        await harness.Orchestrator.StartAgentRunAsync(TicketId, CreateRequest());
        var action = Assert.Single(harness.Store.Actions.Values);

        await harness.Orchestrator.ResumeAgentRunAsync(new ResumeAgentRunRequest(
            Guid.NewGuid(),
            TicketId,
            ApprovalGranted: true,
            ApprovalDecidedBy: "forged-approver",
            ToolResultJson: "{\"forged\":true}",
            ToolExecutionSucceeded: false,
            ActionExecutionId: action.Id,
            ApprovalStatus: "Rejected",
            ActionStatus: "Failed",
            ToolExecutionError: "forged error"));

        var resume = harness.ResumeRequests[^1];
        Assert.Equal(action.Id, resume.CorrelationId);
        Assert.Equal(action.Id, resume.ActionExecutionId);
        Assert.Equal("Succeeded", resume.ActionStatus);
        Assert.Null(resume.ApprovalStatus);
        Assert.Null(resume.ApprovalDecidedBy);
        Assert.True(resume.ToolExecutionSucceeded);
        Assert.False(resume.ApprovalGranted);
        Assert.Contains("Simulated tool completed", resume.ToolResultJson);
        Assert.Null(resume.ToolExecutionError);
    }

    [Fact]
    public async Task Missing_action_or_nonterminal_state_never_calls_gateway_resume()
    {
        var harness = CreateHarness(RiskLevel.Safe);
        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => harness.Orchestrator.ResumeAgentRunAsync(new ResumeAgentRunRequest(
                Guid.NewGuid(),
                TicketId,
                true,
                "forged",
                "{}",
                true,
                ActionExecutionId: Guid.NewGuid())));

        var proposed = ActionExecution.Propose(
            TicketId,
            "Test.Tool",
            "{}",
            RiskLevel.Safe,
            "ToolProposalAgent",
            "not yet executed",
            Now);
        await harness.Store.AddAsync(proposed);
        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => harness.Orchestrator.ResumeAgentRunAsync(new ResumeAgentRunRequest(
                Guid.Empty,
                TicketId,
                false,
                null,
                null,
                false,
                proposed.Id)));

        Assert.Empty(harness.ResumeRequests);
    }

    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    private static AgentRunRequest CreateRequest() => new(
        CorrelationId,
        new AgentTicketContext(
            TicketId,
            "INC-13",
            "Test incident",
            "A test incident description.",
            "reporter@example.com",
            TicketDomain.Unknown,
            Severity.P2,
            TicketStatus.New),
        Array.Empty<ToolManifestEntry>(),
        0.6,
        2);

    private static Harness CreateHarness(
        RiskLevel risk,
        bool succeeds = true)
    {
        var store = new InMemoryLifecycleStore();
        var tool = new TestTool(risk, succeeds);
        var registry = new ToolRegistry([tool]);
        var clock = new FixedClock(Now);
        var audit = (IAuditStore)store;
        var actionStore = (IActionExecutionStore)store;
        var approvalStore = (IApprovalStore)store;
        var approvalService = new ApprovalService(
            actionStore,
            approvalStore,
            audit,
            clock,
            Options.Create(new ApprovalOptions()));
        var executor = new ToolExecutor(registry, store, clock);
        var validation = new ToolProposalValidationService(
            registry,
            actionStore,
            audit,
            clock);
        var gateway = new Mock<IAgentGateway>();
        var resumeRequests = new List<ResumeAgentRunRequest>();
        gateway.Setup(x => x.StartRunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new AgentRunResult(
                AgentRunOutcome.ProposalCreated,
                0.95,
                TicketDomain.Unknown,
                Severity.P2,
                new ToolProposal(
                    tool.Name,
                    "{}",
                    0.9,
                    "The registered test tool is appropriate."),
                null,
                null,
                Array.Empty<StepTrace>(),
                null));
        gateway.Setup(x => x.ResumeRunAsync(It.IsAny<ResumeAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ResumeAgentRunRequest, CancellationToken>((request, _) => resumeRequests.Add(request))
            .ReturnsAsync((ResumeAgentRunRequest request, CancellationToken _) =>
                new AgentRunResult(
                    request.ActionStatus == "Succeeded"
                        ? AgentRunOutcome.Resolved
                        : AgentRunOutcome.Escalated,
                    null,
                    null,
                    null,
                    null,
                    request.ActionStatus == "Succeeded" ? null : request.ToolExecutionError,
                    request.ActionStatus == "Succeeded" ? "Action verified." : null,
                    Array.Empty<StepTrace>(),
                    request.ActionStatus == "Succeeded" ? null : request.ToolExecutionError));

        var grain = new Mock<ITicketGrain>();
        grain.Setup(x => x.GetState())
            .ReturnsAsync(new TicketState { Exists = true });
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(x => x.GetGrain<ITicketGrain>(TicketId, null))
            .Returns(grain.Object);

        var orchestrator = new Orchestrator(
            cluster.Object,
            audit,
            gateway.Object,
            registry,
            validation,
            actionStore,
            approvalStore,
            approvalService,
            executor,
            clock,
            Options.Create(new AgentPolicyOptions()),
            NullLogger<Orchestrator>.Instance);

        return new Harness(orchestrator, store, tool, clock, resumeRequests);
    }

    private sealed record Harness(
        Orchestrator Orchestrator,
        InMemoryLifecycleStore Store,
        TestTool Tool,
        FixedClock Clock,
        List<ResumeAgentRunRequest> ResumeRequests);

    private sealed class FixedClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = utcNow;
    }

    private sealed class TestTool(RiskLevel risk, bool succeeds) : ITool
    {
        public string Name => "Test.Tool";
        public string Description => "Test action execution.";
        public RiskLevel Risk => risk;
        public bool RequiresApproval => risk >= RiskLevel.Moderate;
        public string InputSchemaJson => """
            {"type":"object","properties":{},"additionalProperties":false}
            """;
        public int ExecutionCount { get; private set; }

        public Task<ToolResult> ExecuteAsync(
            string argumentsJson,
            ToolExecutionContext ctx,
            CancellationToken ct = default)
        {
            ExecutionCount++;
            return Task.FromResult(succeeds
                ? new ToolResult(true, "Simulated tool completed.")
                : new ToolResult(false, "Simulated tool failure.", Error: "SimulatedFailure"));
        }
    }

    private sealed class InMemoryLifecycleStore :
        IActionExecutionStore,
        IApprovalStore,
        IApprovalValidator,
        IAuditStore
    {
        public Dictionary<Guid, ActionExecution> Actions { get; } = [];
        public Dictionary<Guid, ApprovalRequest> Approvals { get; } = [];
        public List<AuditRecordInput> AuditRecords { get; } = [];

        public Task AddAsync(ActionExecution actionExecution, CancellationToken ct = default)
        {
            Actions.Add(actionExecution.Id, actionExecution);
            return Task.CompletedTask;
        }

        public Task<ActionExecution?> GetAsync(Guid actionExecutionId, CancellationToken ct = default) =>
            Task.FromResult(Actions.GetValueOrDefault(actionExecutionId));

        public Task UpdateAsync(ActionExecution actionExecution, CancellationToken ct = default)
        {
            Actions[actionExecution.Id] = actionExecution;
            return Task.CompletedTask;
        }

        public Task AddAsync(ApprovalRequest approval, CancellationToken ct = default)
        {
            Approvals.Add(approval.Id, approval);
            return Task.CompletedTask;
        }

        Task<ApprovalRequest?> IApprovalStore.GetAsync(Guid approvalId, CancellationToken ct) =>
            Task.FromResult(Approvals.GetValueOrDefault(approvalId));

        public Task<ApprovalRequest?> GetForActionExecutionAsync(Guid actionExecutionId, CancellationToken ct = default) =>
            Task.FromResult(Approvals.Values.SingleOrDefault(x => x.ActionExecutionId == actionExecutionId));

        public Task UpdateAsync(ApprovalRequest approval, CancellationToken ct = default)
        {
            Approvals[approval.Id] = approval;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<ApprovalRequest>>(
                Approvals.Values.Where(x => x.Status == ApprovalStatus.Pending).ToArray());

        public Task<ApprovalRequest?> GetApprovalForActionExecutionAsync(Guid actionExecutionId, CancellationToken ct = default) =>
            GetForActionExecutionAsync(actionExecutionId, ct);

        public Task AppendAsync(AuditRecordInput record, CancellationToken ct = default)
        {
            AuditRecords.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditRecordView>> QueryAsync(
            string? entityType = null,
            string? entityId = null,
            string? actorId = null,
            int take = 100,
            CancellationToken ct = default) =>
            Task.FromResult<IReadOnlyList<AuditRecordView>>([]);
    }
}
