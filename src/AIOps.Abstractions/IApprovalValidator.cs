using AIOps.Domain.Entities;
using System.Threading;
using System.Threading.Tasks;

namespace AIOps.Abstractions;

/// <summary>
/// Server-side approval source for validating approval requests before tool execution.
/// </summary>
public interface IApprovalValidator
{
    /// <summary>
    /// Attempts to retrieve the approval request bound to the given action execution.
    /// Returns null if no such approval exists or cannot be loaded.
    /// </summary>
    Task<ApprovalRequest?> GetApprovalForActionExecutionAsync(Guid actionExecutionId, CancellationToken ct = default);
}