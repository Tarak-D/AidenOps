# Data and Persistence

## PostgreSQL

PostgreSQL is the persistent store described by the project architecture. The documented data areas include:

- Audit records.
- Knowledge documents and chunks.
- Action execution state.
- Approval requests/state.
- Evaluation runs and results.

Check the current EF Core model, migrations/schema scripts, and `AIOpsDbContext` before assuming that every listed area is persisted in the same way or is fully implemented.

## pgvector and retrieval

The documented retrieval path uses vectors stored with PostgreSQL/pgvector and similarity search. The historical README describes cosine distance and HNSW indexing and a deterministic embedding generator. Confirm the current schema, index creation scripts, and embedding code before changing these claims.

## Audit

Audit records are the record of what the control plane did; a model-generated explanation is not proof of execution. Historical audit fields included correlation ID, entity ID/type, event type, timestamp, payload, and sequence. Use the current entity definition for the canonical field list.

## Database scripts

The repository contains root-level PostgreSQL scripts, including scripts for phases 6, 7, 14, and 15. Review the scripts in order and follow the project's actual database setup process. Do not blindly execute a phase script against a database containing important data.

## Configuration and secrets

Never put real credentials into documentation or committed configuration. Use local environment configuration and the checked-in example file with blank values. Do not paste connection strings or API keys into logs or chat.
