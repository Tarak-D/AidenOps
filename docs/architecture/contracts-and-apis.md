# Contracts and API Boundaries

This document summarizes the intended boundary. Verify exact routes, DTO property names, validation rules, and response codes in the current source before treating examples as an API specification.

## .NET to Python

The .NET control plane calls the Python FastAPI agent service through an agent-gateway abstraction. The orchestration layer should depend on the gateway contract, not on LangGraph or a particular LLM provider.

The historical contract family includes:

- `AgentRunRequest`
- `AgentTicketContext`
- `ToolManifestEntry`
- `ToolProposal`
- `StepTrace`
- `AgentRunOutcome`
- `AgentRunResult`
- `ResumeAgentRunRequest`

Names may change; use the definitions under `src/AIOps.Contracts` and `src/AIOps.Abstractions` as authoritative.

## Tool proposal

The documented proposal fields include tool name, JSON arguments, confidence, and justification. A proposal is not an execution record. Server-side schema validation, risk policy, approval policy, and execution policy must run before a consequential action is executed.

## Resume flow

The documented resume data includes correlation/ticket identity, approval decision information, tool result data, and execution success. Exact field names and optionality must be checked against the current contracts.

## Python service

The historical local development route is the health endpoint at `/health`. The agent-run and resume routes are implemented by the Python service and .NET gateway, but confirm the exact current paths and request schemas in `python/agent_service/main.py` and the gateway implementation before publishing curl examples.

## API documentation checklist

Before changing this page, verify:

1. Exact route and HTTP method.
2. Request and response schemas.
3. Authentication and network exposure.
4. Error response behavior.
5. Timeout and retry behavior.
6. Whether the route is public, internal, or development-only.
7. Whether tests cover the documented behavior.
