# ADR-0002: Python + LangGraph AI layer behind an agent gateway

**Status:** Accepted (Phase 1)

## Context
The agentic workflow (conditional routing, retries, interrupts, tool proposals) is best
expressed with LangGraph, which is Python-first. The .NET platform must not depend on
LangGraph internals.

## Decision
- `IAgentGateway` is the **only** boundary: `StartRunAsync` / `ResumeRunAsync` with
  versioned DTOs (`AIOps.Contracts.AgentGateway`, mirrored as pydantic models in the
  Python service).
- Dev default: `InProcessFakeAgentGateway` — deterministic heuristics, offline, used by
  tests/CI/demos. Production mode (Phase 5): `HttpAgentGateway` → FastAPI + LangGraph.
- **The Python service never executes tools and never touches the database.**
  It reasons, retrieves, and proposes; the .NET control plane decides and executes.

## Consequences
- Agent reasoning can be replaced (other framework, other language) without touching .NET.
- .NET unit/integration tests never require Python or network access.
- Contract schema documented in `docs/architecture/agent-gateway-contract.md` (Phase 4).
