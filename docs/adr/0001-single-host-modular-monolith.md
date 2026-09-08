# ADR-0001: Single-host modular monolith (.NET) + separate Python AI service

**Status:** Accepted (Phase 1)

## Context
Portfolio project must demonstrate production-grade architecture without unnecessary
deployment complexity. Target roles: AI, Agentic AI, Data Engineering, Full-Stack, .NET.

## Decision
- One ASP.NET Core application (`AIOps.Host`) hosts: REST APIs, SignalR hub, Blazor Web App
  (MudBlazor), and the co-hosted Orleans silo.
- Internal boundaries are enforced by projects: `Domain` (no deps) ← `Contracts`,
  `Abstractions` (all seams) ← `Grains`, `Orchestration`, `Tools`, `Infrastructure` → `Host`.
- The AI agent layer is a separate **Python** service (starting Phase 5) behind the
  `IAgentGateway` contract. Everything else stays in the monolith.

## Consequences
- Single `dotnet run` for the entire platform in development.
- No Kubernetes, no service mesh, no extra brokers. Scaling concerns documented, not solved.
- Boundary discipline enforced by project references and code review.
