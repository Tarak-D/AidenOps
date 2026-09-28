using System.Text.Json;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Abstractions.Tools;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;
using AIOps.Domain.Entities;

namespace AIOps.Orchestration.Agents;

/// <summary>
/// Validates an untrusted agent proposal against the server-owned tool registry,
/// classifies it with registered metadata, and records a Proposed action.
/// This service never executes tools or creates an approval request.
/// </summary>
public sealed class ToolProposalValidationService
{
    private readonly IToolRegistry _toolRegistry;
    private readonly IActionExecutionStore _actionStore;
    private readonly IAuditStore _auditStore;
    private readonly IClock _clock;

    public ToolProposalValidationService(
        IToolRegistry toolRegistry,
        IActionExecutionStore actionStore,
        IAuditStore auditStore,
        IClock clock)
    {
        _toolRegistry = toolRegistry;
        _actionStore = actionStore;
        _auditStore = auditStore;
        _clock = clock;
    }

    public async Task<AgentRunResult> ValidateAndPersistAsync(
        Guid ticketId,
        Guid correlationId,
        AgentRunResult result,
        CancellationToken cancellationToken = default)
    {
        if (result.Proposal is not { } proposal)
        {
            if (result.Outcome != AgentRunOutcome.ProposalCreated)
            {
                return result;
            }

            const string missingProposalRejection =
                "Agent gateway reported a tool proposal without providing one.";
            await _auditStore.AppendAsync(
                new AuditRecordInput(
                    correlationId,
                    ActorKind.Agent,
                    "ToolProposalValidator",
                    "ToolProposalRejected",
                    "ticket",
                    ticketId.ToString("N"),
                    JsonSerializer.Serialize(new { reason = missingProposalRejection })),
                cancellationToken);

            return result with
            {
                Outcome = AgentRunOutcome.Escalated,
                EscalationSummary = missingProposalRejection,
                Error = missingProposalRejection,
                Trace = AppendValidationTrace(result.Trace, missingProposalRejection)
            };
        }

        var rejection = ValidateProposal(proposal, out var tool);
        if (rejection is not null)
        {
            await _auditStore.AppendAsync(
                new AuditRecordInput(
                    correlationId,
                    ActorKind.Agent,
                    "ToolProposalValidator",
                    "ToolProposalRejected",
                    "ticket",
                    ticketId.ToString("N"),
                    JsonSerializer.Serialize(new
                    {
                        proposal.ToolName,
                        proposal.ArgumentsJson,
                        proposal.Confidence,
                        reason = rejection
                    })),
                cancellationToken);

            return result with
            {
                Outcome = AgentRunOutcome.Escalated,
                Proposal = null,
                EscalationSummary = rejection,
                Error = rejection,
                Trace = AppendValidationTrace(result.Trace, rejection)
            };
        }

        var action = ActionExecution.Propose(
            ticketId,
            tool!.Name,
            proposal.ArgumentsJson,
            tool.Risk,
            "ToolProposalAgent",
            proposal.Justification,
            _clock.UtcNow);

        await _actionStore.AddAsync(action, cancellationToken);

        var classifiedProposal = proposal with
        {
            Risk = tool.Risk,
            RequiresApproval = tool.RequiresApproval,
            ActionExecutionId = action.Id
        };

        await _auditStore.AppendAsync(
            new AuditRecordInput(
                correlationId,
                ActorKind.Agent,
                "ToolProposalValidator",
                "ToolProposalValidated",
                "action_execution",
                action.Id.ToString("N"),
                JsonSerializer.Serialize(new
                {
                    ticketId,
                    actionExecutionId = action.Id,
                    classifiedProposal.ToolName,
                    classifiedProposal.ArgumentsJson,
                    classifiedProposal.Confidence,
                    classifiedProposal.Justification,
                    risk = tool.Risk,
                    requiresApproval = tool.RequiresApproval
                })),
            cancellationToken);

        return result with
        {
            Outcome = AgentRunOutcome.ProposalCreated,
            Proposal = classifiedProposal,
            Trace = AppendValidationTrace(
                result.Trace,
                $"Validated {tool.Name}; risk={tool.Risk}; requiresApproval={tool.RequiresApproval}.")
        };
    }

