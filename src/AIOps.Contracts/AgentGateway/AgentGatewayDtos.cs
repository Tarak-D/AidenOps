using AIOps.Domain;

namespace AIOps.Contracts.AgentGateway;

// Wire contract between the .NET control plane and the Python/LangGraph AI service.
// Versioned under /api/v1 of the AI service; the pydantic mirror of these records
// lives in ai/aioops-agents/src/aioops_agents/schemas.py (added in Phase 5).
// See docs/architecture/agent-gateway-contract.md.

public sealed record ToolManifestEntry(
    string Name,
    string Description,
    RiskLevel Risk,
    bool RequiresApproval,
    string InputSchemaJson);

public sealed record AgentTicketContext(
    Guid TicketId,
    string ExternalRef,
    string Title,
    string Description,
    string ReporterEmail,
    TicketDomain Domain,
    Severity Severity,
    TicketStatus Status);

public sealed record AgentRunRequest(
    Guid CorrelationId,
    AgentTicketContext Ticket,
    IReadOnlyList<ToolManifestEntry> AllowedTools,
    double TriageConfidenceThreshold,
    int MaxAttempts);

public sealed record ToolProposal(
    string ToolName,
    string ArgumentsJson,
    double Confidence,
    string Justification);

public sealed record StepTrace(
    string Agent,
    string StepName,
    string Model,
    string PromptVersion,
    int TokensPrompt,
    int TokensCompletion,
    double LatencyMs,
    string Summary);

public enum AgentRunOutcome
{
    Resolved = 0,
    AwaitingApproval = 1,
    Escalated = 2,
    Failed = 3
}

public sealed record AgentRunResult(
    AgentRunOutcome Outcome,
    double? TriageConfidence,
    TicketDomain? Domain,
    Severity? Severity,
    ToolProposal? Proposal,
    string? EscalationSummary,
    string? ResolutionSummary,
    IReadOnlyList<StepTrace> Trace,
    string? Error);

public sealed record ResumeAgentRunRequest(
    Guid CorrelationId,
    Guid TicketId,
    bool ApprovalGranted,
    string? ApprovalDecidedBy,
    string? ToolResultJson,
    bool ToolExecutionSucceeded);
