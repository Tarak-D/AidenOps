using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Abstractions.Audit;
using AIOps.Infrastructure.Audit;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Pgvector.EntityFrameworkCore;

namespace AIOps.Infrastructure.Tests.Persistence;

public sealed class PostgresActionApprovalStoreTests
{
    private const string ConnectionString =
        "Host=127.0.0.1;Port=5432;Database=aios;Username=aios;Password=aiospw";

    [Fact]
    public async Task Action_and_approval_records_round_trip_all_domain_statuses()
    {
        var actionIds = new List<Guid>();
        var approvalIds = new List<Guid>();
        var ticketId = Guid.NewGuid();

        try
        {
            await CleanupPreviousTestRows();
            await ApplyActionStatusConstraintUpdate();

            foreach (var status in Enum.GetValues<ActionStatus>())
            {
                var action = NewAction(ticketId);
                actionIds.Add(action.Id);
                SetStatus(action, status);

                await using (var writeContext = CreateContext())
                {
                    await new PostgresActionExecutionStore(writeContext)
                        .AddAsync(action);
                }

                await using var readContext = CreateContext();
                var loaded = await new PostgresActionExecutionStore(readContext)
                    .GetAsync(action.Id);

                Assert.NotNull(loaded);
                Assert.Equal(action.Id, loaded.Id);
                Assert.Equal(ticketId, loaded.TicketId);
                Assert.Equal(status, loaded.Status);
            }

            foreach (var status in Enum.GetValues<ApprovalStatus>())
            {
                var action = NewAction(ticketId, RiskLevel.Moderate);
                actionIds.Add(action.Id);
                var approval = ApprovalRequest.Create(
                    action.Id,
                    ticketId,
                    "postgres-store-test",
                    "PostgreSQL persistence round trip",
                    DateTimeOffset.UtcNow,
                    TimeSpan.FromMinutes(10));
                approvalIds.Add(approval.Id);
                SetStatus(approval, status);

                await using (var writeContext = CreateContext())
                {
                    await new PostgresActionExecutionStore(writeContext)
                        .AddAsync(action);
                    await new PostgresApprovalStore(writeContext)
                        .AddAsync(approval);
                }

                await using var readContext = CreateContext();
                var actionStore = new PostgresActionExecutionStore(readContext);
                var approvalStore = new PostgresApprovalStore(readContext);
                var loadedAction = await actionStore.GetAsync(action.Id);
                var loadedApproval = await approvalStore.GetAsync(approval.Id);
                var loadedForAction = await approvalStore
                    .GetForActionExecutionAsync(action.Id);

                Assert.NotNull(loadedAction);
                Assert.NotNull(loadedApproval);
                Assert.NotNull(loadedForAction);
                Assert.Equal(action.Id, loadedApproval.ActionExecutionId);
                Assert.Equal(ticketId, loadedApproval.TicketId);
                Assert.Equal(status, loadedApproval.Status);
                Assert.Equal(approval.Id, loadedForAction.Id);
            }

            var execution = NewAction(ticketId);
            actionIds.Add(execution.Id);
            await using (var writeContext = CreateContext())
            {
                var store = new PostgresActionExecutionStore(writeContext);
                await store.AddAsync(execution);
                execution.BeginExecution();
                execution.MarkSucceeded("persisted result", DateTimeOffset.UtcNow);
                await store.UpdateAsync(execution);
            }

            await using (var readContext = CreateContext())
            {
                var loaded = await new PostgresActionExecutionStore(readContext)
                    .GetAsync(execution.Id);
                Assert.NotNull(loaded);
                Assert.Equal(ActionStatus.Succeeded, loaded.Status);
                Assert.Equal("persisted result", loaded.ResultSummary);
                Assert.NotNull(loaded.ExecutedAt);
            }

            var failedExecution = NewAction(ticketId);
            actionIds.Add(failedExecution.Id);
            await using (var writeContext = CreateContext())
            {
                var store = new PostgresActionExecutionStore(writeContext);
                await store.AddAsync(failedExecution);
                failedExecution.BeginExecution();
                failedExecution.MarkFailed("persisted failure", DateTimeOffset.UtcNow);
                await store.UpdateAsync(failedExecution);
            }

            await using var failureReadContext = CreateContext();
            var loadedFailure = await new PostgresActionExecutionStore(failureReadContext)
                .GetAsync(failedExecution.Id);
            Assert.NotNull(loadedFailure);
            Assert.Equal(ActionStatus.Failed, loadedFailure.Status);
            Assert.Equal("persisted failure", loadedFailure.ResultSummary);
            Assert.NotNull(loadedFailure.ExecutedAt);
        }
        finally
        {
            await using var cleanup = CreateContext();
            var approvals = await cleanup.ApprovalRequests
                .Where(x => approvalIds.Contains(x.Id))
                .ToListAsync();
            cleanup.ApprovalRequests.RemoveRange(approvals);
            await cleanup.SaveChangesAsync();

            var actions = await cleanup.ActionExecutions
                .Where(x => actionIds.Contains(x.Id))
                .ToListAsync();
            cleanup.ActionExecutions.RemoveRange(actions);
            await cleanup.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task Approval_decision_and_audit_event_round_trip()
    {
        var action = NewAction(Guid.NewGuid(), RiskLevel.Moderate);
        var approval = ApprovalRequest.Create(
            action.Id,
            action.TicketId,
            "postgres-store-test",
            "Approval decision persistence round trip",
            DateTimeOffset.UtcNow,
            TimeSpan.FromMinutes(10));

        try
        {
            await CleanupPreviousTestRows();
            await using (var writeContext = CreateContext())
            {
                await new PostgresActionExecutionStore(writeContext).AddAsync(action);
                var store = new PostgresApprovalStore(writeContext);
                await store.AddAsync(approval);

                var loaded = await store.GetAsync(approval.Id);
                Assert.NotNull(loaded);
                loaded.Decide(true, "postgres-approver", "approved", DateTimeOffset.UtcNow);
                await store.UpdateAsync(loaded);
            }

            await using (var readContext = CreateContext())
            {
                var reloaded = await new PostgresApprovalStore(readContext)
                    .GetAsync(approval.Id);
                Assert.NotNull(reloaded);
                Assert.Equal(ApprovalStatus.Approved, reloaded.Status);
                Assert.Equal("postgres-approver", reloaded.DecidedBy);
                Assert.Equal("approved", reloaded.DecisionComment);
                Assert.Equal(action.Id, reloaded.ActionExecutionId);
            }

            var auditEntityId = $"postgres-store-test-{Guid.NewGuid():N}";
            await using (var auditContext = CreateContext())
            {
                var auditStore = new PostgresAuditStore(auditContext);
                await auditStore.AppendAsync(new AuditRecordInput(
                    Guid.NewGuid(),
                    ActorKind.System,
                    "postgres-store-test",
                    "PersistenceVerified",
                    "ActionExecution",
                    auditEntityId,
                    "{}"));

                var audit = Assert.Single(await auditStore.QueryAsync(
                    entityType: "ActionExecution",
                    entityId: auditEntityId));
                Assert.Equal("PersistenceVerified", audit.Record.EventType);
                Assert.Equal(auditEntityId, audit.Record.EntityId);
            }
        }
        finally
        {
            await using var cleanup = CreateContext();
            var persistedApproval = await cleanup.ApprovalRequests
                .SingleOrDefaultAsync(x => x.Id == approval.Id);
            if (persistedApproval is not null)
            {
                cleanup.ApprovalRequests.Remove(persistedApproval);
                await cleanup.SaveChangesAsync();
            }

            var persistedAction = await cleanup.ActionExecutions
                .SingleOrDefaultAsync(x => x.Id == action.Id);
            if (persistedAction is not null)
            {
                cleanup.ActionExecutions.Remove(persistedAction);
                await cleanup.SaveChangesAsync();
            }
        }
    }

    private static AIOpsDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AIOpsDbContext>()
            .UseNpgsql(ConnectionString, npgsqlOptions => npgsqlOptions.UseVector())
            .Options;
        return new AIOpsDbContext(options);
    }

    private static async Task ApplyActionStatusConstraintUpdate()
    {
        await using var context = CreateContext();
        var sql = """
            ALTER TABLE action_executions
                DROP CONSTRAINT IF EXISTS ck_action_executions_status;
            ALTER TABLE action_executions
                ADD CONSTRAINT ck_action_executions_status
                CHECK (status BETWEEN 0 AND 7);
            """;
        await context.Database.ExecuteSqlRawAsync(sql);
    }

    private static async Task CleanupPreviousTestRows()
    {
        await using var context = CreateContext();
        var approvals = await context.ApprovalRequests
            .Where(x => x.RequestedBy == "postgres-store-test")
            .ToListAsync();
        context.ApprovalRequests.RemoveRange(approvals);
        await context.SaveChangesAsync();

        var actions = await context.ActionExecutions
            .Where(x => x.ProposedBy == "postgres-store-test" &&
                        x.ToolName == "postgres-store-test")
            .ToListAsync();
        context.ActionExecutions.RemoveRange(actions);
        await context.SaveChangesAsync();
    }

    private static ActionExecution NewAction(
        Guid ticketId,
        RiskLevel risk = RiskLevel.Safe) =>
        ActionExecution.Propose(
            ticketId,
            "postgres-store-test",
            "{}",
            risk,
            "postgres-store-test",
            "Persistence round trip",
            DateTimeOffset.UtcNow);

    private static void SetStatus(ActionExecution action, ActionStatus status) =>
        typeof(ActionExecution)
            .GetProperty(nameof(ActionExecution.Status))!
            .SetValue(action, status);

    private static void SetStatus(ApprovalRequest approval, ApprovalStatus status) =>
        typeof(ApprovalRequest)
            .GetProperty(nameof(ApprovalRequest.Status))!
            .SetValue(approval, status);
}
