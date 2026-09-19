# Autonomous IT Operations (AIOps) Agent Platform

A production-oriented portfolio project demonstrating multi-agent **Agentic AI**, **Generative AI / RAG**,
**data engineering**, and **full-stack .NET** engineering, built as a modular monolith:

- **.NET 10 control plane** — ASP.NET Core, Orleans (stateful Ticket grains), PostgreSQL, SignalR, Blazor (MudBlazor), EF Core.
- **Python AI agent layer (Phase 5+)** — LangGraph stateful workflows, LangChain components, NVIDIA hostedNIM
  (`moonshotai/kimi-k3`, OpenAI-compatible), human-in-the-loop interrupts, guardrails.
- **RAG / Knowledge Retrieval (Phase 7)** — knowledge documents and chunks, embedding abstraction,
  deterministic local embeddings for development/testing, PostgreSQL + pgvector, cosine-similarity retrieval,
  ranked knowledge results, and HNSW vector indexing.
- **Data engineering (Phase 11+)** — batch + API ingestion, validation, normalization, dedup, analytics SQL, versioned datasets.
- **Evaluation (Phase 12-13)** — versioned golden datasets, experiment tracking, classical ML baseline (TF-IDF + Logistic
  Regression) vs LLM triage on the same benchmark. **No performance numbers are claimed unless a committed experiment produced them.**

## Current status: Phase 7 — RAG / Knowledge Retrieval

Phase 1–6 capabilities remain in place, with Phase 7 adding the foundational knowledge retrieval layer.

### Phase 7 completed

- Knowledge document abstraction.
- Knowledge chunk abstraction.
- PostgreSQL persistence for knowledge documents and chunks.
- `pgvector` integration through EF Core.
- Embedding generation abstraction via `IEmbeddingGenerator`.
- Deterministic 64-dimensional local embedding implementation for development and testing.
- Cosine-similarity vector search.
- Ranked knowledge retrieval results.
- PostgreSQL HNSW vector index using `vector_cosine_ops`.
- Foreign-key relationship between documents and chunks with cascade deletion.
- Unique document/chunk ordering constraint.
- End-to-end PostgreSQL knowledge-store integration test.
- Existing Phase 1–6 behavior preserved.

The Phase 7 RAG foundation follows this flow:

```text
Knowledge Document
        ↓
Document Chunks
        ↓
Embedding Vector
        ↓
PostgreSQL + pgvector
        ↓
Similarity Retrieval
        ↓
Ranked Knowledge Context
        ↓
Agent Gateway / Orchestrator