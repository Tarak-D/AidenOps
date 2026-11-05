using System.Text.Json;
using AIOps.Abstractions.Evaluation;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Evaluation;
using AIOps.Infrastructure.Time;
using AIOps.Orchestration.Evaluation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using Pgvector.EntityFrameworkCore;

try
{
    var arguments = ParseArguments(args);
    var manifest = JsonSerializer.Deserialize<EvaluationHarnessManifest>(
        await File.ReadAllTextAsync(arguments["manifest"]),
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException(
            "The run manifest is invalid.");

    var policy = JsonSerializer.Deserialize<EvaluationAcceptancePolicyOptions>(
        await File.ReadAllTextAsync(arguments["policy"]),
        new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        }) ?? throw new InvalidOperationException(
            "The acceptance policy is invalid.");
    policy.CreatePolicy();

    var connectionString = GetIsolatedConnectionString();
    var datasetBytes = await File.ReadAllBytesAsync(arguments["dataset"]);
    ReadOnlyMemory<byte>? corpusBytes =
        arguments.TryGetValue("corpus", out var corpusPath)
            ? await File.ReadAllBytesAsync(corpusPath)
            : null;

    var dbOptions = new DbContextOptionsBuilder<AIOpsDbContext>()
        .UseNpgsql(
            connectionString,
            npgsqlOptions => npgsqlOptions.UseVector())
        .Options;

    await using var dbContext = new AIOpsDbContext(dbOptions);
    var evaluationStore = new PostgresEvaluationStore(dbContext);
    var acceptanceStore =
        new PostgresEvaluationAcceptanceStore(dbContext);
    var options = Options.Create(policy);
    var acceptanceService = new EvaluationAcceptanceService(
        new EvaluationAcceptanceGate(evaluationStore),
        acceptanceStore,
        options,
        new SystemClock());
    var harness = new EvaluationAcceptanceHarness(
        evaluationStore,
        acceptanceStore,
        acceptanceService,
        options);
    var decision = await harness.EvaluateAsync(
        new EvaluationAcceptanceHarnessRequest(
            manifest.Baseline,
            manifest.Candidate,
            datasetBytes,
            corpusBytes));

    Console.WriteLine(
        $"Evaluation acceptance: PASSED; result={decision.ResultId}; baselineRun={decision.Result.BaselineRunId}; candidateRun={decision.Result.CandidateRunId}");
    return 0;
}
catch (Exception exception)
{
    Console.Error.WriteLine(
        $"Evaluation acceptance: FAILED ({exception.GetType().Name}).");
    return 1;
}

static Dictionary<string, string> ParseArguments(string[] arguments)
{
    var values = new Dictionary<string, string>(StringComparer.Ordinal);
    for (var index = 0; index < arguments.Length; index += 2)
    {
        if (index + 1 >= arguments.Length ||
            arguments[index] is not ("--manifest" or "--policy" or "--dataset" or "--corpus") ||
            !values.TryAdd(arguments[index][2..], arguments[index + 1]))
        {
            throw new ArgumentException(
                "Arguments must use unique --manifest, --policy, --dataset, and optional --corpus values.");
        }
    }

    if (!values.ContainsKey("manifest") ||
        !values.ContainsKey("policy") ||
        !values.ContainsKey("dataset"))
    {
        throw new ArgumentException(
            "The --manifest, --policy, and --dataset arguments are required.");
    }

    return values;
}

static string GetIsolatedConnectionString()
{
    const string requiredDatabase = "aios_phase15_test";
    var connectionString = Environment.GetEnvironmentVariable(
        "AIOPS_EVALUATION_HARNESS_CONNECTION");
    var expectedHost = Environment.GetEnvironmentVariable(
        "AIOPS_EVALUATION_HARNESS_HOST");
    var expectedPortValue = Environment.GetEnvironmentVariable(
        "AIOPS_EVALUATION_HARNESS_PORT");
    var expectedDatabase = Environment.GetEnvironmentVariable(
        "AIOPS_EVALUATION_HARNESS_DATABASE");

    if (string.IsNullOrWhiteSpace(connectionString) ||
        string.IsNullOrWhiteSpace(expectedHost) ||
        !int.TryParse(expectedPortValue, out var expectedPort) ||
        expectedPort is < 1 or > 65535 ||
        !string.Equals(
            expectedDatabase,
            requiredDatabase,
            StringComparison.Ordinal))
    {
        throw new InvalidOperationException(
            "The isolated evaluation database configuration is unavailable or invalid.");
    }

    var connection = new NpgsqlConnectionStringBuilder(connectionString);
    if (!string.Equals(
            connection.Database,
            requiredDatabase,
            StringComparison.Ordinal) ||
        string.IsNullOrWhiteSpace(connection.Host) ||
        connection.Host.Contains(',') ||
        !string.Equals(
            connection.Host,
            expectedHost,
            StringComparison.OrdinalIgnoreCase) ||
        connection.Port != expectedPort)
    {
        throw new InvalidOperationException(
            "The evaluation harness requires its explicitly configured isolated PostgreSQL database.");
    }

    return connectionString;
}

internal sealed record EvaluationHarnessManifest(
    EvaluationRevisionRunInput Baseline,
    EvaluationRevisionRunInput Candidate);
