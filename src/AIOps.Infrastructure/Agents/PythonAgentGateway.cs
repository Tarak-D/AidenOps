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
                request.Ticket.Severity.ToString());

            using var response =
                await httpClient.PostAsJsonAsync(
                    StartRunPath,
                    payload,
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                return await CreateHttpFailureResultAsync(
                    response,
                    "Python agent start request failed.",
                    ct);
            }

            var body =
                await response.Content.ReadFromJsonAsync<PythonAgentRunResponse>(
                    cancellationToken: ct);

            if (body is null)
            {
                return CreateFailureResult(
                    "Python agent returned an empty response.");
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
                ex,
                "Python agent start request failed for correlation {CorrelationId}.",
                request.CorrelationId);

            return CreateFailureResult(
                $"Python agent gateway error: {ex.Message}");
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
                request.ToolExecutionSucceeded);

            using var response =
                await httpClient.PostAsJsonAsync(
                    ResumeRunPath,
                    payload,
                    ct);

            if (!response.IsSuccessStatusCode)
            {
                return await CreateHttpFailureResultAsync(
                    response,
                    "Python agent resume request failed.",
                    ct);
            }

            var body =
                await response.Content.ReadFromJsonAsync<PythonAgentRunResponse>(
                    cancellationToken: ct);

            if (body is null)
            {
                return CreateFailureResult(
                    "Python agent returned an empty resume response.");
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
                ex,
                "Python agent resume request failed for correlation {CorrelationId}.",
                request.CorrelationId);

            return CreateFailureResult(
                $"Python agent gateway error: {ex.Message}");
        }
    }

    private static AgentRunResult MapResult(
        PythonAgentRunResponse response)
    {
        var outcome = response.Outcome switch
        {
            "Resolved" => AgentRunOutcome.Resolved,
            "AwaitingApproval" => AgentRunOutcome.AwaitingApproval,
            "Escalated" => AgentRunOutcome.Escalated,
            "Failed" => AgentRunOutcome.Failed,
            _ => AgentRunOutcome.Failed
        };

        TicketDomain? domain = null;

        if (!string.IsNullOrWhiteSpace(response.Triage.Domain) &&
            Enum.TryParse<TicketDomain>(
                response.Triage.Domain,
                ignoreCase: true,
                out var parsedDomain))
        {
            domain = parsedDomain;
        }

        Severity? severity = null;

        if (!string.IsNullOrWhiteSpace(response.Triage.Severity) &&
            Enum.TryParse<Severity>(
                response.Triage.Severity,
                ignoreCase: true,
                out var parsedSeverity))
        {
            severity = parsedSeverity;
        }

        ToolProposal? proposal = null;

        if (response.ToolProposal is not null)
        {
            proposal = new ToolProposal(
                response.ToolProposal.ToolName,
                response.ToolProposal.ArgumentsJson,
                response.ToolProposal.Confidence,
                response.ToolProposal.Justification);
        }

        var trace = response.Trace
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
            response.Triage.Confidence,
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

    private static async Task<AgentRunResult> CreateHttpFailureResultAsync(
        HttpResponseMessage response,
        string message,
        CancellationToken ct)
    {
        var details = await response.Content.ReadAsStringAsync(ct);

        var error =
            string.IsNullOrWhiteSpace(details)
                ? $"{message} HTTP {(int)response.StatusCode}."
                : $"{message} HTTP {(int)response.StatusCode}: {details}";

        return CreateFailureResult(error);
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
        string Severity);

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
        bool ToolExecutionSucceeded);

    private sealed class PythonAgentRunResponse
    {
        [JsonPropertyName("correlation_id")]
        public string CorrelationId { get; set; } = "";

        [JsonPropertyName("outcome")]
        public string Outcome { get; set; } = "Failed";

        [JsonPropertyName("triage")]
        public PythonTriageResponse Triage { get; set; } = new();

        [JsonPropertyName("tool_proposal")]
        public PythonToolProposalResponse? ToolProposal { get; set; }

        [JsonPropertyName("trace")]
        public List<PythonTraceResponse> Trace { get; set; } = [];

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    private sealed class PythonTriageResponse
    {
        [JsonPropertyName("domain")]
        public string Domain { get; set; } = "Unknown";

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "P3";

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }
    }

    private sealed class PythonToolProposalResponse
    {
        [JsonPropertyName("tool_name")]
        public string ToolName { get; set; } = "";

        [JsonPropertyName("arguments_json")]
        public string ArgumentsJson { get; set; } = "{}";

        [JsonPropertyName("confidence")]
        public double Confidence { get; set; }

        [JsonPropertyName("justification")]
        public string Justification { get; set; } = "";
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