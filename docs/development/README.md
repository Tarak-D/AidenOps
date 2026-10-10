# Development Setup and Commands

## Prerequisites

- .NET SDK compatible with the solution.
- Python 3.12.10 was the version documented in the original README; verify the current requirement.
- Docker Desktop when running PostgreSQL locally.
- Git and PowerShell on Windows.

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
dotnet test AIOps.slnx --no-build
```

From the Python directory:

```powershell
pytest -q
```

Keep live-provider tests separate from deterministic regression tests.

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