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
}