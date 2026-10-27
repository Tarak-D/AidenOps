# Development Setup and Commands

## Prerequisites

- .NET SDK 10.0.400 or a compatible later feature release, as selected by `global.json`.
- Python 3.12.
- Docker when running PostgreSQL-backed tests locally.
- Git and PowerShell on Windows.

The full .NET test suite includes PostgreSQL integration tests. Start the
repository's PostgreSQL/pgvector service and apply the SQL schema scripts before
running the suite. The acceptance integration test also requires
`ConnectionStrings__PostgresConnection`. CI uses a disposable
`pgvector/pgvector:pg15` service, waits for its PostgreSQL health check, and
applies the schemas before testing.

Start the local service from the repository root and configure the connection
used by the acceptance integration test:

```powershell
docker compose up -d postgres
$env:ConnectionStrings__PostgresConnection = "Host=localhost;Port=5432;Database=aios;Username=aios;Password=aiospw"
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
```

These scripts do not reset the database or repair incompatible schemas. Some
core scripts create objects without `IF NOT EXISTS`, so apply the full sequence
once to a clean local database. The Phase 15 integration test creates its own
uniquely identified dataset inside its transaction and verifies that the
dataset, runs, and acceptance decision are rolled back.

## Python environment

Run from the repository's Python directory:

```powershell
cd D:\Project\AIOps.AgentSwarm\python
python -m venv .venv
.\.venv\Scripts\Activate.ps1
python --version
pip install -r requirements.txt
```

If `.venv` already exists, activate it instead of recreating it.

## Run the Python service

From the Python directory:

```powershell
uvicorn agent_service.main:app --host 127.0.0.1 --port 8000
```

In another terminal, check the health endpoint:

```powershell
python -c "import httpx; r=httpx.get('http://127.0.0.1:8000/health',timeout=10); print(r.status_code); print(r.text)"
```

## Run tests

From the repository root:

```powershell
dotnet build AIOps.slnx
dotnet test AIOps.slnx
```

From the Python directory:

```powershell
python -m pytest -q
```

The CI workflow runs the Python suite with the deterministic provider selected,
then restores and builds the .NET solution and runs its test suite once with
the PostgreSQL connection available. The PostgreSQL-backed tests use the
disposable test service; Python tests use the repository's
`python/requirements.txt` and do not require live LLM credentials.

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