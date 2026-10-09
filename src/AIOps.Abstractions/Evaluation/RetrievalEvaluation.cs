using AIOps.Abstractions.Knowledge;

namespace AIOps.Abstractions.Evaluation;

public sealed record RetrievalEvaluationCase(
    string Id,
    string Query,
    IReadOnlyList<Guid> ExpectedDocumentIds,
    int TopK = 5);

public sealed record RetrievalEvaluationCaseResult(
    string CaseId,
    string Query,
    IReadOnlyList<Guid> ExpectedDocumentIds,
    IReadOnlyList<Guid> RetrievedDocumentIds,
    int TopK,
    bool Hit,
    double Recall,
    double ReciprocalRank,
    int? FirstRelevantRank,
    IReadOnlyList<KnowledgeSearchResult> Results);

public sealed record RetrievalEvaluationMetrics(
    int SampleCount,
    double HitAtK,
    double RecallAtK,
    double MeanReciprocalRank,
    double AverageFirstRelevantRank);

public sealed record RetrievalEvaluationSummary(
    ExperimentRunRecord Run,
    IReadOnlyList<RetrievalEvaluationCaseResult> Cases,
    RetrievalEvaluationMetrics Metrics);