using AIOps.Abstractions;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Domain;
using AIOps.Domain.Entities;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AIOps.Tools.Tests;

public sealed class ToolExecutorTests
{
    private sealed class FakeTool : ITool
    {
        public string Name { get; }

        public string Description { get; } = "Fake tool";

        public RiskLevel Risk { get; }

        public bool RequiresApproval => Risk >= RiskLevel.Moderate;

        public string? LastArgumentsJson { get; private set; }

        public ToolExecutionContext? LastContext { get; private set; }

        public bool WasExecuted { get; private set; }

        public FakeTool(string name, RiskLevel risk)
        {
            Name = name;
            Risk = risk;
        }

        public Task<ToolResult> ExecuteAsync(
            string argumentsJson,
            ToolExecutionContext ctx,
            CancellationToken ct = default)
        {
            LastArgumentsJson = argumentsJson;
            LastContext = ctx;
            WasExecuted = true;

            return Task.FromResult(
                new ToolResult(true, "Fake tool executed"));
        }
    }

    private sealed class FakeToolRegistry : IToolRegistry
    {
        private readonly Dictionary<string, ITool> _tools = new();

        public void RegisterTool(ITool tool)
        {
            _tools[tool.Name] = tool;
        }

        public ITool? Get(string name)
        {
            return _tools.TryGetValue(name, out var tool)
                ? tool
                : null;
        }

        public IReadOnlyList<ToolDescriptor> Describe()
        {
            throw new NotImplementedException();
        }
    }

    private sealed class FakeApprovalValidator : IApprovalValidator
    {
        private ApprovalRequest? _approval;

        public void SetApproval(ApprovalRequest approval)
        {
            _approval = approval;
        }

        public Task<ApprovalRequest?> GetApprovalForActionExecutionAsync(
            Guid actionExecutionId,
            CancellationToken ct = default)
        {
            return Task.FromResult(_approval);
        }
    }

