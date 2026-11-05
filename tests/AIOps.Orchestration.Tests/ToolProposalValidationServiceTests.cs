using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Domain.Entities;
using AIOps.Orchestration.Agents;
using Moq;

namespace AIOps.Orchestration.Tests;

public sealed class ToolProposalValidationServiceTests
{
    private static readonly Guid TicketId = Guid.NewGuid();
    private static readonly Guid CorrelationId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = DateTimeOffset.UtcNow;

    [Fact]
    public async Task Validates_proposal_and_classifies_it_from_registered_tool_metadata()
    {
        var tool = new TestTool("Directory.ResetPassword", RiskLevel.Sensitive);
        var registry = new TestToolRegistry([tool]);
        ActionExecution? savedAction = null;
        var actionStore = new Mock<IActionExecutionStore>();
        actionStore.Setup(x => x.AddAsync(It.IsAny<ActionExecution>(), It.IsAny<CancellationToken>()))
            .Callback<ActionExecution, CancellationToken>((action, _) => savedAction = action)
            .Returns(Task.CompletedTask);
        var audit = new Mock<IAuditStore>();
        AuditRecordInput? auditRecord = null;
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditRecordInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecordInput, CancellationToken>((record, _) => auditRecord = record)
            .Returns(Task.CompletedTask);

        var result = await CreateService(registry, actionStore, audit)
            .ValidateAndPersistAsync(
                TicketId,
                CorrelationId,
                CreateResult(new ToolProposal(
                    tool.Name,
                    "{\"userPrincipalName\":\"user@example.com\"}",
                    0.88,
                    "The ticket reports a password reset need.",
                    RiskLevel.Safe,
                    false)));

        Assert.Equal(AgentRunOutcome.ProposalCreated, result.Outcome);
        Assert.NotNull(savedAction);
        Assert.Equal(RiskLevel.Sensitive, savedAction!.Risk);
        Assert.True(savedAction.RequiresApproval);
        Assert.Equal(ActionStatus.Proposed, savedAction.Status);
        Assert.Equal(ToolProposalValidationServiceActor, savedAction.ProposedBy);
        Assert.Equal(RiskLevel.Sensitive, result.Proposal!.Risk);
        Assert.True(result.Proposal.RequiresApproval);
        Assert.Equal(savedAction.Id, result.Proposal.ActionExecutionId);
        Assert.Equal("ToolProposalValidated", auditRecord!.EventType);
        Assert.Equal("action_execution", auditRecord.EntityType);
    }

