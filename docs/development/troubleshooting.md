# Development Troubleshooting

## Start with repository state

From the repository root:

```powershell
git status --short
git log -1 --oneline
```

Protect uncommitted changes. Do not use `git reset --hard`, `git clean`, or broad restore commands as routine troubleshooting steps.

## .NET build and tests

The prior README documents:

```powershell
dotnet build AIOps.slnx
dotnet test AIOps.slnx
dotnet test AIOps.slnx --no-build
```

Run from the repository root. If a command fails, capture the first compiler/test error and the project name before attempting fixes. Do not infer that a successful build implies live provider or integration verification.

## Python environment

From `python`:

```powershell
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
pytest -q
```

Use the repository's expected Python version and current dependency file. If pytest cannot import `agent_service`, check the working directory, active interpreter, and import path before changing package structure.

## Database

Check Docker/PostgreSQL status and the actual configured connection key. Do not paste a full connection string into logs or chat. Inspect the relevant SQL scripts before applying schema changes, and back up important data first.

## Provider failures

For live provider failures, distinguish invalid credentials, model availability, endpoint/network errors, rate limits, timeouts, response-format errors, and application validation errors. Re-run deterministic tests separately so network instability does not obscure local logic failures.

## Safe debugging

- Keep deterministic provider mode as the normal development default.
- Never print API keys or authorization headers.
- Do not change architecture to solve a single test failure without inspecting the failing test and intended boundary.
- After a fix, run the narrow test first, then the relevant project suite, then broader regression tests.
