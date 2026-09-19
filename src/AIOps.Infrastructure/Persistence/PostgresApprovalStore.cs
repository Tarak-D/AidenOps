using AIOps.Abstractions;
using AIOps.Abstractions.Persistence;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Infrastructure.EfCore;
using Microsoft.EntityFrameworkCore;

namespace AIOps.Infrastructure.Persistence;

public sealed class PostgresApprovalStore : IApprovalStore, IApprovalValidator
{
    private readonly AIOpsDbContext _context;

    public PostgresApprovalStore(AIOpsDbContext context)
    {
        _context = context;
    }

    public async Task AddAsync(
        ApprovalRequest approval,
        CancellationToken ct = default)
    {
        _context.ApprovalRequests.Add(approval);

        await _context.SaveChangesAsync(ct);
    }

    public Task<ApprovalRequest?> GetAsync(
        Guid approvalId,
        CancellationToken ct = default)
    {
        return _context.ApprovalRequests
            .SingleOrDefaultAsync(
                x => x.Id == approvalId,
                ct);
    }

    public Task<ApprovalRequest?> GetForActionExecutionAsync(
        Guid actionExecutionId,
        CancellationToken ct = default)
    {
        return _context.ApprovalRequests
            .SingleOrDefaultAsync(
                x => x.ActionExecutionId == actionExecutionId,
                ct);
    }

    public async Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
        CancellationToken ct = default)
    {
        return await _context.ApprovalRequests
            .Where(x => x.Status == ApprovalStatus.Pending)
            .OrderBy(x => x.CreatedAt)
            .ToListAsync(ct);
    }

    public async Task UpdateAsync(
        ApprovalRequest approval,
        CancellationToken ct = default)
    {
        _context.ApprovalRequests.Update(approval);

        await _context.SaveChangesAsync(ct);
    }

    async Task<ApprovalRequest?> IApprovalValidator
        .GetApprovalForActionExecutionAsync(
            Guid actionExecutionId,
            CancellationToken ct)
    {
        return await GetForActionExecutionAsync(
            actionExecutionId,
            ct);
    }
}