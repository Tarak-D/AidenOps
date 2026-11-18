# Phase History and Roadmap

This page summarizes the milestone history recorded in the project documentation. Completion claims should be verified against the source code, tests, and Git history before release.

## Milestone history

| Phase | Scope recorded in prior README |
|---|---|
| 1 | Foundation and solution structure |
| 2 | Ticket lifecycle |
| 3 | Persistence and audit |
| 4 | Tool execution |
| 5 | Agent gateway |
| 6 | Human approval and safety controls |
| 7 | RAG / knowledge retrieval |
| 8 | Evaluation and observability foundation |
| 9 | Python / LangGraph agent workflow |
| 10 | Multi-provider LLM integration |
| 11 | RAG context injection / reasoning |
| 12 | Tool Proposal Agent |
| 13 | Approval-aware execution resume |
| 14 | Production integration boundaries, credential isolation, and telemetry |
| 15 | Advanced evaluation and ML + LLM evaluation |

The previous README marked Phases 1-14 complete. That historical statement is not a substitute for a fresh implementation audit.

## Latest recorded checkpoint

- Historical checkpoint: `2e7aaf2` — `Add scikit-learn evaluation dependency`.
  Its recorded test counts are historical only and must not be used as current
  verification.
- Current repository checkpoint verified on 2026-11-18: `0a57e78` — `Complete
  Phase 15 evaluation acceptance checkpoint`.
- Current local verification: .NET Release **615 passed, 0 failed, 12 skipped**;
  Python **164 passed, 0 failed**, with one Starlette multipart deprecation
  warning. PostgreSQL skips are opt-in local integrations requiring isolated
  connection environment variables.
- These are local results, not a release checkpoint, a production-readiness
  claim, or proof of live provider integration.

Rerun the test suites after code, dependency, environment, or documentation
changes to establish a new current result.

## Phase 15: Evaluation progress

The latest recorded work includes evaluation pricing metadata, estimated token-based cost calculation, cost persistence, and regression tests.

The classical TF-IDF + Logistic Regression baseline already exists with fixed,
ordered training and evaluation corpora. Its metadata and aligned prediction
report are covered by source and tests; a .NET runner records runs through the
existing evaluation store and comparison pipeline. This does not establish a
managed dataset registry or independent generalization study.

Other evaluation roadmap work still requires verification and may include:

- Expanded LLM triage and end-to-end evaluation.
- Provider, model, and prompt comparisons.
- Persisted regression reporting and defined metric thresholds.

The classical baseline uses a fixed training corpus and an evaluation corpus
with close semantic overlap. No explicit train/test split or independent
generalization study is established, so its score is not a generalization
claim. Evaluation runners do not execute tools; when the server-side execution
lifecycle was not run, execution outcomes remain unknown.

Do not mark these roadmap items complete until the implementation and tests demonstrate them.

## Documentation maintenance

Update this page when a milestone is verified. Record the relevant commit, test results, and any remaining limitations. Distinguish implemented features from planned work.
