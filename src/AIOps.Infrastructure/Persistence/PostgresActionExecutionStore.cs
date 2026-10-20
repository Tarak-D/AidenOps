using AIOps.Abstractions.Persistence;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Persistence;

public sealed class PostgresActionExecutionStore : IActionExecutionStore
{
    private readonly AIOpsDbContext _context;

    public PostgresActionExecutionStore(AIOpsDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        ActionExecution actionExecution,
        CancellationToken ct = default)
    {
        _context.ActionExecutions.Add(actionExecution);
        await _context.SaveChangesAsync(ct);
    }

    public Task<ActionExecution?> GetAsync(
        Guid actionExecutionId,
        CancellationToken ct = default)
    {
        return _context.ActionExecutions
            .SingleOrDefaultAsync(x => x.Id == actionExecutionId, ct);
    }

    public async Task UpdateAsync(
        ActionExecution actionExecution,
        CancellationToken ct = default)
    {
        _context.ActionExecutions.Update(actionExecution);
        await _context.SaveChangesAsync(ct);
    }

    public async Task<bool> TryBeginExecutionAsync(
        ActionExecution actionExecution,
        CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(actionExecution);

        var expectedStatus = actionExecution.Status;
        if ((actionExecution.RequiresApproval && expectedStatus != ActionStatus.Approved) ||
            (!actionExecution.RequiresApproval && expectedStatus != ActionStatus.Proposed))
        {
            return false;
        }

        var claimed = await _context.ActionExecutions
            .Where(action =>
                action.Id == actionExecution.Id &&
                action.Status == expectedStatus &&
                action.RequiresApproval == actionExecution.RequiresApproval)
            .ExecuteUpdateAsync(
                setters => setters.SetProperty(
                    action => action.Status,
                    ActionStatus.Executing),
                ct);

        if (claimed != 1)
        {
            return false;
        }

        actionExecution.BeginExecution();
        return true;
    }
}
