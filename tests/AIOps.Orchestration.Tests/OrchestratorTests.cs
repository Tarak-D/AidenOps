using AIOps.Infrastructure.Agents;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Grains;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Orchestration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Orleans;

namespace AIOps.Orchestration.Tests;

public sealed class OrchestratorTests
{
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
            .Setup(x => x.StartRunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()))
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

        var orchestrator = new Orchestrator(
            cluster.Object,
            audit.Object,
            gateway.Object,
            NullLogger<Orchestrator>.Instance);

        var result = await orchestrator.StartAgentRunAsync(ticketId, request);

        Assert.Equal(AgentRunOutcome.AwaitingApproval, result.Outcome);
        gateway.Verify(
            x => x.StartRunAsync(request, It.IsAny<CancellationToken>()),
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
            .ReturnsAsync(new TicketState { Exists = false });

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

        var orchestrator = new Orchestrator(
            cluster.Object,
            audit.Object,
            gateway.Object,
            NullLogger<Orchestrator>.Instance);

        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => orchestrator.StartAgentRunAsync(ticketId, request));

        gateway.Verify(
            x => x.StartRunAsync(It.IsAny<AgentRunRequest>(), It.IsAny<CancellationToken>()),
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
            .Setup(x => x.ResumeRunAsync(It.IsAny<ResumeAgentRunRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedResult);

        var audit = new Mock<IAuditStore>();

        var request = new ResumeAgentRunRequest(
            correlationId,
            ticketId,
            true,
            "operator-1",
            """{"success":true}""",
            true);

        var orchestrator = new Orchestrator(
            cluster.Object,
            audit.Object,
            gateway.Object,
            NullLogger<Orchestrator>.Instance);

        var result = await orchestrator.ResumeAgentRunAsync(request);

        Assert.Equal(AgentRunOutcome.Resolved, result.Outcome);

        gateway.Verify(
            x => x.ResumeRunAsync(request, It.IsAny<CancellationToken>()),
            Times.Once);

        audit.Verify(
            x => x.AppendAsync(
                It.Is<AuditRecordInput>(r =>
                    r.CorrelationId == correlationId &&
                    r.EventType == "AgentRunResumed" &&
                    r.EntityType == "ticket" &&
                    r.EntityId == ticketId.ToString("N")),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

}
