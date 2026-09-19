using AIOps.Abstractions.Persistence;
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
}