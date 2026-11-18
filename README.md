# AidenOps — AIOps Agent Swarm

AidenOps is an enterprise-oriented AI Operations (AIOps) platform for incident triage, knowledge retrieval, investigation, controlled tool execution, human approval, enterprise integrations, auditability, telemetry, and evaluation.

**Core principle:** AI can recommend an operational action, but the .NET control plane remains responsible for policy validation, approval, execution, and audit.

## Portfolio-ready MVP

The guided `/demo` page shows the current end-to-end sandbox: create a deterministic incident, inspect persisted ticket activity, and walk a simulated infrastructure change through approval or rejection. The page also shows only evaluation runs returned by the persisted-run comparison API; an empty store is shown as empty rather than filled with example results.

### Local setup and launch

Prerequisites: .NET SDK 10.0.400 or later within the selected feature band, Python 3.12, Docker, and PostgreSQL client tools. Start PostgreSQL and apply the database scripts once to a clean local database, following the [development setup guide](docs/development/README.md):

```powershell
docker compose up -d postgres
```

In a Python terminal:

```powershell
Set-Location .\python
python -m venv .venv
.\.venv\Scripts\Activate.ps1
pip install -r requirements.txt
$env:AGENT_LLM_PROVIDER = "deterministic"
uvicorn agent_service.main:app --host 127.0.0.1 --port 8000
```

In a second terminal at the repository root:

```powershell
$env:ASPNETCORE_ENVIRONMENT = "Development"
$env:ConnectionStrings__PostgresConnection = Read-Host 'Development PostgreSQL connection string'
$env:AI__AgentGatewayMode = "Http"
dotnet run --project .\src\AIOps.Host --launch-profile http
```

