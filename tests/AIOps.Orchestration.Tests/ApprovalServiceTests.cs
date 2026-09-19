using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Orchestration.Approvals;
using Microsoft.Extensions.Options;
using Xunit;

namespace AIOps.Orchestration.Tests;

public class ApprovalServiceTests
{
    private static readonly Guid TicketId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Create_moves_action_to_awaiting_approval()
    {
        var action = CreateModerateAction();

        var actionStore = new FakeActionExecutionStore(action);
        var approvalStore = new FakeApprovalStore();
        var auditStore = new FakeAuditStore();

        var service = CreateService(
            actionStore,
            approvalStore,
            auditStore);

        var result = await service.CreateAsync(
            action.Id,
            "agent.remediation",
            "Restart unhealthy instance.");

        Assert.Equal(
            ApprovalStatus.Pending,
            result.Status);

        Assert.Equal(
            ActionStatus.AwaitingApproval,
            action.Status);

        Assert.Equal(
            action.Id,
            result.ActionExecutionId);

        Assert.Single(
            auditStore.Records);

        Assert.Equal(
            "ApprovalRequested",
            auditStore.Records[0].EventType);
    }

    [Fact]
    public async Task Create_rejects_action_that_does_not_require_approval()
    {
        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.GetInstanceStatus",
            """{"instanceId":"i-123"}""",
            RiskLevel.Safe,
            "agent.diagnostics",
            "Check instance health.",
            Now);

