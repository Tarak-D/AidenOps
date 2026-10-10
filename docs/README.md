# AidenOps Documentation

This index separates the project overview from implementation details. Treat code and current configuration as authoritative when they disagree with documentation.

## Main guides

- [Architecture](architecture/README.md) — system boundaries and request flow.
- [Agent workflow](architecture/agent-workflow.md) — triage, retrieval, investigation, decision, proposals, and resume.
- [Contracts and APIs](architecture/contracts-and-apis.md) — .NET/Python boundary and agent-run contracts.
- [Data and persistence](architecture/data-and-persistence.md) — PostgreSQL, pgvector, audit, actions, approvals, and evaluation.
- [Development setup](development/README.md) — build and test commands.
- [Troubleshooting](development/troubleshooting.md) — common local-development failures.
- [LLM providers](providers/README.md) — provider abstraction and deterministic mode.
- [Provider configuration](providers/provider-configuration.md) — environment configuration guidance.
- [Live provider testing](providers/live-provider-testing.md) — optional network-backed smoke tests.
- [Evaluation](evaluation/README.md) — evaluation foundation and current checkpoint.
- [Evaluation design](evaluation/evaluation-design.md) — metrics and planned experiment workflow.
- [Integrations](integrations/README.md) — integration boundaries.
- [Integration status](integrations/integration-status.md) — distinguish interface, simulated, and live verification.
- [Security](security/README.md) — approval, execution, and secrets.
- [Credential isolation](security/credential-isolation.md) — secret-handling principles.
- [Operational telemetry](security/operational-telemetry.md) — safe instrumentation.
- [Phase history](phases/phase-history.md) — phases 1–15 status and scope.
- [Phase roadmap](phases/README.md) — current milestone and remaining work.

## Architecture decision records

The original records remain under [`adr/`](adr/):

- `0001-single-host-modular-monolith.md`
- `0002-python-langgraph-ai-boundary.md`
- `0003-orleans-persistence.md`
- `0004-ui-library.md`
- `0005-approval-before-execution.md`

## Accuracy rule

Test counts and commit IDs are checkpoint-specific, not permanent guarantees. Re-run the documented commands before publishing fresh results. Provider names or integration interfaces do not, by themselves, prove that a live external service was successfully exercised.
