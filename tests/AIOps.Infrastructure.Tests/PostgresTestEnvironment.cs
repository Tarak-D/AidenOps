namespace AIOps.Infrastructure.Tests;

internal static class PostgresTestEnvironment
{
    internal const string ConnectionVariable = "AIOPS_POSTGRES_TEST_CONNECTION";

    internal static string ConnectionString =>
        Environment.GetEnvironmentVariable(ConnectionVariable)
        ?? throw new InvalidOperationException(
            $"Configure {ConnectionVariable} before running PostgreSQL integration tests.");
}

public sealed class RequiresPostgresFactAttribute : FactAttribute
{
    public RequiresPostgresFactAttribute()
    {
        if (string.IsNullOrWhiteSpace(
                Environment.GetEnvironmentVariable(
                    PostgresTestEnvironment.ConnectionVariable)))
        {
            Skip =
                "Configure AIOPS_POSTGRES_TEST_CONNECTION to run PostgreSQL integration tests.";
        }
    }
}