    private string? ValidateProposal(
        ToolProposal proposal,
        out ITool? tool)
    {
        tool = null;
        if (string.IsNullOrWhiteSpace(proposal.ToolName))
        {
            return "Tool proposal does not name a tool.";
        }

        tool = _toolRegistry.Get(proposal.ToolName);
        if (tool is null)
        {
            return $"Tool '{proposal.ToolName}' is not registered.";
        }

        if (!double.IsFinite(proposal.Confidence) ||
            proposal.Confidence is < 0 or > 1)
        {
            return "Tool proposal confidence must be between 0 and 1.";
        }

        if (string.IsNullOrWhiteSpace(proposal.Justification))
        {
            return "Tool proposal justification is required.";
        }

        return ValidateArguments(proposal.ArgumentsJson, tool.InputSchemaJson);
    }

    private static string? ValidateArguments(
        string argumentsJson,
        string inputSchemaJson)
    {
        if (string.IsNullOrWhiteSpace(argumentsJson))
        {
            return "Tool proposal arguments must be valid JSON.";
        }

        JsonDocument arguments;
        try
        {
            arguments = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException)
        {
            return "Tool proposal arguments must be valid JSON.";
        }

        using (arguments)
        using (var schema = JsonDocument.Parse(inputSchemaJson))
        {
            var root = arguments.RootElement;
            var schemaRoot = schema.RootElement;
            if (schemaRoot.ValueKind != JsonValueKind.Object ||
                !schemaRoot.TryGetProperty("type", out var type) ||
                type.GetString() != "object")
            {
                throw new InvalidOperationException(
                    "Registered tool input schema must describe a JSON object.");
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                return "Tool proposal arguments must be a JSON object.";
            }

            var properties = schemaRoot.GetProperty("properties");
            foreach (var property in root.EnumerateObject())
            {
                if (!properties.TryGetProperty(property.Name, out var propertySchema))
                {
                    if (schemaRoot.TryGetProperty("additionalProperties", out var additional) &&
                        additional.ValueKind == JsonValueKind.False)
                    {
                        return $"Unexpected tool argument '{property.Name}'.";
                    }

                    continue;
                }

                var propertyError = ValidateProperty(property.Name, property.Value, propertySchema);
                if (propertyError is not null)
                {
                    return propertyError;
                }
            }

            if (schemaRoot.TryGetProperty("required", out var required))
            {
                foreach (var requiredProperty in required.EnumerateArray())
                {
                    var name = requiredProperty.GetString();
                    if (name is not null && !root.TryGetProperty(name, out _))
                    {
                        return $"Required tool argument '{name}' is missing.";
                    }
                }
            }
        }

        return null;
    }

    private static string? ValidateProperty(
        string name,
        JsonElement value,
        JsonElement schema)
    {
        if (!schema.TryGetProperty("type", out var typeElement))
        {
            throw new InvalidOperationException(
                $"Registered schema for tool argument '{name}' has no type.");
        }

        var valid = typeElement.GetString() switch
        {
            "string" => value.ValueKind == JsonValueKind.String,
            "number" => value.ValueKind == JsonValueKind.Number,
            "integer" => value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out _),
            "boolean" => value.ValueKind is JsonValueKind.True or JsonValueKind.False,
            "object" => value.ValueKind == JsonValueKind.Object,
            "array" => value.ValueKind == JsonValueKind.Array,
            _ => throw new InvalidOperationException(
                $"Registered schema for tool argument '{name}' uses an unsupported type.")
        };

        if (!valid)
        {
            return $"Tool argument '{name}' must be of type {typeElement.GetString()}.";
        }

        if (value.ValueKind == JsonValueKind.String &&
            schema.TryGetProperty("minLength", out var minLength) &&
            ((value.GetString()?.Length ?? 0) < minLength.GetInt32() ||
             string.IsNullOrWhiteSpace(value.GetString())))
        {
            return $"Tool argument '{name}' is too short.";
        }

        return null;
    }

    private static IReadOnlyList<StepTrace> AppendValidationTrace(
        IReadOnlyList<StepTrace> existing,
        string summary)
    {
        return existing.Append(
            new StepTrace(
                "ToolProposalValidator",
                "validate",
                "server/policy",
                "v1-tool-schema",
                0,
                0,
                0,
                summary)).ToArray();
    }
}
