# ADR-0004: UI library — MudBlazor

**Status:** Accepted (Phase 1)

## Context
Per the plan, MudBlazor is adopted only if it restores and renders on .NET 10 Blazor Web App.

## Decision
MudBlazor 9.9.0 restored cleanly against `net10.0` and its providers are wired into
`MainLayout.razor` with a working interactive landing page. **The compatibility check passed.**

## Consequences
- All dashboard pages use MudBlazor components.
- Fallback (plain Blazor + CSS) recorded here but not needed.
