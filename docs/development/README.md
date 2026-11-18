# Development Setup and Commands

## Prerequisites

- .NET SDK 10.0.400 or a compatible later feature release, as selected by `global.json`.
- Python 3.12.
- Docker when running PostgreSQL-backed tests locally.
- Git and PowerShell on Windows.

Run the portfolio demo with `ASPNETCORE_ENVIRONMENT=Development` (set it
explicitly when launching locally). The local `X-Dev-User`
identity is configured only by `appsettings.Development.json`; see
[authentication and authorization](../security/README.md). Non-Development
hosts reject that configuration and have no production identity provider
configured. The demo identity lets local users choose a role and is not
authentication, so keep the Host on its `localhost` launch profile and do not
expose a Development instance to a shared network.

The full .NET test suite includes PostgreSQL integration tests. CI provisions a
disposable `pgvector/pgvector:pg15` service and separate
`aios_phase15_test` and `aios_acceptance_test` databases. The acceptance test
is opt-in locally and requires `AIOPS_ACCEPTANCE_TEST_CONNECTION`,
`AIOPS_ACCEPTANCE_TEST_HOST`, `AIOPS_ACCEPTANCE_TEST_PORT`, and
`AIOPS_ACCEPTANCE_TEST_DATABASE=aios_acceptance_test`. It refuses any other
database name. Never point it at the development database.

The other Phase 15 PostgreSQL integration tests use
`AIOPS_PHASE15_TEST_CONNECTION`, `AIOPS_PHASE15_TEST_HOST`,
`AIOPS_PHASE15_TEST_PORT`, and `AIOPS_PHASE15_TEST_DATABASE=aios_phase15_test`.
They remain opt-in locally and validate that dedicated target before writing.

Start the local service from the repository root and configure the development
host connection. This connection is not used by the opt-in PostgreSQL
integration tests:

```powershell
docker compose up -d postgres
$env:ConnectionStrings__PostgresConnection = Read-Host 'Development PostgreSQL connection string'
$env:AIOPS_POSTGRES_TEST_CONNECTION = Read-Host 'PostgreSQL integration test connection string'
```

For local PostgreSQL setup, run the schema scripts from the repository root in
this order:

```powershell
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Main.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Persistence.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase6.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase7.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase14-Batch1.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase14-Batch2.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase14-Batch3.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase15-Batch2-Runs.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase15-Batch2.sql
psql -h localhost -p 5432 -U aios -d aios -v ON_ERROR_STOP=1 -f .\PostgreSQL-Phase15-Batch7.sql
```

These scripts do not reset the database or repair incompatible schemas. Some
core scripts create objects without `IF NOT EXISTS`, so apply the full sequence
once to a clean local database. The PostgreSQL acceptance integration test
creates its own uniquely identified dataset inside its transaction and
verifies that the dataset, runs, and acceptance decision are rolled back.

### Isolated Phase 15 PostgreSQL test databases

Create the two isolated databases through the existing Docker service, then
apply the idempotent Phase 15 evaluation schema to each. This does not modify
or reset the development `aios` database. The following commands do not print
credentials; enter each connection string locally when prompted.

```powershell
$evaluationSchemas = @(
  '.\PostgreSQL-Phase15-Batch2-Runs.sql',
  '.\PostgreSQL-Phase15-Batch2.sql',
  '.\PostgreSQL-Phase15-Batch7.sql'
)

foreach ($database in 'aios_phase15_test', 'aios_acceptance_test') {
  $exists = docker exec aios-postgres psql -U aios -d aios -tAc "SELECT 1 FROM pg_database WHERE datname = '$database'"
  if ([string]::IsNullOrWhiteSpace($exists)) {
    docker exec aios-postgres createdb -U aios $database
  }

  foreach ($schema in $evaluationSchemas) {
    Get-Content -Raw -LiteralPath $schema |
      docker exec -i aios-postgres psql -v ON_ERROR_STOP=1 -U aios -d $database
  }
}

$env:AIOPS_PHASE15_TEST_CONNECTION = Read-Host 'Phase 15 isolated connection string'
$env:AIOPS_PHASE15_TEST_HOST = 'localhost'
$env:AIOPS_PHASE15_TEST_PORT = '5432'
$env:AIOPS_PHASE15_TEST_DATABASE = 'aios_phase15_test'

$env:AIOPS_ACCEPTANCE_TEST_CONNECTION = Read-Host 'Acceptance isolated connection string'
$env:AIOPS_ACCEPTANCE_TEST_HOST = 'localhost'
$env:AIOPS_ACCEPTANCE_TEST_PORT = '5432'
$env:AIOPS_ACCEPTANCE_TEST_DATABASE = 'aios_acceptance_test'
```

