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

- Repository HEAD: `2e7aaf2`
- Commit message: `Add scikit-learn evaluation dependency`
- Current working-tree verification: .NET 509 passed, 0 failed, 0 skipped; Python 159 passed, 0 failed, with one Starlette/AnyIO deprecation warning.
- These results include uncommitted audit changes and are not a release checkpoint.

These results are historical checkpoint results. Rerun the test suites to establish the current results after documentation changes.

## Phase 15: Evaluation progress

The latest recorded work includes evaluation pricing metadata, estimated token-based cost calculation, cost persistence, and regression tests.

The broader evaluation roadmap still requires verification and may include:

- Versioned golden datasets.
- Experiment and run tracking.
- A TF-IDF + Logistic Regression baseline.
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
