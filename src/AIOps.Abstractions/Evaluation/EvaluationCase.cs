using AIOps.Contracts.AgentGateway;
using AIOps.Domain;

namespace AIOps.Abstractions.Evaluation;

public sealed record EvaluationCase(
    string Id,
    AgentTicketContext Ticket,
    IReadOnlyList<ToolManifestEntry> AllowedTools,
    TicketDomain ExpectedDomain,
    Severity ExpectedSeverity,
    AgentRunOutcome ExpectedInitialOutcome,
    string? ExpectedToolName,
    string ExpectedResolution,
    bool ExpectedApprovalRequired,
    double TriageConfidenceThreshold = 0.70,
    int MaxAttempts = 3);

public sealed record EvaluationDataset(
    string Name,
    string Version,
    IReadOnlyList<EvaluationCase> Cases);