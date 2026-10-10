# Agent Workflow

## Responsibility boundary

The Python service runs the agent workflow through FastAPI and LangGraph. The .NET control plane remains authoritative for ticket lifecycle, policy, approval, tool execution, and audit.

## High-level flow

```text
Ticket / incident context
  -> Triage
  -> Knowledge retrieval and context
  -> Investigation
  -> Decision
  -> Resolve, escalate, or propose a tool
  -> .NET validation and policy
  -> Approval when required
  -> .NET tool execution
  -> Persisted result, audit, telemetry
  -> Resume or terminal outcome
```

## Triage

The triage layer produces structured fields such as domain, severity, and confidence. When a live model is used, the response is parsed and validated before it becomes an internal result. Deterministic mode supports repeatable development and tests.

Treat model output as untrusted input. A malformed response should become a controlled failure rather than an operational action.

## Knowledge retrieval

The knowledge subsystem is authoritative for retrieval. The documented data path is knowledge documents, chunks, embeddings, PostgreSQL/pgvector similarity search, and ranked results. Python consumes retrieved context through the knowledge boundary.

Do not describe the embedding implementation or retrieval ranking as production-grade beyond what current source and tests establish.

## Tool proposals and execution

A tool proposal is a candidate action, not proof that an action occurred. The server-provided tool manifest and server-side policy determine what can be considered. .NET validates proposals, determines approval requirements, and invokes the existing .NET tool executor.

Python must not execute privileged tools directly.

## Approval and resume

When approval is required, the control plane persists the approval/action state. Before resuming, the orchestrator validates the relevant ticket/action/approval state and records the request, decision, response, or failure in audit.

## Outcome semantics

Document the actual enum/contracts in source. The known logical outcomes include resolved, awaiting approval, escalated, and failed; confirm exact names and serialization before changing API documentation.
