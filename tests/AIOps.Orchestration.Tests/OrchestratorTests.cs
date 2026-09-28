using AIOps.Infrastructure.Agents;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Grains;
using AIOps.Abstractions;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Orchestration;
using AIOps.Orchestration.Approvals;
using AIOps.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using Orleans;

namespace AIOps.Orchestration.Tests;

public sealed class OrchestratorTests
{
    [Fact]
    public async Task StartAgentRun_builds_server_manifest_and_persists_validated_proposal()
    {
        var ticketId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();
        const string schema = """
            {"type":"object","properties":{"userPrincipalName":{"type":"string","minLength":1}},"required":["userPrincipalName"],"additionalProperties":false}
            """;
        var tool = new ManifestTestTool(schema);
        var registry = new Mock<IToolRegistry>();
        registry.Setup(x => x.Describe()).Returns(
        [
            new ToolDescriptor(tool.Name, tool.Description, tool.Risk, tool.RequiresApproval, schema)
        ]);
        registry.Setup(x => x.Get(tool.Name)).Returns(tool);

        var actionStore = new Mock<IActionExecutionStore>();
        ActionExecution? persisted = null;
        actionStore.Setup(x => x.AddAsync(It.IsAny<ActionExecution>(), It.IsAny<CancellationToken>()))
            .Callback<ActionExecution, CancellationToken>((action, _) => persisted = action)
            .Returns(Task.CompletedTask);
        actionStore.Setup(x => x.GetAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .Returns(() => Task.FromResult(persisted));
        actionStore.Setup(x => x.UpdateAsync(It.IsAny<ActionExecution>(), It.IsAny<CancellationToken>()))
            .Callback<ActionExecution, CancellationToken>((action, _) => persisted = action)
            .Returns(Task.CompletedTask);
        var approvalStore = new Mock<IApprovalStore>();
        ApprovalRequest? persistedApproval = null;
        approvalStore.Setup(x => x.GetForActionExecutionAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ApprovalRequest?)null);
        approvalStore.Setup(x => x.AddAsync(It.IsAny<ApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRequest, CancellationToken>((approval, _) => persistedApproval = approval)
            .Returns(Task.CompletedTask);
        approvalStore.Setup(x => x.UpdateAsync(It.IsAny<ApprovalRequest>(), It.IsAny<CancellationToken>()))
            .Callback<ApprovalRequest, CancellationToken>((approval, _) => persistedApproval = approval)
            .Returns(Task.CompletedTask);
        var audit = new Mock<IAuditStore>();
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditRecordInput>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        var grain = new Mock<ITicketGrain>();
        grain.Setup(x => x.GetState()).ReturnsAsync(new TicketState { Exists = true });
        var cluster = new Mock<IClusterClient>();
        cluster.Setup(x => x.GetGrain<ITicketGrain>(ticketId, null)).Returns(grain.Object);

        AgentRunRequest? sentRequest = null;
        var gateway = new Mock<IAgentGateway>();
        gateway.Setup(x => x.StartRunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
            .Callback<AgentRunRequest, CancellationToken>((request, _) => sentRequest = request)
            .ReturnsAsync(new AgentRunResult(
                AgentRunOutcome.ProposalCreated,
                0.9,
                TicketDomain.Identity,
                Severity.P2,
                new ToolProposal(
                    tool.Name,
                    "{\"userPrincipalName\":\"user@example.com\"}",
                    0.9,
                    "Password reset requested.",
                    RiskLevel.Safe,
                    false),
                null,
                null,
                Array.Empty<StepTrace>(),
                null));

        var validation = new AIOps.Orchestration.Agents.ToolProposalValidationService(
            registry.Object,
            actionStore.Object,
            audit.Object,
            clock.Object);
        var approvalService = new ApprovalService(
            actionStore.Object,
            approvalStore.Object,
            audit.Object,
            clock.Object,
            Options.Create(new ApprovalOptions()));
        var approvalValidator = new Mock<IApprovalValidator>();
        var executor = new ToolExecutor(
            registry.Object,
            approvalValidator.Object,
            clock.Object);
        var orchestrator = new Orchestrator(
            cluster.Object,
            audit.Object,
            gateway.Object,
            registry.Object,
            validation,
            actionStore.Object,
            approvalStore.Object,
            approvalService,
            executor,
            clock.Object,
            Options.Create(new AgentPolicyOptions
            {
                TriageConfidenceThreshold = 0.7,
                MaxAttempts = 2
            }),
            NullLogger<Orchestrator>.Instance);
        var request = new AgentRunRequest(
            correlationId,
            new AgentTicketContext(
                ticketId,
                "INC-12",
                "Reset password",
                "User locked out",
                "user@example.com",
                TicketDomain.Identity,
                Severity.P2,
                TicketStatus.New),
            [new ToolManifestEntry("Untrusted.Tool", "Untrusted", RiskLevel.Safe, false, "{}")],
            0.0,
            99);

        var result = await orchestrator.StartAgentRunAsync(ticketId, request);

        Assert.NotNull(sentRequest);
        var manifest = Assert.Single(sentRequest!.AllowedTools);
        Assert.Equal(tool.Name, manifest.Name);
        Assert.Equal(RiskLevel.Sensitive, manifest.Risk);
        Assert.True(manifest.RequiresApproval);
        Assert.Equal(schema, manifest.InputSchemaJson);
        Assert.Equal(0.7, sentRequest.TriageConfidenceThreshold);
        Assert.Equal(2, sentRequest.MaxAttempts);
        Assert.Equal(AgentRunOutcome.AwaitingApproval, result.Outcome);
        Assert.Equal(RiskLevel.Sensitive, result.Proposal!.Risk);
        Assert.True(result.Proposal.RequiresApproval);
        Assert.NotNull(persisted);
        Assert.Equal(ActionStatus.AwaitingApproval, persisted!.Status);
        Assert.NotNull(persistedApproval);
        Assert.Equal(ApprovalStatus.Pending, persistedApproval!.Status);
    }

    [Fact]
    public async Task StartAgentRun_delegates_to_gateway_and_audits_result()
    {
        var ticketId = Guid.NewGuid();
        var correlationId = Guid.NewGuid();

        var grain = new Mock<ITicketGrain>();

        grain.Setup(x => x.GetState())
            .ReturnsAsync(new TicketState
            {
                Exists = true,
                Title = "VPN keeps dropping",
                Description = "Details here"
            });

        var cluster = new Mock<IClusterClient>();

        cluster
            .Setup(x => x.GetGrain<ITicketGrain>(ticketId, null))
            .Returns(grain.Object);

        var gateway = new Mock<IAgentGateway>();

        var expectedResult = new AgentRunResult(
            AgentRunOutcome.AwaitingApproval,
            0.92,
            TicketDomain.Network,
            Severity.P2,
            null,
            null,
            null,
            Array.Empty<StepTrace>(),
            null);

        gateway
            .Setup(x => x.StartRunAsync(
                It.IsAny<AgentRunRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var audit = new Mock<IAuditStore>();

        var request = new AgentRunRequest(
            correlationId,
            new AgentTicketContext(
                ticketId,
                "EXT-ORCH-1",
                "VPN keeps dropping",
                "Details here",
                "user@corp.example",
                TicketDomain.Network,
                Severity.P2,
                TicketStatus.New),
            Array.Empty<ToolManifestEntry>(),
            0.80,
            3);

        var orchestrator = CreateOrchestrator(
            cluster.Object,
            audit.Object,
            gateway.Object);

        var result = await orchestrator.StartAgentRunAsync(
            ticketId,
            request);

        Assert.Equal(
            AgentRunOutcome.AwaitingApproval,
            result.Outcome);

        gateway.Verify(
            x => x.StartRunAsync(
                It.Is<AgentRunRequest>(actual =>
                    actual.CorrelationId == correlationId &&
                    actual.AllowedTools.Count == 0),
                It.IsAny<CancellationToken>()),
            Times.Once);

        audit.Verify(
            x => x.AppendAsync(
                It.Is<AuditRecordInput>(r =>
                    r.CorrelationId == correlationId &&
                    r.EventType == "AgentRunStarted" &&
                    r.EntityType == "ticket" &&
                    r.EntityId == ticketId.ToString("N")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task StartAgentRun_for_missing_ticket_is_rejected()
    {
        var ticketId = Guid.NewGuid();

        var grain = new Mock<ITicketGrain>();

        grain.Setup(x => x.GetState())
            .ReturnsAsync(new TicketState
            {
                Exists = false
            });

        var cluster = new Mock<IClusterClient>();

        cluster
            .Setup(x => x.GetGrain<ITicketGrain>(ticketId, null))
            .Returns(grain.Object);

        var gateway = new Mock<IAgentGateway>();
        var audit = new Mock<IAuditStore>();

        var request = new AgentRunRequest(
            Guid.NewGuid(),
            new AgentTicketContext(
                ticketId,
                "MISSING",
                "Missing ticket",
                "Details",
                "user@corp.example",
                TicketDomain.Network,
                Severity.P3,
                TicketStatus.New),
            Array.Empty<ToolManifestEntry>(),
            0.80,
            3);

        var orchestrator = CreateOrchestrator(
            cluster.Object,
            audit.Object,
            gateway.Object);

        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => orchestrator.StartAgentRunAsync(
                ticketId,
                request));

        gateway.Verify(
            x => x.StartRunAsync(
                It.IsAny<AgentRunRequest>(),
                It.IsAny<CancellationToken>()),
            Times.Never);

        audit.Verify(
            x => x.AppendAsync(
                It.IsAny<AuditRecordInput>(),
                It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ResumeAgentRun_delegates_to_gateway_and_audits_result()
    {
        var ticketId = Guid.NewGuid();
        var action = ActionExecution.Propose(ticketId, "Test.Tool", "{}", RiskLevel.Safe, "agent", null, DateTimeOffset.UtcNow);
        action.BeginExecution();
        action.MarkSucceeded("Persisted actual result", DateTimeOffset.UtcNow);
        var correlationId = Guid.NewGuid();

        var grain = new Mock<ITicketGrain>();

        grain.Setup(x => x.GetState())
            .ReturnsAsync(new TicketState
            {
                Exists = true,
                Title = "Database latency",
                Description = "Queries are slow"
            });

        var cluster = new Mock<IClusterClient>();

        cluster
            .Setup(x => x.GetGrain<ITicketGrain>(ticketId, null))
            .Returns(grain.Object);

        var gateway = new Mock<IAgentGateway>();

        var expectedResult = new AgentRunResult(
            AgentRunOutcome.Resolved,
            0.99,
            TicketDomain.Network,
            Severity.P2,
            null,
            null,
            "Resolved",
            Array.Empty<StepTrace>(),
            null);

        gateway
            .Setup(x => x.ResumeRunAsync(
                It.IsAny<ResumeAgentRunRequest>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var audit = new Mock<IAuditStore>();

        var request = new ResumeAgentRunRequest(
            Guid.NewGuid(),
            ticketId,
            false,
            "forged-actor",
            "forged-result",
            false,
            action.Id);

        var actionStore = new Mock<AIOps.Abstractions.Persistence.IActionExecutionStore>();
        actionStore.Setup(x => x.GetAsync(action.Id, It.IsAny<CancellationToken>())).ReturnsAsync(action);
        var approvalStore = new Mock<AIOps.Abstractions.Persistence.IApprovalStore>();
        approvalStore.Setup(x => x.GetForActionExecutionAsync(action.Id, It.IsAny<CancellationToken>())).ReturnsAsync((ApprovalRequest?)null);

        var orchestrator = CreateOrchestrator(
            cluster.Object,
            audit.Object,
            gateway.Object,
            actionStore.Object,
            approvalStore.Object);

        var result = await orchestrator.ResumeAgentRunAsync(
            request);

        Assert.Equal(
            AgentRunOutcome.Resolved,
            result.Outcome);

        gateway.Verify(x => x.ResumeRunAsync(
            It.Is<ResumeAgentRunRequest>(r =>
                r.ActionExecutionId == action.Id && r.TicketId == ticketId &&
                r.ToolExecutionSucceeded && !r.ApprovalGranted &&
                r.ToolResultJson!.Contains("Persisted actual result") && r.ToolResultJson != "forged-result"),
            It.IsAny<CancellationToken>()), Times.Once);

        audit.Verify(
            x => x.AppendAsync(
                It.Is<AuditRecordInput>(r =>
                    r.CorrelationId == action.Id &&
                    r.EventType == "AgentRunResumed" &&
                    r.EntityType == "action_execution" &&
                    r.EntityId == action.Id.ToString("N")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private static Orchestrator CreateOrchestrator(
        IClusterClient cluster,
        IAuditStore audit,
        IAgentGateway gateway,
        AIOps.Abstractions.Persistence.IActionExecutionStore? actionStoreOverride = null,
        AIOps.Abstractions.Persistence.IApprovalStore? approvalStoreOverride = null)
    {
        var toolRegistry = new Mock<AIOps.Abstractions.Tools.IToolRegistry>();
        toolRegistry.Setup(x => x.Describe())
            .Returns(Array.Empty<AIOps.Abstractions.Tools.ToolDescriptor>());

        var actionStore = actionStoreOverride ?? new Mock<AIOps.Abstractions.Persistence.IActionExecutionStore>().Object;
        var approvalStore = approvalStoreOverride ?? new Mock<AIOps.Abstractions.Persistence.IApprovalStore>().Object;
        var clock = new Mock<AIOps.Abstractions.Time.IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(DateTimeOffset.UtcNow);

        var validation = new AIOps.Orchestration.Agents.ToolProposalValidationService(
            toolRegistry.Object,
            actionStore,
            audit,
            clock.Object);
        var approvalService = new ApprovalService(
            actionStore,
            approvalStore,
            audit,
            clock.Object,
            Options.Create(new ApprovalOptions()));
        var approvalValidator = new Mock<IApprovalValidator>();
        var executor = new ToolExecutor(
            toolRegistry.Object,
            approvalValidator.Object,
            clock.Object);

        return new Orchestrator(
            cluster,
            audit,
            gateway,
            toolRegistry.Object,
            validation,
            actionStore,
            approvalStore,
            approvalService,
            executor,
            clock.Object,
            Options.Create(new AgentPolicyOptions()),
            NullLogger<Orchestrator>.Instance);
    }

    private sealed class ManifestTestTool(string schema) : ITool
    {
        public string Name => "Directory.ResetPassword";
        public string Description => "Reset a directory password.";
        public RiskLevel Risk => RiskLevel.Sensitive;
        public bool RequiresApproval => true;
        public string InputSchemaJson => schema;

        public Task<ToolResult> ExecuteAsync(
            string argumentsJson,
            ToolExecutionContext ctx,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Phase 12 must not execute tools.");
    }
}
