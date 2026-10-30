using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using AIOps.Abstractions.Agents;
using AIOps.Contracts.AgentGateway;
using AIOps.Domain;

namespace AIOps.Infrastructure.Agents;

public sealed class PythonAgentGateway(
    HttpClient httpClient,
    ILogger<PythonAgentGateway> logger) : IAgentGateway
{
    private const string StartRunPath =
        "api/v1/agent/runs";

    private const string ResumeRunPath =
        "api/v1/agent/runs/resume";

    public async Task<AgentRunResult> StartRunAsync(
        AgentRunRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new PythonAgentRunRequest(
                request.CorrelationId.ToString(),
                request.Ticket.TicketId.ToString(),
                request.Ticket.ExternalRef,
                request.Ticket.Title,
                request.Ticket.Description,
                request.Ticket.ReporterEmail,
                request.Ticket.Domain.ToString(),
                request.Ticket.Severity.ToString(),
                request.AllowedTools.Select(tool => new PythonToolManifestEntry(
                    tool.Name,
                    tool.Description,
                    tool.Risk.ToString(),
                    tool.RequiresApproval,
                    tool.InputSchemaJson)).ToArray(),
                request.TriageConfidenceThreshold,
                request.MaxAttempts);

            using var response =
                await httpClient.PostAsJsonAsync(
                    StartRunPath,
                    payload,
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                return await CreateHttpFailureResultAsync(
                    response,
                    "Python agent start request failed.");
            }

            var body =
                await response.Content.ReadFromJsonAsync<PythonAgentRunResponse>(
                    cancellationToken: ct);

            if (body is null)
            {
                return CreateFailureResult(
                    "Python agent returned an empty response.");
            }

            var invalidResponse = ValidateResponse(
                body,
                request.CorrelationId,
                isResumeResponse: false);
            if (invalidResponse is not null)
            {
                logger.LogWarning(
                    "Python agent returned an invalid start response for correlation {CorrelationId}: {ValidationError}.",
                    request.CorrelationId,
                    invalidResponse);
                return CreateFailureResult(invalidResponse);
            }

            return MapResult(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Python agent start request failed for correlation {CorrelationId} (error type {ErrorType}).",
                request.CorrelationId,
                ex.GetType().Name);

            return CreateFailureResult(
                "Python agent gateway request failed.");
        }
    }

    public async Task<AgentRunResult> ResumeRunAsync(
        ResumeAgentRunRequest request,
        CancellationToken ct = default)
    {
        try
        {
            var payload = new PythonResumeAgentRunRequest(
                request.CorrelationId.ToString(),
                request.TicketId.ToString(),
                request.ApprovalGranted,
                request.ApprovalDecidedBy,
                request.ToolResultJson,
                request.ToolExecutionSucceeded,
                request.ActionExecutionId.ToString(),
                request.ApprovalStatus,
                request.ActionStatus,
                request.ToolExecutionError);

            using var response =
                await httpClient.PostAsJsonAsync(
                    ResumeRunPath,
                    payload,
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                return await CreateHttpFailureResultAsync(
                    response,
                    "Python agent resume request failed.");
            }

            var body =
                await response.Content.ReadFromJsonAsync<PythonAgentRunResponse>(
                    cancellationToken: ct);

            if (body is null)
            {
                return CreateFailureResult(
                    "Python agent returned an empty resume response.");
            }

            var invalidResponse = ValidateResponse(
                body,
                request.CorrelationId,
                isResumeResponse: true);
            if (invalidResponse is not null)
            {
                logger.LogWarning(
                    "Python agent returned an invalid resume response for correlation {CorrelationId}: {ValidationError}.",
                    request.CorrelationId,
                    invalidResponse);
                return CreateFailureResult(invalidResponse);
            }

            return MapResult(body);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            logger.LogError(
                "Python agent resume request failed for correlation {CorrelationId} (error type {ErrorType}).",
                request.CorrelationId,
                ex.GetType().Name);

            return CreateFailureResult(
                "Python agent gateway request failed.");
        }
    }

    private static AgentRunResult MapResult(
        PythonAgentRunResponse response)
    {
        var triage = response.Triage!;
        var outcome = response.Outcome switch
        {
            "Resolved" => AgentRunOutcome.Resolved,
            "AwaitingApproval" => AgentRunOutcome.AwaitingApproval,
            "Escalated" => AgentRunOutcome.Escalated,
            "Failed" => AgentRunOutcome.Failed,
            "ProposalCreated" => AgentRunOutcome.ProposalCreated,
            _ => AgentRunOutcome.Failed
        };

        TicketDomain? domain = null;

        if (!string.IsNullOrWhiteSpace(triage.Domain) &&
            Enum.TryParse<TicketDomain>(
                triage.Domain,
                ignoreCase: true,
                out var parsedDomain))
        {
            domain = parsedDomain;
        }

        Severity? severity = null;

        if (!string.IsNullOrWhiteSpace(triage.Severity) &&
            Enum.TryParse<Severity>(
                triage.Severity,
                ignoreCase: true,
                out var parsedSeverity))
        {
            severity = parsedSeverity;
        }

        ToolProposal? proposal = null;

        if (response.ToolProposal is not null)
        {
            proposal = new ToolProposal(
                response.ToolProposal.ToolName!,
                response.ToolProposal.ArgumentsJson!,
                response.ToolProposal.Confidence!.Value,
                response.ToolProposal.Justification!);
        }

        var trace = response.Trace!
            .Select(
                item => new StepTrace(
                    item.Agent,
                    item.Step,
                    item.Model,
                    item.PromptVersion,
                    item.PromptTokens,
                    item.CompletionTokens,
                    item.LatencyMs,
                    item.Summary))
            .ToList();

        return new AgentRunResult(
            outcome,
            triage.Confidence,
            domain,
            severity,
            proposal,
            outcome == AgentRunOutcome.Escalated
                ? response.Error ?? "Python agent escalated the run."
                : null,
            outcome == AgentRunOutcome.Resolved
                ? "Python agent completed the workflow."
                : null,
            trace,
            response.Error);
    }

    private static string? ValidateResponse(
        PythonAgentRunResponse response,
        Guid expectedCorrelationId,
        bool isResumeResponse)
    {
        if (!Guid.TryParse(response.CorrelationId, out var correlationId) ||
            correlationId != expectedCorrelationId)
        {
            return "Python agent response correlation ID did not match the request.";
        }

        var supportedOutcome = isResumeResponse
            ? response.Outcome is "Resolved" or "Escalated"
            : response.Outcome is "Resolved" or "Escalated" or "ProposalCreated";
        if (!supportedOutcome)
        {
            return "Python agent response contained an unsupported outcome.";
        }

        if (response.Triage is null)
        {
            return "Python agent response omitted triage data.";
        }

        if (!isResumeResponse)
        {
            var hasDomain = Enum.TryParse<TicketDomain>(
                response.Triage.Domain,
                ignoreCase: true,
                out var domain);
            var hasSeverity = Enum.TryParse<Severity>(
                response.Triage.Severity,
                ignoreCase: true,
                out var severity);
            if (string.IsNullOrWhiteSpace(response.Triage.Domain) ||
                !hasDomain ||
                !Enum.IsDefined(domain) ||
                string.IsNullOrWhiteSpace(response.Triage.Severity) ||
                !hasSeverity ||
                !Enum.IsDefined(severity) ||
                response.Triage.Confidence is not double confidence ||
                !double.IsFinite(confidence) ||
                confidence is < 0 or > 1)
            {
                return "Python agent response contained invalid triage data.";
            }
        }

        if (response.Trace is null ||
            response.Trace.Any(trace =>
                trace is null ||
                string.IsNullOrWhiteSpace(trace.Agent) ||
                string.IsNullOrWhiteSpace(trace.Step) ||
                string.IsNullOrWhiteSpace(trace.Model) ||
                string.IsNullOrWhiteSpace(trace.PromptVersion) ||
                string.IsNullOrWhiteSpace(trace.Summary) ||
                trace.PromptTokens < 0 ||
                trace.CompletionTokens < 0 ||
                !double.IsFinite(trace.LatencyMs) ||
                trace.LatencyMs < 0))
        {
            return "Python agent response contained invalid trace data.";
        }

        if (response.ToolProposal is not null)
        {
            if (isResumeResponse ||
                response.Outcome != "ProposalCreated")
            {
                return "Python agent response included a proposal for an outcome that does not carry one.";
            }

            if (string.IsNullOrWhiteSpace(response.ToolProposal.ToolName) ||
                string.IsNullOrWhiteSpace(response.ToolProposal.Justification) ||
                response.ToolProposal.Confidence is not double proposalConfidence ||
                !double.IsFinite(proposalConfidence) ||
                proposalConfidence is < 0 or > 1 ||
                !IsJsonObject(response.ToolProposal.ArgumentsJson))
            {
                return "Python agent response contained an invalid tool proposal.";
            }
        }

        if (response.Outcome == "ProposalCreated" &&
            response.ToolProposal is null)
        {
            return "Python agent response reported a proposal without proposal data.";
        }

        return null;
    }

    private static bool IsJsonObject(string? json)
    {
        if (json is null)
        {
            return false;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            return document.RootElement.ValueKind ==
                System.Text.Json.JsonValueKind.Object;
        }
        catch (System.Text.Json.JsonException)
        {
            return false;
        }
    }

    private static AgentRunResult CreateFailureResult(
        string error)
    {
        return new AgentRunResult(
            AgentRunOutcome.Failed,
            null,
            null,
            null,
            null,
            null,
            null,
            [
                new StepTrace(
                    "PythonAgentGateway",
                    "http",
                    "none",
                    "v1",
                    0,
                    0,
                    0,
                    error)
            ],
            error);
    }

    private static Task<AgentRunResult> CreateHttpFailureResultAsync(
        HttpResponseMessage response,
        string message)
    {
        return Task.FromResult(CreateFailureResult(
            $"{message} HTTP {(int)response.StatusCode}."));
    }

    private sealed record PythonAgentRunRequest(
        [property: JsonPropertyName("correlation_id")]
        string CorrelationId,

        [property: JsonPropertyName("ticket_id")]
        string TicketId,

        [property: JsonPropertyName("external_ref")]
        string ExternalRef,

        [property: JsonPropertyName("title")]
        string Title,

        [property: JsonPropertyName("description")]
        string Description,

        [property: JsonPropertyName("reporter_email")]
        string ReporterEmail,

        [property: JsonPropertyName("domain")]
        string Domain,

        [property: JsonPropertyName("severity")]
        string Severity,

        [property: JsonPropertyName("allowed_tools")]
        IReadOnlyList<PythonToolManifestEntry> AllowedTools,

        [property: JsonPropertyName("triage_confidence_threshold")]
        double TriageConfidenceThreshold,

        [property: JsonPropertyName("max_attempts")]
        int MaxAttempts);

    private sealed record PythonToolManifestEntry(
        [property: JsonPropertyName("name")]
        string Name,

        [property: JsonPropertyName("description")]
        string Description,

        [property: JsonPropertyName("risk")]
        string Risk,

        [property: JsonPropertyName("requires_approval")]
        bool RequiresApproval,

        [property: JsonPropertyName("input_schema_json")]
        string InputSchemaJson);

    private sealed record PythonResumeAgentRunRequest(
        [property: JsonPropertyName("correlation_id")]
        string CorrelationId,

        [property: JsonPropertyName("ticket_id")]
        string TicketId,

        [property: JsonPropertyName("approval_granted")]
        bool ApprovalGranted,

        [property: JsonPropertyName("approval_decided_by")]
        string? ApprovalDecidedBy,

        [property: JsonPropertyName("tool_result_json")]
        string? ToolResultJson,

        [property: JsonPropertyName("tool_execution_succeeded")]
        bool ToolExecutionSucceeded,

        [property: JsonPropertyName("action_execution_id")]
        string ActionExecutionId,

        [property: JsonPropertyName("approval_status")]
        string? ApprovalStatus,

        [property: JsonPropertyName("action_status")]
        string? ActionStatus,

        [property: JsonPropertyName("tool_execution_error")]
        string? ToolExecutionError);

    private sealed class PythonAgentRunResponse
    {
        [JsonPropertyName("correlation_id")]
        public string CorrelationId { get; set; } = "";

        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = "Failed";

        [JsonPropertyName("triage")]
        public PythonTriageResponse? Triage { get; set; }

        [JsonPropertyName("tool_proposal")]
        public PythonToolProposalResponse? ToolProposal { get; set; }

        [JsonPropertyName("trace")]
        public List<PythonTraceResponse>? Trace { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class PythonTriageResponse
    {
        [JsonPropertyName("domain")]
        public string? Domain { get; set; }

        [JsonPropertyName("severity")]
        public string? Severity { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }
    }

    private sealed class PythonToolProposalResponse
    {
        [JsonPropertyName("tool_name")]
        public string? ToolName { get; set; }

        [JsonPropertyName("arguments_json")]
        public string? ArgumentsJson { get; set; }

        [JsonPropertyName("confidence")]
        public double? Confidence { get; set; }

        [JsonPropertyName("justification")]
        public string? Justification { get; set; }
    }

    private sealed class PythonTraceResponse
    {
        [JsonPropertyName("agent")]
        public string Agent { get; set; } = "";

        [JsonPropertyName("step")]
        public string Step { get; set; } = "";

        [JsonPropertyName("model")]
        public string Model { get; set; } = "";

        [JsonPropertyName("prompt_version")]
        public string PromptVersion { get; set; } = "";

        [JsonPropertyName("prompt_tokens")]
        public int PromptTokens { get; set; }

        [JsonPropertyName("completion_tokens")]
        public int CompletionTokens { get; set; }

        [JsonPropertyName("latency_ms")]
        public double LatencyMs { get; set; }

        [JsonPropertyName("summary")]
        public string Summary { get; set; } = "";
    }
}
