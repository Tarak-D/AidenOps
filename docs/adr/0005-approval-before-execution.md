# ADR-0005: Approval-before-execution is enforced in .NET, never in the LLM

**Status:** Accepted (Phase 1)

## Context
AI agents propose actions; nothing an LLM emits is trusted. Sensitive actions
(password reset, access grants, instance restarts) must never execute without human approval.

## Decision
- Agents may only **propose** tools via structured output referencing the
  `AllowedTools` manifest the .NET control plane supplies.
- `ToolExecutor` (.NET, AIOps.Tools/Orchestration) refuses execution unless:
  1. input validates against the tool's schema, and
  2. for `Risk >= Moderate`, a **bound, unexpired, Approved** `ApprovalRequest` exists —
     bound by FK to the exact `ActionExecution` id (not an agent-claimed flag).
- Approvals are decided via REST (`Approver` role), never via SignalR messages.
- Every step is written to the append-only audit store.

## Consequences
- A compromised or hallucinating agent physically cannot trigger sensitive actions.
- Unit tests assert refusal paths as first-class behavior.
- All tools are simulations; no real infrastructure integration exists in this repository.