The integration tests reject any connection that does not match the respective
host, port, and isolated database name.

The existing PostgreSQL infrastructure tests are also opt-in locally. Set
`AIOPS_POSTGRES_TEST_CONNECTION` to the intended test connection before
running them. CI sets it only for its disposable PostgreSQL service.

## Python environment

The deterministic .NET-to-Python integration test discovers an interpreter in
this order: `AIDENOPS_PYTHON_EXECUTABLE`, repository-root `.venv`, then
repository-root `venv` (with Windows and Unix layouts supported). An explicit
override must point to an existing interpreter; it never silently falls back.

Create the repository-root virtual environment and install the Python service
requirements:

```powershell
Set-Location D:\Project\AIOps.AgentSwarm
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python --version
python -m pip install -r .\python\requirements.txt
```

If `.venv` already exists, activate it instead of recreating it. To use a
different existing interpreter for the deterministic HTTP integration test:

```powershell
$env:AIDENOPS_PYTHON_EXECUTABLE = 'C:\path with spaces\python.exe'
```

## Run the Python service

From the repository root with the virtual environment active:

```powershell
python -m uvicorn agent_service.main:app --app-dir .\python --host 127.0.0.1 --port 8000
```

In another terminal, check the health endpoint:

```powershell
python -c "import httpx; r=httpx.get('http://127.0.0.1:8000/health',timeout=10); print(r.status_code); print(r.text)"
```

## Run tests

From the repository root:

```powershell
dotnet build AIOps.slnx --configuration Release
dotnet test AIOps.slnx --configuration Release
```

From the repository root with the virtual environment active:

```powershell
python -m pytest .\python\agent_service\tests -q
```

The CI workflow runs the Python suite with the deterministic provider selected,
then restores and builds the .NET solution and runs its test suite once with
the PostgreSQL service available. Acceptance persistence uses its isolated
database; other database-backed tests use their configured test service.
Python tests use the repository's
`python/requirements.txt` and do not require live LLM credentials.

The revision acceptance harness is covered by isolated unit tests:

```powershell
dotnet test tests\AIOps.Orchestration.Tests\AIOps.Orchestration.Tests.csproj `
  --configuration Release `
  --filter "FullyQualifiedName~EvaluationAcceptanceHarnessTests"
```

The command-line harness requires a protected policy file and persisted runs
with revision and input provenance. Configure its four
`AIOPS_EVALUATION_HARNESS_*` environment variables for a disposable database
named exactly `aios_phase15_test`; never point it at the development database.
Invoke it with explicit `--manifest`, `--policy`, and `--dataset` file paths,
adding `--corpus` when comparing knowledge retrieval runs:

```powershell
dotnet run --project tools\AIOps.Evaluation.Harness -- `
  --manifest <run-manifest.json> `
  --policy <protected-policy.json> `
  --dataset <verified-dataset.json>
```

The PostgreSQL acceptance persistence integration test runs only when its
dedicated `AIOPS_ACCEPTANCE_TEST_*` environment values are configured.

## Inspect repository state

```powershell
git status --short
git log --oneline --decorate -10
```

Protect uncommitted changes. Do not use destructive Git commands as routine troubleshooting.

## Related guides

- [Troubleshooting](troubleshooting.md)
- [Provider configuration](../providers/provider-configuration.md)
- [Live provider testing](../providers/live-provider-testing.md)
- [Documentation index](../README.md)