        var service = CreateService(
            new FakeActionExecutionStore(action),
            new FakeApprovalStore(),
            new FakeAuditStore());

        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => service.CreateAsync(
                action.Id,
                "agent.remediation",
                "This should not require approval."));
    }

    [Fact]
    public async Task Create_rejects_duplicate_approval_request()
    {
        var action = CreateModerateAction();

        var actionStore = new FakeActionExecutionStore(action);
        var approvalStore = new FakeApprovalStore();

        var service = CreateService(
            actionStore,
            approvalStore,
            new FakeAuditStore());

        await service.CreateAsync(
            action.Id,
            "agent.remediation",
            "Restart unhealthy instance.");

        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => service.CreateAsync(
                action.Id,
                "agent.remediation",
                "Create a second approval."));
    }

    [Fact]
    public async Task Approve_moves_action_to_approved()
    {
        var action = CreateModerateAction();

        var actionStore = new FakeActionExecutionStore(action);
        var approvalStore = new FakeApprovalStore();

        var service = CreateService(
            actionStore,
            approvalStore,
            new FakeAuditStore());

        var created = await service.CreateAsync(
            action.Id,
            "agent.remediation",
            "Restart unhealthy instance.");

        var result = await service.DecideAsync(
            created.Id,
            approve: true,
            "approver",
            "Approved after review.");

        Assert.Equal(
            ApprovalStatus.Approved,
            result.Status);

        Assert.Equal(
            ActionStatus.Approved,
            action.Status);

        Assert.Equal(
            "approver",
            result.DecidedBy);
    }

    [Fact]
    public async Task Reject_moves_action_to_rejected()
    {
        var action = CreateModerateAction();

        var actionStore = new FakeActionExecutionStore(action);
        var approvalStore = new FakeApprovalStore();

        var service = CreateService(
            actionStore,
            approvalStore,
            new FakeAuditStore());

        var created = await service.CreateAsync(
            action.Id,
            "agent.remediation",
            "Restart unhealthy instance.");

        var result = await service.DecideAsync(
            created.Id,
            approve: false,
            "approver",
            "Do not restart the instance.");

        Assert.Equal(
            ApprovalStatus.Rejected,
            result.Status);

        Assert.Equal(
            ActionStatus.Rejected,
            action.Status);

        Assert.Equal(
            "Do not restart the instance.",
            action.ResultSummary);
    }

    [Fact]
    public async Task Expired_approval_cannot_be_approved()
    {
        var action = CreateModerateAction();

        var actionStore = new FakeActionExecutionStore(action);
        var approvalStore = new FakeApprovalStore();

        var clock = new FakeClock(
            Now.AddHours(25));

        var service = CreateService(
            actionStore,
            approvalStore,
            new FakeAuditStore(),
            clock);

        var approval = ApprovalRequest.Create(
            action.Id,
            action.TicketId,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now,
            TimeSpan.FromHours(24));

        approvalStore.AddExisting(approval);

        action.MarkAwaitingApproval();

        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => service.DecideAsync(
                approval.Id,
                approve: true,
                "approver",
                "Too late."));

        Assert.Equal(
            ApprovalStatus.Expired,
            approval.Status);

        Assert.Equal(
            ActionStatus.AwaitingApproval,
            action.Status);
    }

    [Fact]
    public async Task Decide_rejects_mismatched_ticket()
    {
        var action = CreateModerateAction();

        var actionStore = new FakeActionExecutionStore(action);
        var approvalStore = new FakeApprovalStore();

        var approval = ApprovalRequest.Create(
            action.Id,
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            "agent.remediation",
            "Restart unhealthy instance.",
            Now,
            TimeSpan.FromHours(24));

        approvalStore.AddExisting(approval);

        var service = CreateService(
            actionStore,
            approvalStore,
            new FakeAuditStore());

        await Assert.ThrowsAsync<DomainInvariantViolationException>(
            () => service.DecideAsync(
                approval.Id,
                approve: true,
                "approver",
                "Approved."));
    }

    private static ActionExecution CreateModerateAction()
    {
        return ActionExecution.Propose(
            TicketId,
            "Cloud.RestartInstance",
            """{"instanceId":"i-123"}""",
            RiskLevel.Moderate,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now);
    }

    private static ApprovalService CreateService(
        FakeActionExecutionStore actionStore,
        FakeApprovalStore approvalStore,
        FakeAuditStore auditStore,
        FakeClock? clock = null)
    {
        return new ApprovalService(
            actionStore,
            approvalStore,
            auditStore,
            clock ?? new FakeClock(Now),
            Options.Create(
                new AIOps.Abstractions.Configuration.ApprovalOptions
                {
                    DefaultTtl = TimeSpan.FromHours(24)
                }));
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeActionExecutionStore
        : IActionExecutionStore
    {
        private readonly Dictionary<Guid, ActionExecution> _items = new();

        public FakeActionExecutionStore(ActionExecution action)
        {
            _items[action.Id] = action;
        }

        public Task AddAsync(
            ActionExecution actionExecution,
            CancellationToken ct = default)
        {
            _items[actionExecution.Id] = actionExecution;
            return Task.CompletedTask;
        }

        public Task<ActionExecution?> GetAsync(
            Guid actionExecutionId,
            CancellationToken ct = default)
        {
            _items.TryGetValue(
                actionExecutionId,
                out var action);

            return Task.FromResult(action);
        }

        public Task UpdateAsync(
            ActionExecution actionExecution,
            CancellationToken ct = default)
        {
            _items[actionExecution.Id] = actionExecution;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeApprovalStore
        : IApprovalStore
    {
        private readonly Dictionary<Guid, ApprovalRequest> _items = new();

        public void AddExisting(
            ApprovalRequest approval)
        {
            _items[approval.Id] = approval;
        }

        public Task AddAsync(
            ApprovalRequest approval,
            CancellationToken ct = default)
        {
            _items[approval.Id] = approval;
            return Task.CompletedTask;
        }

        public Task<ApprovalRequest?> GetAsync(
            Guid approvalId,
            CancellationToken ct = default)
        {
            _items.TryGetValue(
                approvalId,
                out var approval);

            return Task.FromResult(approval);
        }

        public Task<ApprovalRequest?> GetForActionExecutionAsync(
            Guid actionExecutionId,
            CancellationToken ct = default)
        {
            var approval = _items.Values
                .SingleOrDefault(
                    x => x.ActionExecutionId == actionExecutionId);

            return Task.FromResult(approval);
        }

        public Task<IReadOnlyList<ApprovalRequest>> ListPendingAsync(
            CancellationToken ct = default)
        {
            IReadOnlyList<ApprovalRequest> result = _items.Values
                .Where(x => x.Status == ApprovalStatus.Pending)
                .ToList();

            return Task.FromResult(result);
        }

        public Task UpdateAsync(
            ApprovalRequest approval,
            CancellationToken ct = default)
        {
            _items[approval.Id] = approval;
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAuditStore : IAuditStore
    {
        public List<AuditRecordInput> Records { get; } = [];

        public Task AppendAsync(
            AuditRecordInput record,
            CancellationToken ct = default)
        {
            Records.Add(record);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<AuditRecordView>> QueryAsync(
            string? entityType = null,
            string? entityId = null,
            string? actorId = null,
            int take = 100,
            CancellationToken ct = default)
        {
            IReadOnlyList<AuditRecordView> result = [];
            return Task.FromResult(result);
        }
    }
}