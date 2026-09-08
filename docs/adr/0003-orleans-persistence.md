# ADR-0003: Orleans grain persistence — memory in dev, ADO.NET/PostgreSQL in deployment

**Status:** Accepted (Phase 1)

## Context
Each ticket is a stateful virtual actor (`ITicketGrain`) with durable state. Docker is not
available until later phases, and the grain state must nevertheless be persistent and
production-shaped.

## Decision
- Grain state uses the named storage provider `"ticketStore"`.
- Phase 1-2: `AddMemoryGrainStorage("ticketStore")` (local dev).
- Phase 3: switch registration to the Orleans ADO.NET storage provider backed by
  PostgreSQL (`Microsoft.Orleans.Persistence.AdoNet`, invariant `Npgsql`). No grain code
  changes — only host configuration.

## Why Orleans at all
The grain gives each ticket a **single-writer state machine**: transitions from
`TicketStatusTransitions` are validated exactly once, in order, without distributed locks.
This is the honest justification — not "because actors are trendy".

## Consequences
- Dev setup stays Docker-free (req. #8).
- `--storage` swap is a documented, one-line configuration change with a migration script.
