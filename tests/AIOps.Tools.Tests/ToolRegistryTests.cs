using AIOps.Abstractions.Tools;
using AIOps.Domain;

namespace AIOps.Tools.Tests;

public sealed class ToolRegistryTests
{
    private sealed class TestTool : ITool
    {
        public string Name { get; }
        public string Description { get; }
        public RiskLevel Risk { get; }

        public TestTool(string name, string description, RiskLevel risk)
        {
            Name = name;
            Description = description;
            Risk = risk;
        }

        public Task<ToolResult> ExecuteAsync(
            string argumentsJson,
            ToolExecutionContext ctx,
            CancellationToken ct = default)
        {
            return Task.FromResult(new ToolResult(true, "test"));
        }
    }

    [Fact]
    public void Get_returns_registered_tool()
    {
        var tool = new TestTool("Test.Read", "Test read tool", RiskLevel.Safe);
        var registry = new ToolRegistry([tool]);

        var result = registry.Get("Test.Read");

        Assert.Same(tool, result);
    }

    [Fact]
    public void Get_returns_null_for_unknown_tool()
    {
        var registry = new ToolRegistry([]);

        var result = registry.Get("Does.NotExist");

        Assert.Null(result);
    }

    [Fact]
    public void Constructor_rejects_duplicate_tool_names()
    {
        var tools = new ITool[]
        {
            new TestTool("Duplicate", "First", RiskLevel.Safe),
            new TestTool("Duplicate", "Second", RiskLevel.Moderate)
        };

        var exception = Assert.Throws<ArgumentException>(() => new ToolRegistry(tools));

        Assert.Contains("Duplicate", exception.Message);
    }

    [Fact]
    public void Constructor_rejects_null_tool_collection()
    {
        Assert.Throws<ArgumentNullException>(() => new ToolRegistry(null!));
    }

    [Fact]
    public void Constructor_rejects_null_tool_entry()
    {
        ITool?[] tools =
        [
            new TestTool("Valid", "Valid tool", RiskLevel.Safe),
            null
        ];

        Assert.Throws<ArgumentException>(() => new ToolRegistry(tools!));
    }

    [Fact]
    public void Describe_returns_tools_in_deterministic_name_order()
    {
        var registry = new ToolRegistry(
        [
            new TestTool("Z.Tool", "Z", RiskLevel.Safe),
            new TestTool("A.Tool", "A", RiskLevel.Moderate),
            new TestTool("M.Tool", "M", RiskLevel.Sensitive)
        ]);

        var descriptions = registry.Describe();

        Assert.Equal(
            ["A.Tool", "M.Tool", "Z.Tool"],
            descriptions.Select(x => x.Name));
    }

    [Fact]
    public void Describe_preserves_risk_and_approval_metadata()
    {
        var registry = new ToolRegistry(
        [
            new TestTool("Safe.Tool", "Safe", RiskLevel.Safe),
            new TestTool("Sensitive.Tool", "Sensitive", RiskLevel.Sensitive)
        ]);

        var descriptions = registry.Describe();

        var safe = descriptions.Single(x => x.Name == "Safe.Tool");
        var sensitive = descriptions.Single(x => x.Name == "Sensitive.Tool");

        Assert.Equal(RiskLevel.Safe, safe.Risk);
        Assert.False(safe.RequiresApproval);

        Assert.Equal(RiskLevel.Sensitive, sensitive.Risk);
        Assert.True(sensitive.RequiresApproval);
    }
}
