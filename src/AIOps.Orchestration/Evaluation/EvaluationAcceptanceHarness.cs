using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using AIOps.Abstractions.Evaluation;
using Microsoft.Extensions.Options;

namespace AIOps.Orchestration.Evaluation;

public sealed record EvaluationRevisionRunInput(
    string RevisionSha,
    string ExperimentId,
    Guid RunId,
    string DatasetSha256,
    string? KnowledgeCorpusSha256);

public sealed record EvaluationAcceptanceHarnessRequest(
    EvaluationRevisionRunInput Baseline,
    EvaluationRevisionRunInput Candidate,
    ReadOnlyMemory<byte> DatasetBytes,
    ReadOnlyMemory<byte>? KnowledgeCorpusBytes);

public sealed class EvaluationAcceptanceHarness(
    IEvaluationStore evaluationStore,
    IEvaluationAcceptanceStore acceptanceStore,
    EvaluationAcceptanceService acceptanceService,
    IOptions<EvaluationAcceptancePolicyOptions> policyOptions)
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new()
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task<EvaluationAcceptanceDecision> EvaluateAsync(
        EvaluationAcceptanceHarnessRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateInput(request.Baseline, nameof(request.Baseline));
        ValidateInput(request.Candidate, nameof(request.Candidate));

        if (request.Baseline.RevisionSha.Equals(
                request.Candidate.RevisionSha,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException(
                "Baseline and candidate revisions must be different.",
                nameof(request));
        }

        if (request.DatasetBytes.IsEmpty ||
            request.KnowledgeCorpusBytes is { IsEmpty: true })
        {
            throw new ArgumentException(
                "Verified dataset and corpus bytes cannot be empty.",
                nameof(request));
        }

        var datasetSha256 = Hash(request.DatasetBytes.Span);
        var corpusSha256 = request.KnowledgeCorpusBytes is { } corpusBytes
            ? Hash(corpusBytes.Span)
            : null;

        ValidateDigestMatches(request.Baseline, datasetSha256, corpusSha256);
        ValidateDigestMatches(request.Candidate, datasetSha256, corpusSha256);

        var baselineRuns = await evaluationStore.ListRunsAsync(
            request.Baseline.ExperimentId,
            ct: cancellationToken);
        var candidateRuns = await evaluationStore.ListRunsAsync(
            request.Candidate.ExperimentId,
            ct: cancellationToken);

        ValidatePersistedRun(
            baselineRuns,
            request.Baseline,
            "baseline");
        ValidatePersistedRun(
            candidateRuns,
            request.Candidate,
            "candidate");

        var decision = await acceptanceService.EvaluateAndPersistAsync(
            request.Baseline.ExperimentId,
            request.Candidate.ExperimentId,
            cancellationToken);

        var persisted = await acceptanceStore.GetAsync(
            decision.ResultId,
            cancellationToken);
        ValidateReadBack(request, decision, persisted);

        if (decision.Result.Status != EvaluationAcceptanceStatus.Passed)
        {
            throw new EvaluationAcceptanceHarnessException(
                $"Evaluation acceptance returned {decision.Result.Status}.");
        }

        return decision;
    }

    private void ValidatePersistedRun(
        IReadOnlyList<ExperimentRunRecord> runs,
        EvaluationRevisionRunInput input,
        string side)
    {
        if (runs.Count != 1)
        {
            throw new EvaluationAcceptanceHarnessException(
                $"Expected exactly one persisted {side} run for experiment '{input.ExperimentId}' but found {runs.Count}.");
        }

        var run = runs[0];
        if (run.Id != input.RunId ||
            !string.Equals(
                run.ExperimentId,
                input.ExperimentId,
                StringComparison.Ordinal) ||
            !string.Equals(
                run.SourceRevision,
                input.RevisionSha,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                run.DatasetSha256,
                input.DatasetSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                run.KnowledgeCorpusSha256,
                input.KnowledgeCorpusSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new EvaluationAcceptanceHarnessException(
                $"The persisted {side} run does not match the supplied revision, run, and input fingerprints.");
        }
    }

    private void ValidateReadBack(
        EvaluationAcceptanceHarnessRequest request,
        EvaluationAcceptanceDecision decision,
        EvaluationAcceptanceRecord? persisted)
    {
        if ((decision.Result.BaselineRunId is Guid baselineRunId &&
             baselineRunId != request.Baseline.RunId) ||
            (decision.Result.CandidateRunId is Guid candidateRunId &&
             candidateRunId != request.Candidate.RunId))
        {
            throw new EvaluationAcceptanceHarnessException(
                "The acceptance decision run IDs do not match the requested run IDs.");
        }

        if (persisted is null ||
            persisted.Id != decision.ResultId ||
            persisted.EvaluatedAt != decision.EvaluatedAt ||
            persisted.BaselineExperimentId != request.Baseline.ExperimentId ||
            persisted.CandidateExperimentId != request.Candidate.ExperimentId ||
            persisted.BaselineRunId != decision.Result.BaselineRunId ||
            persisted.CandidateRunId != decision.Result.CandidateRunId ||
            persisted.PolicyName != decision.Result.PolicyName ||
            persisted.Status != decision.Result.Status ||
            !string.Equals(
                persisted.PolicyJson,
                JsonSerializer.Serialize(
                    new
                    {
                        policyOptions.Value.Name,
                        policyOptions.Value.RequiredMetrics
                    },
                    AuditJsonOptions),
                StringComparison.Ordinal) ||
            !string.Equals(
                persisted.IssuesJson,
                JsonSerializer.Serialize(
                    decision.Result.Issues,
                    AuditJsonOptions),
                StringComparison.Ordinal) ||
            !string.Equals(
                persisted.MetricsJson,
                JsonSerializer.Serialize(
                    decision.Result.Metrics,
                    AuditJsonOptions),
                StringComparison.Ordinal))
        {
            throw new EvaluationAcceptanceHarnessException(
                "The persisted acceptance result did not match the evaluated decision.");
        }
    }

    private static void ValidateInput(
        EvaluationRevisionRunInput input,
        string parameterName)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (!IsGitSha(input.RevisionSha))
        {
            throw new ArgumentException(
                "Revision must be a full 40-character hexadecimal Git SHA.",
                parameterName);
        }

        if (string.IsNullOrWhiteSpace(input.ExperimentId) ||
            input.ExperimentId.Length > 200 ||
            input.RunId == Guid.Empty ||
            !EvaluationInputFingerprint.IsSha256(input.DatasetSha256) ||
            (input.KnowledgeCorpusSha256 is not null &&
             !EvaluationInputFingerprint.IsSha256(
                 input.KnowledgeCorpusSha256)))
        {
            throw new ArgumentException(
                "Revision run input is incomplete or invalid.",
                parameterName);
        }
    }

    private static void ValidateDigestMatches(
        EvaluationRevisionRunInput input,
        string datasetSha256,
        string? corpusSha256)
    {
        if (!string.Equals(
                input.DatasetSha256,
                datasetSha256,
                StringComparison.OrdinalIgnoreCase) ||
            !string.Equals(
                input.KnowledgeCorpusSha256,
                corpusSha256,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new EvaluationAcceptanceHarnessException(
                "The supplied baseline and candidate inputs do not match the verified dataset or knowledge corpus bytes.");
        }
    }

    private static string Hash(ReadOnlySpan<byte> bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes));

    private static bool IsGitSha(string? value) =>
        value is { Length: 40 } && value.All(Uri.IsHexDigit);
}

public sealed class EvaluationAcceptanceHarnessException(string message)
    : Exception(message);
