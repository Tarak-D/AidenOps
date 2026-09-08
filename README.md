# Autonomous IT Operations (AIOps) Agent Platform

A production-oriented portfolio project demonstrating multi-agent **Agentic AI**, **Generative AI / RAG**,
**data engineering**, and **full-stack .NET** engineering, built as a modular monolith:

- **.NET 10 control plane** — ASP.NET Core, Orleans (stateful Ticket grains), PostgreSQL, SignalR, Blazor (MudBlazor), EF Core.
- **Python AI agent layer (Phase 5+)** — LangGraph stateful workflows, LangChain components, NVIDIA hosted NIM
  (`moonshotai/kimi-k3`, OpenAI-compatible), human-in-the-loop interrupts, guardrails.
- **Data engineering (Phase 11+)** — batch + API ingestion, validation, normalization, dedup, analytics SQL, versioned datasets.
- **Evaluation (Phase 12-13)** — versioned golden datasets, experiment tracking, classical ML baseline (TF-IDF + Logistic
  Regression) vs LLM triage on the same benchmark. **No performance numbers are claimed unless a committed experiment produced them.**

## Current status: Phase 1 — Foundation

Solution skeleton, project boundaries, domain model + ticket state machine rules, platform abstractions
(agent gateway, audit, tools, integrations, notifications), dev authentication + authorization policies,
Orleans co-hosted silo with in-memory grain storage, Serilog structured logging, MudBlazor Blazor shell,
`/health` + `/api/v1/meta/*` endpoints, and the first unit/contract tests.

## Run (development)

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/AIOps.Host
```

Then open https://localhost:7xxx (see console), `/health`, `/api/v1/meta/selfcheck`, `/api/v1/meta/config`.

Dev identity: send header `X-Dev-User: viewer|engineer|approver|admin` to simulate roles (dev-only mechanism).

## Secrets

No secrets in this repository. Configure the NVIDIA NIM key via environment variable or user-secrets:

```powershell
cd src/AIOps.Host
dotnet user-secrets init
dotnet user-secrets set "AI:NvidiaNim:ApiKey" "nvapi-..."
# or environment variable: AI__NvidiaNim__ApiKey
```

## Architecture & decisions

See `docs/adr/` (architecture decision records) and `docs/architecture/` (added in later phases).

**Honesty policy:** this system is *designed with* auditability, least privilege, and human-in-the-loop
governance in mind. It is not SOC 2 / ISO / anything certified. All infrastructure actions are simulated.
