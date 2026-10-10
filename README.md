# AidenOps — AIOps Agent Swarm

AidenOps is an enterprise-oriented AI Operations (AIOps) platform for incident triage, knowledge retrieval, investigation, controlled tool execution, human approval, enterprise integrations, auditability, telemetry, and evaluation.

**Core principle:** AI can recommend an operational action, but the .NET control plane remains responsible for policy validation, approval, execution, and audit.

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

## Current development status

- Phases 1–14: recorded as complete in the project history.
- Phase 15: advanced evaluation improvements are in progress; do not treat every originally planned Phase 15 item as complete without checking the implementation.
- Latest recorded commit: `9d3941e` — `Add evaluation cost tracking and regression tests`.
- Latest recorded .NET test run: **503 passed, 0 failed, 0 skipped**.
- Latest recorded Python test run: **130 passed, 0 failed**, with one Starlette/AnyIO deprecation warning.
- Last recorded Git status: clean and synchronized with `origin/master`.

These are the latest recorded results from the development session, not a guarantee that the tests have been rerun against every later change.

## Current evaluation work

The latest recorded Phase 15 changes include:

- Estimated LLM evaluation cost from prompt/completion token counts and configured per-million-token prices.
- Optional input/output price metadata, with validation that both prices are supplied together and are non-negative.
- Persisted estimated cost and USD currency metadata when pricing is configured.
- Regression-comparison handling for `Hit@` metrics.
- Additional evaluation runner, metadata, and comparison regression tests.

Pricing-based cost is an estimate based on configured rates and reported token counts; it is not a provider invoice.

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
dotnet build AIOps.slnx
dotnet test AIOps.slnx
```

Run Python tests:

```powershell
Set-Location .\python
.\.venv\Scripts\Activate.ps1
pytest -q
```

For the full setup and safe development defaults, see [Development](docs/development/README.md).

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