    private sealed class FakeClock : IClock
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
    }

    private sealed class Fixture
    {
        public FakeToolRegistry ToolRegistry { get; } = new();

        public FakeApprovalValidator ApprovalValidator { get; } = new();

        public FakeClock Clock { get; } = new();

        public ToolExecutor Sut =>
            new ToolExecutor(
                ToolRegistry,
                ApprovalValidator,
                Clock);
    }

    private readonly Fixture _fixture;

    public ToolExecutorTests()
    {
        _fixture = new Fixture();
    }

    [Fact]
    public async Task SafeTool_ExecutesWithoutApproval()
    {
        var safeTool = new FakeTool(
            "Safe.Tool",
            RiskLevel.Safe);

        _fixture.ToolRegistry.RegisterTool(safeTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Safe.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Safe,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.True(result.Success);
        Assert.Equal("Fake tool executed", result.Summary);
        Assert.True(safeTool.WasExecuted);
        Assert.Equal("{}", safeTool.LastArgumentsJson);

        Assert.NotNull(safeTool.LastContext);
        Assert.Equal(action.TicketId, safeTool.LastContext!.TicketId);
        Assert.Equal(action.Id, safeTool.LastContext.CorrelationId);
        Assert.Equal(action.ProposedBy, safeTool.LastContext.ActorId);
        Assert.Null(safeTool.LastContext.ApprovalId);
    }

    [Fact]
    public async Task UnknownTool_IsRejected()
    {
        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Unknown.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Safe,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("not found", result.Summary);
        Assert.Equal("UnknownTool", result.Error);
    }

    [Fact]
    public async Task RiskMismatch_IsRejected()
    {
        var sensitiveTool = new FakeTool(
            "Sensitive.Tool",
            RiskLevel.Sensitive);

        _fixture.ToolRegistry.RegisterTool(sensitiveTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Sensitive.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Safe,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Equal("RiskMismatch", result.Error);
        Assert.False(sensitiveTool.WasExecuted);
    }

    [Fact]
    public async Task ModerateTool_WithoutApproval_IsRejected()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("No approval exists", result.Summary);
        Assert.Equal("MissingApproval", result.Error);
        Assert.False(moderateTool.WasExecuted);
    }

    [Fact]
    public async Task SensitiveTool_WithoutApproval_IsRejected()
    {
        var sensitiveTool = new FakeTool(
            "Sensitive.Tool",
            RiskLevel.Sensitive);

        _fixture.ToolRegistry.RegisterTool(sensitiveTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Sensitive.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Sensitive,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("No approval exists", result.Summary);
        Assert.Equal("MissingApproval", result.Error);
        Assert.False(sensitiveTool.WasExecuted);
    }

    [Fact]
    public async Task MissingServerSideApproval_IsRejected()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("No approval exists", result.Summary);
        Assert.Equal("MissingApproval", result.Error);
        Assert.False(moderateTool.WasExecuted);
    }

    [Fact]
    public async Task ApprovalForAnotherActionExecution_IsRejected()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var otherAction = ActionExecution.Propose(
            ticketId: action.TicketId,
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Different action",
            now: DateTimeOffset.UtcNow);

        var approval = ApprovalRequest.Create(
            actionExecutionId: otherAction.Id,
            ticketId: action.TicketId,
            requestedBy: "test-agent",
            justification: "Test",
            now: DateTimeOffset.UtcNow,
            ttl: TimeSpan.FromHours(1));

        approval.Decide(
            approve: true,
            decidedBy: "test-admin",
            comment: "Approved for different action",
            now: DateTimeOffset.UtcNow);

        _fixture.ApprovalValidator.SetApproval(approval);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains(
            "is not bound to action execution",
            result.Summary);
        Assert.Equal(
            "ApprovalMismatchActionExecution",
            result.Error);
        Assert.False(moderateTool.WasExecuted);
    }

    [Fact]
    public async Task ApprovalForAnotherTicketId_IsRejected()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var otherTicketId = Guid.NewGuid();

        var approval = ApprovalRequest.Create(
            actionExecutionId: action.Id,
            ticketId: otherTicketId,
            requestedBy: "test-agent",
            justification: "Test",
            now: DateTimeOffset.UtcNow,
            ttl: TimeSpan.FromHours(1));

        approval.Decide(
            approve: true,
            decidedBy: "test-admin",
            comment: "Approved",
            now: DateTimeOffset.UtcNow);

        _fixture.ApprovalValidator.SetApproval(approval);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("is for ticket", result.Summary);
        Assert.Equal("ApprovalMismatchTicketId", result.Error);
        Assert.False(moderateTool.WasExecuted);
    }

    [Fact]
    public async Task PendingApproval_IsRejected()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var approval = ApprovalRequest.Create(
            actionExecutionId: action.Id,
            ticketId: action.TicketId,
            requestedBy: "test-agent",
            justification: "Test",
            now: DateTimeOffset.UtcNow,
            ttl: TimeSpan.FromHours(1));

        _fixture.ApprovalValidator.SetApproval(approval);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("is not approved", result.Summary);
        Assert.Equal("ApprovalNotApproved", result.Error);
        Assert.False(moderateTool.WasExecuted);
    }

    [Fact]
    public async Task RejectedApproval_IsRejected()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var approval = ApprovalRequest.Create(
            actionExecutionId: action.Id,
            ticketId: action.TicketId,
            requestedBy: "test-agent",
            justification: "Test",
            now: DateTimeOffset.UtcNow,
            ttl: TimeSpan.FromHours(1));

        approval.Decide(
            approve: false,
            decidedBy: "test-admin",
            comment: "Rejected for test",
            now: DateTimeOffset.UtcNow);

        _fixture.ApprovalValidator.SetApproval(approval);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Contains("is not approved", result.Summary);
        Assert.Equal("ApprovalNotApproved", result.Error);
        Assert.False(moderateTool.WasExecuted);
    }

    [Fact]
    public async Task ExpiredApproval_IsRejected()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var expiredTime = DateTimeOffset.UtcNow - TimeSpan.FromHours(1);

        var approval = ApprovalRequest.Create(
            actionExecutionId: action.Id,
            ticketId: action.TicketId,
            requestedBy: "test-agent",
            justification: "Test",
            now: expiredTime,
            ttl: TimeSpan.FromMinutes(30));

        _fixture.ApprovalValidator.SetApproval(approval);

        _fixture.Clock.UtcNow = DateTimeOffset.UtcNow;

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.Equal("ApprovalNotApproved", result.Error);
        Assert.False(moderateTool.WasExecuted);
    }

    [Fact]
    public async Task ValidApprovedApproval_ExecutesSuccessfully()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var approval = ApprovalRequest.Create(
            actionExecutionId: action.Id,
            ticketId: action.TicketId,
            requestedBy: "test-agent",
            justification: "Test",
            now: DateTimeOffset.UtcNow,
            ttl: TimeSpan.FromHours(1));

        approval.Decide(
            approve: true,
            decidedBy: "test-admin",
            comment: "Approved for test",
            now: DateTimeOffset.UtcNow);

        _fixture.ApprovalValidator.SetApproval(approval);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.True(result.Success);
        Assert.Equal("Fake tool executed", result.Summary);
        Assert.True(moderateTool.WasExecuted);
        Assert.Equal("{}", moderateTool.LastArgumentsJson);

        Assert.NotNull(moderateTool.LastContext);
        Assert.Equal(action.TicketId, moderateTool.LastContext!.TicketId);
        Assert.Equal(action.Id, moderateTool.LastContext.CorrelationId);
        Assert.Equal(action.ProposedBy, moderateTool.LastContext.ActorId);
        Assert.Equal(
            approval.Id,
            moderateTool.LastContext.ApprovalId);
    }

    [Fact]
    public async Task ToolExecutionContext_IsPassedCorrectly_ForSafeTool()
    {
        var safeTool = new FakeTool(
            "Safe.Tool",
            RiskLevel.Safe);

        _fixture.ToolRegistry.RegisterTool(safeTool);

        var ticketId = Guid.NewGuid();
        var proposedBy = "test-agent";

        var action = ActionExecution.Propose(
            ticketId: ticketId,
            toolName: "Safe.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Safe,
            proposedBy: proposedBy,
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.True(result.Success);
        Assert.NotNull(safeTool.LastContext);
        Assert.Equal(ticketId, safeTool.LastContext!.TicketId);
        Assert.Equal(action.Id, safeTool.LastContext.CorrelationId);
        Assert.Equal(proposedBy, safeTool.LastContext.ActorId);
        Assert.Null(safeTool.LastContext.ApprovalId);
    }

    [Fact]
    public async Task ToolIsNeverCalledWhenValidationFails()
    {
        var moderateTool = new FakeTool(
            "Moderate.Tool",
            RiskLevel.Moderate);

        _fixture.ToolRegistry.RegisterTool(moderateTool);

        var action = ActionExecution.Propose(
            ticketId: Guid.NewGuid(),
            toolName: "Moderate.Tool",
            argumentsJson: "{}",
            risk: RiskLevel.Moderate,
            proposedBy: "test-agent",
            reason: "Testing",
            now: DateTimeOffset.UtcNow);

        var result = await _fixture.Sut.ExecuteAsync(action);

        Assert.False(result.Success);
        Assert.False(moderateTool.WasExecuted);
    }
}