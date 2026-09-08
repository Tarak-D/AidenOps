using AIOps.Domain;

namespace AIOps.Abstractions.Tools;

public sealed record ToolResult(bool Success, string Summary, string? DetailsJson = null, string? Error = null);

public sealed record ToolExecutionContext(
    Guid TicketId,
    Guid CorrelationId,
    string ActorId,
    /// <summary>Non-null only when execution was preceded by a valid approved approval.</summary>
    Guid? ApprovalId);

/// <summary>
/// A strongly typed, guardrailed action. AI agents may only *propose* tool calls;
/// execution goes through ToolExecutor which enforces input validation and the
/// approval-before-execution invariant (see docs/adr/0005).
/// All Phase 1-15 implementations are simulations; they never touch real infrastructure.
/// </summary>
public interface ITool
{
    string Name { get; }           // e.g. "Identity.ResetPassword"
    string Description { get; }
    RiskLevel Risk { get; }
    bool RequiresApproval => Risk >= RiskLevel.Moderate;
    Task<ToolResult> ExecuteAsync(string argumentsJson, ToolExecutionContext ctx, CancellationToken ct = default);
}

public sealed record ToolDescriptor(string Name, string Description, RiskLevel Risk, bool RequiresApproval);

public interface IToolRegistry
{
    ITool? Get(string name);
    IReadOnlyList<ToolDescriptor> Describe();
}