Open [http://localhost:5166/demo](http://localhost:5166/demo). The Python service is configured to use deterministic inference; no external LLM credentials are needed. For database schema ordering and isolated test databases, see the [development setup guide](docs/development/README.md).

### Demo walkthrough

1. Select **Run sandbox incident** to create and investigate the deterministic VPN incident using the simulated diagnostic provider. Follow the ticket activity API link for the persisted activity.
2. Select **Start approval scenario**. The .NET control plane creates an approval request for the registered simulated `Cloud.RestartInstance` action.
3. Choose **Approve simulated action** to exercise approval, simulated execution, and resume, or **Reject action** to verify that rejection does not execute the action. To show both outcomes, start a separate approval scenario for the second decision.
4. Review the persisted chronological activity timeline and the evaluation-results panel. Evaluation results appear only when real persisted runs are available; the comparison API link is a secondary raw-data view.

External cloud, VPN, directory, and ITSM actions in the demo are simulated and guarded by server-side provider checks. The demo does not perform real infrastructure changes, provider writes, or live incident remediation. The `X-Dev-User` identity is a local Development-only convenience, not production authentication. Non-Development environments reject development-identity configuration and return 401 for protected requests until a supported identity provider is explicitly integrated and verified. See [authentication and authorization](docs/security/README.md).

### Screenshot and video guidance

Capture `/demo` with the provider simulation notice visible, then show the incident result, a pending approval, and either the approved/resumed or rejected/no-execution timeline. To demonstrate both decision paths, record two separate approval scenarios. Use a disposable local database, avoid including credentials or unrelated ticket data, and label any recorded UI as a simulated sandbox. Do not edit evaluation values into screenshots: an empty evaluation panel accurately indicates that no persisted results are available.

## Architecture

```text
Operator / Ticket Source
          |
          v
ASP.NET Core UI / API
          |
          v
.NET Control Plane
  - Orleans orchestration
  - Ticket lifecycle
  - Safety and approval policy
  - Tool registry and execution
  - Audit and telemetry
          |
          +--------------------+
          |                    |
          v                    v
Python Agent Service      PostgreSQL + pgvector
FastAPI + LangGraph       Knowledge, audit, evaluation
          |
          v
Provider-independent LLM layer
  Deterministic | OpenRouter | NVIDIA NIM
  OpenAI | Anthropic | Google | Azure OpenAI
```

Python handles agent workflow and proposals. Privileged tools are executed through .NET after server-side validation and approval checks.

## Verified current status

- Phases 1–14: recorded as complete in the project history.
- Phase 15: evaluation capabilities are implemented incrementally; trusted baseline/candidate enforcement is not implemented.
- Repository HEAD verified on 2026-11-18: `0a57e78` — `Complete Phase 15 evaluation acceptance checkpoint`.
- Latest local Release .NET test run: **615 passed, 0 failed, 12 skipped**. The skips are opt-in PostgreSQL integration tests without their isolated local connection settings.
- Latest local Python test run: **164 passed, 0 failed**, with one Starlette multipart deprecation warning.
- These are local verification results, not a production-release claim or proof of live provider operation.

## Current evaluation work

The latest recorded Phase 15 changes include:

- Estimated LLM evaluation cost from prompt/completion token counts and configured per-million-token prices.
- Optional input/output price metadata, with validation that both prices are supplied together and are non-negative.
- Persisted estimated cost and USD currency metadata when pricing is configured.
- Regression-comparison handling for `Hit@` metrics.
- Additional evaluation runner, metadata, and comparison regression tests.
- Evaluation runners do not execute tools or synthesize successful approval/execution outcomes.
- The LLM evaluation boundary verifies that prediction IDs match the submitted cases.

Pricing-based cost is an estimate based on configured rates and reported token counts; it is not a provider invoice.

## Authentication and deployment limitations

`X-Dev-User` is a Development-only local demo mechanism. It is enabled only by
`appsettings.Development.json`. In Production, Staging, or another
non-Development environment, the Host registers an unconfigured authentication
handler and protected endpoints fail closed with `401` until a real identity
provider is integrated. OIDC/Microsoft Entra authentication is not implemented.

The repository does not provide a production hosting topology, managed secret
store, monitoring/alerting backend, backup/recovery process, or verified live
provider deployment. Provider adapters and mock-tested behavior are boundaries
for future production work, not evidence of a production rollout.

## Technology stack

- **.NET:** .NET 10, ASP.NET Core, Microsoft Orleans, Entity Framework Core, Npgsql
- **Python:** Python 3.12, FastAPI, LangGraph, Pydantic, httpx, pytest
- **Data:** PostgreSQL and pgvector
- **Testing:** xUnit and pytest

## Repository documentation

- [Documentation index](docs/README.md)
- [Architecture and control-plane boundaries](docs/architecture/README.md)
- [Development setup and test commands](docs/development/README.md)
- [Evaluation and Phase 15](docs/evaluation/README.md)
- [LLM providers](docs/providers/README.md)
- [Enterprise integrations](docs/integrations/README.md)
- [Security and credentials](docs/security/README.md)
- [Phase history and roadmap](docs/phases/README.md)

## Quick verification

Run from the repository root:

```powershell
dotnet build AIOps.slnx --configuration Release
dotnet test AIOps.slnx --configuration Release
```

Run Python tests:

```powershell
Set-Location .\python
.\.venv\Scripts\Activate.ps1
python -m pytest -q
```

Check the browser module syntax from the repository root:

```powershell
node --check --experimental-default-type=module .\src\AIOps.Host\Components\Pages\Demo.razor.js
```

For the full setup and safe development defaults, see [Development](docs/development/README.md).

## Local Development and Connectivity Verification

Start the local PostgreSQL service from the repository root:

```powershell
docker compose up -d postgres
```

PostgreSQL is exposed on port `5432`. Run the Python service from the `python` directory using the documented command:

```powershell
uvicorn agent_service.main:app --host 127.0.0.1 --port 8000
```

Its health endpoint is `http://127.0.0.1:8000/health`. The .NET host was verified at `http://127.0.0.1:5166`; `GET /api/v1/meta/config` reported `agentGateway: Http`. The committed `src/AIOps.Host/appsettings.json` default remains `Fake`; HTTP mode was enabled for the local running process with `AI__AgentGatewayMode=Http`.

A disposable connectivity probe confirmed that the .NET gateway successfully invoked the Python agent service. NVIDIA NIM credentials are optional for this deterministic local connectivity check. This verifies service connectivity only; real incident resolution, NVIDIA inference, and external provider integrations were not verified.

## Safety boundary

```text
LLM reasoning
     |
     v
Tool proposal
     |
     v
.NET policy and schema validation
     |
     +---- approval required? ----+
     |                            |
     v                            v
Approved/safe execution       Human decision
     |                            |
     +-------------+--------------+
                   v
            .NET ToolExecutor
                   |
                   v
             Result + audit
```

A proposal is not evidence that a tool ran. Python does not directly execute privileged .NET tools.

## Project direction

The project preserves the existing .NET + Python architecture. The next work should extend the evaluation system and improve reproducibility, comparison, reporting, and verification without reimplementing completed phases or overstating unverified live integrations.
