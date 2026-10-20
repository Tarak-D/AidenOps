using AIOps.Domain.Entities;

namespace AIOps.Abstractions.Persistence;

public interface IActionExecutionStore
{
    Task AddAsync(
        ActionExecution actionExecution,
        CancellationToken ct = default);

    Task<ActionExecution?> GetAsync(
        Guid actionExecutionId,
        CancellationToken ct = default);

    Task UpdateAsync(
        ActionExecution actionExecution,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically claims a proposed/saved approved action for its single execution
    /// attempt and transitions the supplied entity to Executing.
    /// </summary>
    Task<bool> TryBeginExecutionAsync(
        ActionExecution actionExecution,
        CancellationToken ct = default);
}
