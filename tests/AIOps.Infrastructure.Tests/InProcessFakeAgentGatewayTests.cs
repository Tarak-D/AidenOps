using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Infrastructure.Agents;

namespace AIOps.Infrastructure.Tests;

public sealed class InProcessFakeAgentGatewayTests
{
    [Fact]
    public async Task Server_manifest_mode_returns_proposal_without_execution()
    {
        var gateway = new InProcessFakeAgentGateway();
        var request = new AgentRunRequest(
            Guid.NewGuid(),
            new AgentTicketContext(
                Guid.NewGuid(),
                "INC-12",
                "VPN connection failure",
                "User cannot connect to VPN.",
                "user@example.com",
                TicketDomain.Network,
                Severity.P2,
                TicketStatus.New),
            [new ToolManifestEntry(
                "Network.RunVpnDiagnostics",
                "Read-only VPN diagnostics.",
                RiskLevel.Safe,
                false,
                """
                {"type":"object","properties":{"userOrDeviceId":{"type":"string","minLength":1}},"required":["userOrDeviceId"],"additionalProperties":false}
                """)],
            0.6,
            2);

        var result = await gateway.StartRunAsync(request);

        Assert.Equal(AgentRunOutcome.ProposalCreated, result.Outcome);
        Assert.Equal("Network.RunVpnDiagnostics", result.Proposal!.ToolName);
        Assert.Equal(
            "{\"userOrDeviceId\":\"user@example.com\"}",
            result.Proposal.ArgumentsJson);
        Assert.Contains(result.Trace, step => step.Agent == "ToolProposalAgent");
    }
}