    [Theory]
    [InlineData("{\"userOrDeviceId\":\"user@example.com\"}")]
    [InlineData("{\"target\":\"user@example.com\",\"targetType\":\"User\"}")]
    public async Task Validates_proposal_against_oneOf_object_schemas(
        string argumentsJson)
    {
        const string inputSchema = """
            {"oneOf":[{"type":"object","properties":{"target":{"type":"string","minLength":1},"targetType":{"type":"string","enum":["User","Device","Ip"]}},"required":["target","targetType"],"additionalProperties":false},{"type":"object","properties":{"userOrDeviceId":{"type":"string","minLength":1}},"required":["userOrDeviceId"],"additionalProperties":false}]}
            """;
        var tool = new TestTool(
            "Network.RunVpnDiagnostics",
            RiskLevel.Safe,
            inputSchema);
        var actionStore = new Mock<IActionExecutionStore>();
        actionStore.Setup(x => x.AddAsync(
                It.IsAny<ActionExecution>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var audit = new Mock<IAuditStore>();
        audit.Setup(x => x.AppendAsync(
                It.IsAny<AuditRecordInput>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await CreateService(
                new TestToolRegistry([tool]),
                actionStore,
                audit)
            .ValidateAndPersistAsync(
                TicketId,
                CorrelationId,
                CreateResult(new ToolProposal(
                    tool.Name,
                    argumentsJson,
                    0.9,
                    "A VPN connectivity issue requires a read-only diagnostic.")));

        Assert.Equal(AgentRunOutcome.ProposalCreated, result.Outcome);
        Assert.Equal(tool.Name, result.Proposal?.ToolName);
        actionStore.Verify(
            x => x.AddAsync(It.IsAny<ActionExecution>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Theory]
    [InlineData(
        "{\"target\":\"user@example.com\"}",
        "Required tool argument 'targetType' is missing.")]
    [InlineData(
        "{\"target\":\"user@example.com\",\"targetType\":\"Other\"}",
        "not one of the registered values")]
    public async Task Rejects_proposal_that_does_not_match_a_oneOf_object_schema(
        string argumentsJson,
        string expectedError)
    {
        const string inputSchema = """
            {"oneOf":[{"type":"object","properties":{"target":{"type":"string","minLength":1},"targetType":{"type":"string","enum":["User","Device","Ip"]}},"required":["target","targetType"],"additionalProperties":false},{"type":"object","properties":{"userOrDeviceId":{"type":"string","minLength":1}},"required":["userOrDeviceId"],"additionalProperties":false}]}
            """;
        var tool = new TestTool(
            "Network.RunVpnDiagnostics",
            RiskLevel.Safe,
            inputSchema);
        var actionStore = new Mock<IActionExecutionStore>();
        var audit = new Mock<IAuditStore>();

        var result = await CreateService(
                new TestToolRegistry([tool]),
                actionStore,
                audit)
            .ValidateAndPersistAsync(
                TicketId,
                CorrelationId,
                CreateResult(new ToolProposal(
                    tool.Name,
                    argumentsJson,
                    0.9,
                    "A VPN connectivity issue requires a read-only diagnostic.")));

        Assert.Equal(AgentRunOutcome.Escalated, result.Outcome);
        Assert.Contains(expectedError, result.Error);
        actionStore.Verify(
            x => x.AddAsync(It.IsAny<ActionExecution>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task ProposalCreated_without_proposal_is_escalated_and_audited()
    {
        var registry = new TestToolRegistry([]);
        var actionStore = new Mock<IActionExecutionStore>();
        var audit = new Mock<IAuditStore>();
        AuditRecordInput? auditRecord = null;
        audit.Setup(x => x.AppendAsync(It.IsAny<AuditRecordInput>(), It.IsAny<CancellationToken>()))
            .Callback<AuditRecordInput, CancellationToken>((record, _) => auditRecord = record)
            .Returns(Task.CompletedTask);

        var result = await CreateService(registry, actionStore, audit)
            .ValidateAndPersistAsync(
                TicketId,
                CorrelationId,
                new AgentRunResult(
                    AgentRunOutcome.ProposalCreated,
                    0.9,
                    TicketDomain.Identity,
                    Severity.P2,
                    null,
                    null,
                    null,
                    Array.Empty<StepTrace>(),
                    null));

        Assert.Equal(AgentRunOutcome.Escalated, result.Outcome);
        Assert.Null(result.Proposal);
        Assert.Contains("without providing one", result.Error);
        Assert.Equal("ToolProposalRejected", auditRecord!.EventType);
        Assert.Equal("ticket", auditRecord.EntityType);
        actionStore.Verify(
            x => x.AddAsync(It.IsAny<ActionExecution>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Theory]
    [InlineData("Directory.Unknown", "{}", "not registered")]
    [InlineData("Directory.ResetPassword", "{}", "Required tool argument")]
    [InlineData("Directory.ResetPassword", "{\"userPrincipalName\":\"u@example.com\",\"extra\":true}", "Unexpected tool argument")]
    [InlineData("Directory.ResetPassword", "{\"userPrincipalName\":42}", "must be of type string")]
    [InlineData("Directory.ResetPassword", "not-json", "valid JSON")]
    public async Task Invalid_proposal_is_escalated_without_persisting_or_executing(
        string toolName,
        string argumentsJson,
        string expectedError)
    {
        var registry = new TestToolRegistry(
        [
            new TestTool("Directory.ResetPassword", RiskLevel.Sensitive)
        ]);
        var actionStore = new Mock<IActionExecutionStore>();
        var audit = new Mock<IAuditStore>();

        var result = await CreateService(registry, actionStore, audit)
            .ValidateAndPersistAsync(
                TicketId,
                CorrelationId,
                CreateResult(new ToolProposal(
                    toolName,
                    argumentsJson,
                    0.9,
                    "A test proposal.")));

        Assert.Equal(AgentRunOutcome.Escalated, result.Outcome);
        Assert.Null(result.Proposal);
        Assert.Contains(expectedError, result.Error);
        actionStore.Verify(
            x => x.AddAsync(It.IsAny<ActionExecution>(), It.IsAny<CancellationToken>()),
            Times.Never);
        audit.Verify(
            x => x.AppendAsync(
                It.Is<AuditRecordInput>(record => record.EventType == "ToolProposalRejected"),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    private const string ToolProposalValidationServiceActor = "ToolProposalAgent";

    private static ToolProposalValidationService CreateService(
        IToolRegistry registry,
        Mock<IActionExecutionStore> actionStore,
        Mock<IAuditStore> audit)
    {
        var clock = new Mock<IClock>();
        clock.SetupGet(x => x.UtcNow).Returns(Now);
        return new ToolProposalValidationService(
            registry,
            actionStore.Object,
            audit.Object,
            clock.Object);
    }

    private static AgentRunResult CreateResult(ToolProposal proposal) => new(
        AgentRunOutcome.ProposalCreated,
        0.9,
        TicketDomain.Identity,
        Severity.P2,
        proposal,
        null,
        null,
        Array.Empty<StepTrace>(),
        null);

    private sealed class TestTool : ITool
    {
        public TestTool(string name, RiskLevel risk, string? inputSchemaJson = null)
        {
            Name = name;
            Risk = risk;
            InputSchemaJson = inputSchemaJson ?? """
                {"type":"object","properties":{"userPrincipalName":{"type":"string","minLength":1}},"required":["userPrincipalName"],"additionalProperties":false}
                """;
        }

        public string Name { get; }
        public string Description => "Test proposal tool.";
        public RiskLevel Risk { get; }
        public bool RequiresApproval => true;
        public string InputSchemaJson { get; }

        public Task<ToolResult> ExecuteAsync(
            string argumentsJson,
            ToolExecutionContext ctx,
            CancellationToken ct = default) =>
            throw new InvalidOperationException("Proposal validation must never execute a tool.");
    }

    private sealed class TestToolRegistry(IEnumerable<ITool> tools) : IToolRegistry
    {
        private readonly IReadOnlyDictionary<string, ITool> _tools =
            tools.ToDictionary(tool => tool.Name, StringComparer.Ordinal);

        public ITool? Get(string name) =>
            _tools.TryGetValue(name, out var tool) ? tool : null;

        public IReadOnlyList<ToolDescriptor> Describe() =>
            _tools.Values.Select(tool => new ToolDescriptor(
                tool.Name,
                tool.Description,
                tool.Risk,
                tool.RequiresApproval,
                tool.InputSchemaJson)).ToArray();
    }
}
