using AIOps.Abstractions;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Tools;
using Xunit;

namespace AIOps.Tools.Tests;

public class ToolExecutorApprovalTests
{
    private static readonly Guid TicketId =
        Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static readonly DateTimeOffset Now =
        new(2026, 1, 1, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Approval_required_tool_is_blocked_without_approval()
    {
        var validator = new FakeApprovalValidator(null);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = CreateExecutor(
            tool,
            validator);

        var action = CreateAction();

        var result = await executor.ExecuteAsync(action);

        Assert.False(result.Success);

        Assert.Equal(
            "MissingApproval",
            result.Error);

        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task Approval_required_tool_is_blocked_when_approval_is_rejected()
    {
        var action = CreateAction();

        var approval = CreateApproval(
            action,
            ApprovalStatus.Rejected);

        var validator = new FakeApprovalValidator(
            approval);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = CreateExecutor(
            tool,
            validator);

        var result = await executor.ExecuteAsync(action);

        Assert.False(result.Success);

        Assert.Equal(
            "ApprovalNotApproved",
            result.Error);

        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task Approval_required_tool_is_blocked_when_approval_is_expired()
    {
        var action = CreateAction();

        var approval = CreateApproval(
            action,
            ApprovalStatus.Approved);

        var validator = new FakeApprovalValidator(
            approval);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = new ToolExecutor(
            new FakeToolRegistry(tool),
            validator,
            new FakeClock(
                approval.ExpiresAt.AddMinutes(1)));

        var result = await executor.ExecuteAsync(action);

        Assert.False(result.Success);

        Assert.Equal(
            "ApprovalExpired",
            result.Error);

        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task Approved_tool_receives_approval_id()
    {
        var action = CreateAction();

        var approval = CreateApproval(
            action,
            ApprovalStatus.Approved);

        var validator = new FakeApprovalValidator(
            approval);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = CreateExecutor(
            tool,
            validator);

        var result = await executor.ExecuteAsync(action);

        Assert.True(result.Success);
        Assert.True(tool.Executed);

        Assert.Equal(
            approval.Id,
            tool.Context?.ApprovalId);

        Assert.Equal(
            TicketId,
            tool.Context?.TicketId);

        Assert.Equal(
            action.Id,
            tool.Context?.CorrelationId);
    }

    [Fact]
    public async Task Approval_for_different_action_is_rejected()
    {
        var approvedAction = CreateAction();

        var approval = CreateApproval(
            approvedAction,
            ApprovalStatus.Approved);

        var validator = new FakeApprovalValidator(
            approval);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = CreateExecutor(
            tool,
            validator);

        var differentAction = CreateAction();

        var result = await executor.ExecuteAsync(
            differentAction);

        Assert.False(result.Success);

        Assert.Equal(
            "ApprovalMismatchActionExecution",
            result.Error);

        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task Approval_for_different_ticket_is_rejected()
    {
        var action = CreateAction();

        var approval = ApprovalRequest.Create(
            action.Id,
            Guid.Parse(
                "44444444-4444-4444-4444-444444444444"),
            "agent.remediation",
            "Restart unhealthy instance.",
            Now,
            TimeSpan.FromMinutes(15));

        approval.Decide(
            approve: true,
            decidedBy: "approver",
            comment: "Approved.",
            Now.AddMinutes(1));

        var validator = new FakeApprovalValidator(
            approval);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = CreateExecutor(
            tool,
            validator);

        var result = await executor.ExecuteAsync(
            action);

        Assert.False(result.Success);

        Assert.Equal(
            "ApprovalMismatchTicketId",
            result.Error);

        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task Approval_required_tool_is_blocked_when_approval_is_pending()
    {
        var action = CreateAction();

        var approval = ApprovalRequest.Create(
            action.Id,
            TicketId,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now,
            TimeSpan.FromMinutes(15));

        var validator = new FakeApprovalValidator(
            approval);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = CreateExecutor(
            tool,
            validator);

        var result = await executor.ExecuteAsync(action);

        Assert.False(result.Success);

        Assert.Equal(
            "ApprovalNotApproved",
            result.Error);

        Assert.False(tool.Executed);
    }

    [Fact]
    public async Task Safe_tool_does_not_require_approval()
    {
        var validator = new FakeApprovalValidator(null);

        var tool = new RecordingTool(
            "Cloud.GetInstanceStatus",
            RiskLevel.Safe);

        var executor = CreateExecutor(
            tool,
            validator);

        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.GetInstanceStatus",
            """{"instanceId":"i-123"}""",
            RiskLevel.Safe,
            "agent.diagnostics",
            "Check instance health.",
            Now);

        var result = await executor.ExecuteAsync(action);

        Assert.True(result.Success);
        Assert.True(tool.Executed);

        Assert.Null(
            tool.Context?.ApprovalId);

        Assert.Equal(
            TicketId,
            tool.Context?.TicketId);
    }

    [Fact]
    public async Task Risk_mismatch_blocks_execution()
    {
        var validator = new FakeApprovalValidator(null);

        var tool = new RecordingTool(
            "Cloud.RestartInstance",
            RiskLevel.Moderate);

        var executor = CreateExecutor(
            tool,
            validator);

        var action = ActionExecution.Propose(
            TicketId,
            "Cloud.RestartInstance",
            """{"instanceId":"i-123"}""",
            RiskLevel.Sensitive,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now);

        var result = await executor.ExecuteAsync(action);

        Assert.False(result.Success);

        Assert.Equal(
            "RiskMismatch",
            result.Error);

        Assert.False(tool.Executed);
    }

    private static ActionExecution CreateAction()
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

    private static ApprovalRequest CreateApproval(
        ActionExecution action,
        ApprovalStatus status)
    {
        var approval = ApprovalRequest.Create(
            action.Id,
            action.TicketId,
            "agent.remediation",
            "Restart unhealthy instance.",
            Now,
            TimeSpan.FromMinutes(15));

        if (status == ApprovalStatus.Approved)
        {
            approval.Decide(
                approve: true,
                decidedBy: "approver",
                comment: "Approved.",
                Now.AddMinutes(1));
        }
        else if (status == ApprovalStatus.Rejected)
        {
            approval.Decide(
                approve: false,
                decidedBy: "approver",
                comment: "Rejected.",
                Now.AddMinutes(1));
        }

        return approval;
    }

    private static ToolExecutor CreateExecutor(
        RecordingTool tool,
        FakeApprovalValidator validator)
    {
        return new ToolExecutor(
            new FakeToolRegistry(tool),
            validator,
            new FakeClock(Now));
    }

    private sealed class FakeClock : IClock
    {
        public FakeClock(DateTimeOffset utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTimeOffset UtcNow { get; }
    }

    private sealed class FakeApprovalValidator
        : IApprovalValidator
    {
        private readonly ApprovalRequest? _approval;

        public FakeApprovalValidator(
            ApprovalRequest? approval)
        {
            _approval = approval;
        }

        public Task<ApprovalRequest?>
            GetApprovalForActionExecutionAsync(
                Guid actionExecutionId,
                CancellationToken ct = default)
        {
            return Task.FromResult(_approval);
        }
    }

    private sealed class FakeToolRegistry
        : IToolRegistry
    {
        private readonly ITool _tool;

        public FakeToolRegistry(ITool tool)
        {
            _tool = tool;
        }

        public ITool? Get(string name)
        {
            return string.Equals(
                name,
                _tool.Name,
                StringComparison.OrdinalIgnoreCase)
                ? _tool
                : null;
        }

        public IReadOnlyList<ToolDescriptor> Describe()
        {
            return
            [
                new ToolDescriptor(
                    _tool.Name,
                    _tool.Description,
                    _tool.Risk,
                    _tool.RequiresApproval)
            ];
        }
    }

    private sealed class RecordingTool : ITool
    {
        public RecordingTool(
            string name,
            RiskLevel risk)
        {
            Name = name;
            Risk = risk;
        }

        public string Name { get; }

        public string Description =>
            "Test tool used for approval safety tests.";

        public RiskLevel Risk { get; }

        public bool Executed { get; private set; }

        public ToolExecutionContext? Context { get; private set; }

        public Task<ToolResult> ExecuteAsync(
            string argumentsJson,
            ToolExecutionContext ctx,
            CancellationToken ct = default)
        {
            Executed = true;
            Context = ctx;

            return Task.FromResult(
                new ToolResult(
                    true,
                    "Test tool executed."));
        }
    }
}