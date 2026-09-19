using AIOps.Abstractions.Tools;

namespace AIOps.Tools;

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, ITool> _tools;

    public ToolRegistry(IEnumerable<ITool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        var materialized = tools.ToArray();

        if (materialized.Any(tool => tool is null))
        {
            throw new ArgumentException("Tool collection cannot contain null entries.", nameof(tools));
        }

        var duplicates = materialized
            .GroupBy(tool => tool.Name, StringComparer.Ordinal)
            .Where(group => group.Count() > 1)
            .Select(group => group.Key)
            .ToArray();

        if (duplicates.Length > 0)
        {
            throw new ArgumentException(
                $"Duplicate tool name(s): {string.Join(", ", duplicates)}",
                nameof(tools));
        }

        _tools = materialized
            .ToDictionary(tool => tool.Name, StringComparer.Ordinal);
    }

    public ITool? Get(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _tools.TryGetValue(name, out var tool)
            ? tool
            : null;
    }

    public IReadOnlyList<ToolDescriptor> Describe()
    {
        return _tools.Values
            .OrderBy(tool => tool.Name, StringComparer.Ordinal)
            .Select(tool => new ToolDescriptor(
                tool.Name,
                tool.Description,
                tool.Risk,
                tool.RequiresApproval))
            .ToArray();
    }
}
