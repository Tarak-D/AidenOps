using AIOps.Domain.Entities;

namespace AIOps.Abstractions.Persistence;

public interface IApprovalStore
{
    Task AddAsync(
        ApprovalRequest approval,
        CancellationToken ct = default);

    Task<ApprovalRequest?> GetAsync(
        Guid approvalId,
        CancellationToken ct = default);

    Task<ApprovalRequest?> GetForActionExecutionAsync(
        Guid actionExecutionId,
        CancellationToken ct = default);

    Task UpdateAsync(
        ApprovalRequest approval,
        CancellationToken ct = default);

    Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
        CancellationToken ct = default);
}