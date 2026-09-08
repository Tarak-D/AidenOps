using AIOps.Abstractions.Agents;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;

namespace AIOps.Infrastructure.Agents;

/// <summary>
/// Deterministic, offline stand-in for the Python/LangGraph AI service.
/// Lets .NET development, tests, CI and demos proceed without external AI dependencies.
/// Simple keyword heuristics classify tickets so the demo pipeline is reproducible.
/// </summary>
public sealed class InProcessFakeAgentGateway : IAgentGateway
{
    public Task<AgentRunResult> StartRunAsync(AgentRunRequest request, CancellationToken ct = default)
    {
        var (domain, severity, confidence) = Classify(request.Ticket.Title, request.Ticket.Description);
        var trace = new List<StepTrace>
        {
            new("TriageAgent", "triage", "fake/offline", "v0-fake", 0, 0, 1.0,
                $"Heuristic triage: domain={domain}, severity={severity}"),
            new("KnowledgeAgent", "knowledge", "fake/offline", "v0-fake", 0, 0, 1.0,
                "No-op retrieval in fake gateway")
        };

        if (confidence < request.TriageConfidenceThreshold)
        {
            trace.Add(new StepTrace("EscalationAgent", "escalate", "fake/offline", "v0-fake", 0, 0, 1.0,
                "Low triage confidence -> escalation"));
            return Task.FromResult(new AgentRunResult(
                AgentRunOutcome.Escalated, confidence, domain, severity, null,
                "Fake gateway: confidence below threshold; handing off to a human engineer.",
                null, trace, null));
        }

        var proposal = ChooseTool(request, domain);
        var outcome = proposal is not null && proposal.Confidence >= 0.5 &&
                      request.AllowedTools.FirstOrDefault(t => t.Name == proposal.ToolName) is { Risk: <= RiskLevel.Safe }
            ? AgentRunOutcome.Resolved
            : proposal is not null
                ? AgentRunOutcome.AwaitingApproval
                : AgentRunOutcome.Escalated;

        trace.Add(new StepTrace("ActionAgent", "propose", "fake/offline", "v0-fake", 0, 0, 1.0,
            proposal is null ? "No suitable action" : $"Proposed {proposal.ToolName}"));

        return Task.FromResult(new AgentRunResult(
            outcome, confidence, domain, severity,
            outcome == AgentRunOutcome.AwaitingApproval ? proposal : null,
            outcome == AgentRunOutcome.Escalated ? "Fake gateway: no suitable tool found." : null,
            outcome == AgentRunOutcome.Resolved ? "Fake gateway: safe simulated action completed." : null,
            trace, null));
    }

    public Task<AgentRunResult> ResumeRunAsync(ResumeAgentRunRequest request, CancellationToken ct = default)
    {
        var trace = new List<StepTrace>
        {
            new("VerificationAgent", "verify", "fake/offline", "v0-fake", 0, 0, 1.0,
                request.ToolExecutionSucceeded ? "Post-action verification passed." : "Post-action verification failed.")
        };
        var outcome = request.ApprovalGranted && request.ToolExecutionSucceeded
            ? AgentRunOutcome.Resolved
            : AgentRunOutcome.Escalated;
        return Task.FromResult(new AgentRunResult(
            outcome, null, null, null, null,
            outcome == AgentRunOutcome.Escalated ? "Fake gateway: approval rejected or action failed." : null,
            outcome == AgentRunOutcome.Resolved ? "Fake gateway: action approved, executed, and verified." : null,
            trace, null));
    }

    private static (TicketDomain, Severity, double) Classify(string title, string description)
    {
        var text = (title + " " + description).ToLowerInvariant();
        var domain =
            text.Contains("vpn") || text.Contains("network") || text.Contains("wifi") ? TicketDomain.Network :
            text.Contains("password") || text.Contains("login") || text.Contains("access") || text.Contains("locked") ? TicketDomain.Identity :
            text.Contains("database") || text.Contains("sql") || text.Contains("query") ? TicketDomain.Database :
            text.Contains("server") || text.Contains("disk") || text.Contains("cpu") || text.Contains("ec2") ? TicketDomain.Infrastructure :
            TicketDomain.Unknown;

        if (domain == TicketDomain.Unknown) return (domain, Severity.P3, 0.35);

        var severity =
            text.Contains("down") || text.Contains("outage") || text.Contains("production") ? Severity.P1 :
            text.Contains("urgent") || text.Contains("cannot work") ? Severity.P2 :
            Severity.P3;

        return (domain, severity, 0.85);
    }

    private static ToolProposal? ChooseTool(AgentRunRequest request, TicketDomain domain) => domain switch
    {
        TicketDomain.Identity when request.AllowedTools.Any(t => t.Name == "Identity.ResetPassword")
            => new ToolProposal("Identity.ResetPassword", "{}", 0.9, "Identity issue; password reset is the standard remedy."),
        TicketDomain.Network when request.AllowedTools.Any(t => t.Name == "Network.RunVpnDiagnostics")
            => new ToolProposal("Network.RunVpnDiagnostics", "{}", 0.9, "VPN/network issue; run diagnostics."),
        TicketDomain.Infrastructure when request.AllowedTools.Any(t => t.Name == "Aws.RestartEc2Instance")
            => new ToolProposal("Aws.RestartEc2Instance", "{}", 0.8, "Infrastructure issue; instance restart may help."),
        _ => null
    };
}
