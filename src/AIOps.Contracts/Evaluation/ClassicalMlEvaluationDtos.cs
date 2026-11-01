namespace AIOps.Contracts.Evaluation;

public sealed record ClassicalMlEvaluationResponse(
    string TrainingCorpusId,
    string TrainingCorpusName,
    string TrainingCorpusVersion,
    string DatasetId,
    string DatasetName,
    string DatasetVersion,
    string ModelType,
    string ModelName,
    int TotalCases,
    int CorrectCases,
    double Accuracy,
    IReadOnlyList<ClassicalMlEvaluationPrediction> CaseResults);

public sealed record ClassicalMlEvaluationPrediction(
    string CaseId,
    string PredictedDomain,
    string ExpectedDomain,
    double Confidence);
