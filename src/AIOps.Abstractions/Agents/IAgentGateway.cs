using AIOps.Contracts.AgentGateway;

namespace AIOps.Abstractions.Agents;

/// <summary>
/// Boundary between the .NET business control plane and the AI agent layer.
/// Implementations: InProcessFakeAgentGateway (dev/tests/CI, deterministic) and
/// HttpAgentGateway (Python/LangGraph service). The .NET side must never depend on
/// LangGraph specifics — only on this contract.
/// </summary>
public interface IAgentGateway
{
    Task<AgentRunResult> StartRunAsync(AgentRunRequest request, CancellationToken ct = default);
    Task<AgentRunResult> ResumeRunAsync(ResumeAgentRunRequest request, CancellationToken ct = default);
}
