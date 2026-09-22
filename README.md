# AIOps Agent Swarm

AIOps Agent Swarm is an enterprise-oriented AI operations platform for incident triage, knowledge retrieval, reasoning, safe operational tool execution, human approval, auditability, evaluation, and controlled autonomy.

The project deliberately separates **AI reasoning** from **operational authority**.

The current architecture is:

```text
ASP.NET Core / UI
        │
        ▼
.NET Control Plane
(Orleans + orchestration + safety + tools + audit)
        │
        ├──────────────► PostgreSQL + pgvector
        │
        ▼
Python Agent Service
(FastAPI + LangGraph)
        │
        ▼
Provider-independent LLM layer
        │
        ├── deterministic
        ├── OpenRouter
        ├── NVIDIA NIM
        ├── OpenAI
        ├── Anthropic
        ├── Google
        └── Azure OpenAI
```

The model can recommend an action, but the model is not allowed to become the authority that executes a consequential operational action.

---

# 1. Current Project Status

## Completed phases

```text
Phase 1  — Foundation
Phase 2  — Ticket Lifecycle
Phase 3  — Persistence & Audit
Phase 4  — Tool Execution
Phase 5  — Agent Gateway
Phase 6  — Human Approval & Safety Controls
Phase 7  — RAG / Knowledge Retrieval
Phase 8  — Agent Evaluation & Observability
Phase 9  — Python / LangGraph Agent Swarm
```

## Current phase

```text
Phase 10 — Multi-Provider LLM Integration
STATUS: IN PROGRESS
```

Phase 10 introduced the provider-independent Python LLM abstraction and support for multiple providers.

The current Phase 10 work includes:

```text
LLM client abstraction
OpenAI-compatible provider abstraction
NVIDIA NIM client
OpenRouter client
OpenAI client
Google client
Azure OpenAI client
Anthropic client
Provider/model environment configuration
Structured JSON LLM responses
LLM token/latency tracing
LLM-specific tests
Deterministic regression preservation
```

The live NVIDIA NIM test was attempted but timed out.

OpenRouter was selected as the next live-provider test target. The repository should not claim a successful OpenRouter live run until that smoke test has actually completed successfully.

## Last completed Git checkpoint

```text
phase-9-complete
```

Latest completed Phase 9 commit:

```text
6a15a32 Implement Phase 9 Python LangGraph agent swarm
```

Phase 10 changes are the active development work after that checkpoint.

---

# 2. Project Vision

The long-term goal is a controlled autonomous AIOps workflow:

```text
Incident
   ↓
Ticket
   ↓
Ticket Grain
   ↓
Agent Run
   ↓
Triage
   ↓
Knowledge Retrieval
   ↓
Reasoning / Investigation
   ↓
Tool Proposal
   ↓
Risk Classification
   ↓
Approval if required
   ↓
Tool Execution
   ↓
Execution Result
   ↓
Verification
   ↓
Agent Re-evaluation
   ↓
Resolution / Escalation
   ↓
Audit
   ↓
Evaluation
   ↓
Observability / UI
```

The platform is designed around these goals:

```text
Safe autonomy
Provider independence
Deterministic testing
Persistent audit
Repeatable evaluation
Human approval for consequential actions
Separation of inference and execution
```

---

# 3. Most Important Architectural Rule

The system follows:

```text
AI decides what it recommends.
Control plane decides what is allowed.
Tools execute only through the control plane.
Audit records what actually happened.
```

A model output such as:

```text
Restart EC2 instance i-123456
```

is only a proposal.

It is not evidence that the instance was restarted.

The actual execution path is:

```text
LLM
 ↓
Agent proposal
 ↓
.NET control plane
 ↓
Risk / policy validation
 ↓
Approval if required
 ↓
.NET tool execution
 ↓
Execution result
 ↓
Audit
```

This boundary is preserved throughout the architecture.

---

# 4. Architecture

## 4.1 Current logical architecture

```text
┌──────────────────────────────────────────────────────────────┐
│                         User / Operator                      │
│                    UI / API / Ticket Source                  │
└──────────────────────────────┬───────────────────────────────┘
                               │
                               ▼
┌──────────────────────────────────────────────────────────────┐
│                       .NET Control Plane                      │
│                                                              │
│ ASP.NET Core                                                │
│ Razor Components                                             │
│ SignalR                                                      │
│ Orleans                                                      │
│ Ticket lifecycle                                             │
│ Agent orchestration                                          │
│ Approval / safety policy                                     │
│ Tool registry                                                │
│ Tool execution                                               │
│ Audit                                                        │
│ Evaluation                                                   │
└───────────────┬───────────────────────────┬──────────────────┘
                │                           │
                │ HTTP                      │ PostgreSQL
                ▼                           ▼
┌──────────────────────────────┐   ┌────────────────────────────┐
│      Python Agent Service    │   │         PostgreSQL          │
│                              │   │                            │
│ FastAPI                      │   │ Audit                       │
│ LangGraph                   │   │ Evaluation                  │
│ TriageAgent                 │   │ Knowledge documents         │
│ Knowledge boundary          │   │ Knowledge chunks            │
│ InvestigationAgent          │   │ pgvector                    │
│ DecisionAgent               │   │ Action executions            │
│ LLM provider abstraction    │   │ Approval requests            │
└──────────────┬───────────────┘   └────────────────────────────┘
               │
               ▼
┌──────────────────────────────────────────────────────────────┐
│                    LLM Provider Layer                        │
│                                                              │
│ deterministic                                                │
│ OpenRouter                                                   │
│ NVIDIA NIM                                                   │
│ OpenAI                                                       │
│ Anthropic                                                    │
│ Google                                                       │
│ Azure OpenAI                                                 │
└──────────────────────────────────────────────────────────────┘
```

---

# 5. Architectural Evolution

The architecture changed significantly during implementation.

## Initial AI direction

The original direction was centered around:

```text
.NET
   ↓
Python / LangGraph
   ↓
NVIDIA NIM / Nemotron
```

Phase 10 initially implemented NVIDIA NIM support.

During live testing, the NVIDIA endpoint did not complete within the configured timeout.

The architecture was therefore expanded instead of making the system dependent on one provider.

## Current direction

The Python service now uses:

```text
Agent logic
    ↓
Provider-independent LLM client
    ↓
Selected provider
```

The provider is selected using environment configuration:

```text
AGENT_LLM_PROVIDER
```

The model is selected using:

```text
AGENT_LLM_MODEL
```

Provider-specific model variables remain supported.

This means the agent logic does not need to know whether the model came from:

```text
OpenRouter
NVIDIA NIM
OpenAI
Anthropic
Google
Azure OpenAI
```

This is a major architectural improvement because provider availability, latency, cost, model capability, and operational reliability can change without rewriting the agent workflow.

---

# 6. Repository Structure

```text
AIOps.AgentSwarm/
│
├── src/
│   ├── AIOps.Abstractions/
│   │   ├── Agents/
│   │   ├── Audit/
│   │   ├── Configuration/
│   │   ├── Evaluation/
│   │   ├── Integrations/
│   │   ├── Knowledge/
│   │   ├── Persistence/
│   │   └── Time/
│   │
│   ├── AIOps.Contracts/
│   │   ├── AgentGateway/
│   │   └── Api/
│   │
│   ├── AIOps.Domain/
│   │
│   ├── AIOps.Grains/
│   │
│   ├── AIOps.Host/
│   │
│   ├── AIOps.Infrastructure/
│   │   ├── Agents/
│   │   ├── Audit/
│   │   ├── EfCore/
│   │   ├── Evaluation/
│   │   ├── Integrations/
│   │   ├── Knowledge/
│   │   ├── Persistence/
│   │   └── Time/
│   │
│   └── AIOps.Orchestration/
│
├── tests/
│   ├── AIOps.Abstractions.Tests/
│   ├── AIOps.Api.Tests/
│   ├── AIOps.Domain.Tests/
│   ├── AIOps.Grains.Tests/
│   ├── AIOps.Infrastructure.Tests/
│   ├── AIOps.Orchestration.Tests/
│   └── AIOps.Tools.Tests/
│
├── python/
│   ├── requirements.txt
│   ├── .env.example
│   └── agent_service/
│       ├── __init__.py
│       ├── main.py
│       ├── graph.py
│       ├── llm.py
│       ├── agents/
│       │   ├── __init__.py
│       │   └── triage.py
│       ├── models/
│       │   ├── __init__.py
│       │   └── agent.py
│       ├── retrieval/
│       │   ├── __init__.py
│       │   └── service.py
│       ├── tools/
│       │   └── __init__.py
│       └── tests/
│           ├── __init__.py
│           ├── test_health.py
│           ├── test_triage.py
│           ├── test_graph.py
│           └── test_llm.py
│
└── README.md
```

---

# 7. Technology Stack

## .NET

```text
.NET 10
ASP.NET Core
Razor Components
SignalR
Microsoft Orleans
Entity Framework Core
Npgsql
pgvector
xUnit
```

## Python

```text
Python 3.12
FastAPI
Uvicorn
LangGraph
Pydantic
httpx
pytest
```

## Database

```text
PostgreSQL
pgvector
```

## AI

```text
Deterministic provider
OpenRouter
NVIDIA NIM
OpenAI
Anthropic
Google
Azure OpenAI
```

---

# 8. Phase 1 — Foundation

Phase 1 established the initial solution structure.

The project was separated into:

```text
Domain
Contracts
Abstractions
Infrastructure
Orchestration
Host
Tests
```

Initial operational functionality included:

```text
ASP.NET Core host
Health endpoint
Application shell
Initial project boundaries
Initial test structure
```

Checkpoint:

```text
Phase 1 complete
```

---

# 9. Phase 2 — Ticket Lifecycle

Phase 2 introduced the operational ticket lifecycle.

The system models ticket state and transitions through the control-plane/application layer.

The lifecycle supports states such as:

```text
New
Triaging
WaitingForApproval
Executing
Resolved
Escalated
```

Ticket processing is kept separate from AI provider implementation.

Checkpoint:

```text
Phase 2 complete
```

---

# 10. Phase 3 — Persistence & Audit

Phase 3 introduced persistent operational state.

PostgreSQL became the persistence foundation.

The system introduced:

```text
Entity Framework Core
Npgsql
PostgreSQL
Audit records
Correlation IDs
Persistent evaluation records
```

Audit records provide a durable record of important operations.

Important audit information includes:

```text
correlation_id
entity_id
entity_type
event_type
occurred_at
payload
sequence
```

The audit layer is important because an AI explanation alone cannot establish what actually happened.

Checkpoint:

```text
Phase 3 complete
```

---

# 11. Phase 4 — Tool Execution

Phase 4 introduced operational tools.

Tools are registered through a tool registry.

Current tool examples include:

```text
GetInstanceStatus
RestartInstance
ResetPassword
GrantGroupAccess
UpdateTicket
RunVpnDiagnostics
```

Tool metadata includes:

```text
Name
Description
Risk
RequiresApproval
Input schema
```

The AI layer does not receive unrestricted direct execution authority.

Checkpoint:

```text
Phase 4 complete
```

---

# 12. Phase 5 — Agent Gateway

Phase 5 established the stable AI gateway contract.

Core abstraction:

```text
IAgentGateway
```

Operations:

```text
StartRunAsync(...)
ResumeRunAsync(...)
```

Shared contracts include:

```text
AgentRunRequest
AgentTicketContext
ToolManifestEntry
ToolProposal
StepTrace
AgentRunResult
ResumeAgentRunRequest
AgentRunOutcome
```

The gateway allows the control plane to communicate with different agent implementations.

Two implementations exist:

```text
InProcessFakeAgentGateway
PythonAgentGateway
```

The fake gateway is retained for deterministic local development and CI.

Checkpoint:

```text
phase-5-complete
```

---

# 13. Phase 6 — Human Approval & Safety Controls

Phase 6 added the safety boundary.

The system introduced:

```text
ActionExecution
ApprovalRequest
Approval lifecycle
Risk classification
Server-side validation
Approval persistence
Approval API
Audit events
```

The core safety rule is:

```text
LLM proposal != execution
```

A consequential proposal can become:

```text
AwaitingApproval
```

A human decision is then recorded by the control plane.

Possible approval states include:

```text
Approved
Rejected
Expired
```

The Python agent is not allowed to approve its own privileged action.

Checkpoint:

```text
phase-6-complete
```

---

# 14. Phase 7 — RAG / Knowledge Retrieval

Phase 7 introduced PostgreSQL-backed knowledge retrieval.

The RAG subsystem includes:

```text
Knowledge documents
Knowledge chunks
Deterministic embeddings
pgvector
Cosine similarity
HNSW index
Knowledge persistence
Knowledge search
```

Core abstractions include:

```text
IEmbeddingGenerator
IKnowledgeStore
KnowledgeDocument
KnowledgeChunk
KnowledgeSearchResult
```

The initial embedding implementation is deterministic.

This allows reproducible tests without requiring an external embedding provider.

The PostgreSQL vector index uses:

```text
HNSW
vector_cosine_ops
```

The .NET/PostgreSQL RAG implementation remains the authoritative RAG system.

Checkpoint:

```text
phase-7-complete
```

---

# 15. Phase 8 — Agent Evaluation & Observability

Phase 8 introduced repeatable evaluation.

The evaluation framework includes:

```text
Evaluation cases
Evaluation datasets
Evaluation runner
Evaluation metrics
Evaluation persistence
Agent trace aggregation
Latency measurement
Token measurement
Tool validation
Approval metrics
Resolution metrics
Escalation metrics
Repeatability tests
```

Evaluation records are stored in PostgreSQL.

This framework remains important for future provider/model changes.

A new provider should be evaluated against repeatable cases rather than judged only from a single live request.

Checkpoint:

```text
phase-8-complete
```

---

# 16. Phase 9 — Python / LangGraph Agent Swarm

Phase 9 introduced the real Python agent service.

The Python service uses:

```text
FastAPI
LangGraph
Pydantic
httpx
pytest
```

The service is exposed over HTTP.

The .NET control plane communicates with it through:

```text
PythonAgentGateway
```

---

# 17. Phase 9 LangGraph Workflow

The current LangGraph bootstrap workflow is:

```text
START
  ↓
triage
  ↓
knowledge
  ↓
investigation
  ↓
decision
  ├── resolve
  └── escalate
  ↓
END
```

The workflow contains these logical agents/nodes:

```text
TriageAgent
KnowledgeAgent
InvestigationAgent
DecisionAgent
```

---

# 18. Phase 9 TriageAgent

The bootstrap TriageAgent classifies:

```text
Domain
Severity
Confidence
```

The deterministic implementation recognizes patterns such as:

```text
VPN
network
wifi
```

as Network-related.

Other categories include:

```text
Identity
Database
Infrastructure
Unknown
```

Deterministic severity classification uses incident text such as:

```text
down
outage
production
urgent
cannot work
```

This deterministic implementation remains useful for tests.

---

# 19. Phase 9 Knowledge Boundary

The Python service contains:

```text
agent_service/retrieval/service.py
```

At the Phase 9 boundary, Python retrieval is intentionally a no-op/bootstrap boundary.

The existing .NET/PostgreSQL RAG system remains authoritative.

The intended future architecture is:

```text
Python LangGraph
      ↓
RAG retrieval boundary
      ↓
.NET/PostgreSQL knowledge system
      ↓
Knowledge context
      ↓
Reasoning
```

This is intentionally not described as fully integrated yet.

---

# 20. Phase 9 Investigation and Decision

Investigation is currently deterministic/bootstrap logic.

Decision behavior is intentionally simple.

Current bootstrap rule:

```text
confidence < 0.50
    ↓
escalate

confidence >= 0.50
    ↓
resolve
```

This is a foundation for future model-driven reasoning.

It is not intended to be the final autonomous reasoning policy.

---

# 21. Phase 9 Python API

Health:

```http
GET /health
```

Start:

```http
POST /api/v1/agent/runs
```

Resume:

```http
POST /api/v1/agent/runs/resume
```

The resume endpoint accepts control-plane execution/approval information.

Python does not directly execute privileged .NET tools.

---

# 22. Phase 9 .NET ↔ Python Boundary

The .NET gateway sends an agent run request containing:

```text
Correlation ID
Ticket ID
External reference
Title
Description
Reporter
Domain
Severity
Status
Allowed tool manifest
Triage confidence threshold
Maximum attempts
```

The Python service returns:

```text
Outcome
Triage confidence
Domain
Severity
Tool proposal
Escalation summary
Resolution summary
Trace
Error
```

The .NET gateway maps the Python result into the shared .NET contracts.

This keeps the .NET orchestration layer independent of Python implementation details.

---

# 23. Phase 9 Verification

Phase 9 was verified with:

```text
Python tests:
9 passed

.NET regression:
140 passed

Real .NET → Python → LangGraph HTTP smoke test:
passed
```

The HTTP smoke test verified the actual cross-process boundary rather than only mocking the Python service.

Checkpoint:

```text
phase-9-complete
```

---

# 24. Phase 10 — Multi-Provider LLM Integration

Phase 10 changes the AI architecture from a provider-specific model integration to a provider-independent model layer.

The major new file is:

```text
python/agent_service/llm.py
```

The core abstraction includes:

```text
LlmClient
LlmResponse
OpenAICompatibleClient
create_llm_client(...)
parse_json_object(...)
```

Provider-specific clients are built around this abstraction.

---

# 25. Why the Provider Abstraction Was Added

The earlier architecture was effectively:

```text
Agent
  ↓
NVIDIA NIM
```

That created an unnecessary dependency between agent logic and one model provider.

The new architecture is:

```text
Agent
  ↓
LlmClient
  ↓
Provider
```

Provider selection is configuration-driven.

This means the same triage code can execute against:

```text
deterministic
openrouter
nvidia
openai
anthropic
google
azure_openai
```

without rewriting the triage workflow.

---

# 26. Current Provider List

The supported provider identifiers are:

```text
deterministic
nvidia
openrouter
openai
anthropic
google
azure_openai
```

---

# 27. Deterministic Provider

Deterministic mode is not an external provider.

It is the offline execution path.

Use it for:

```text
CI
Unit tests
Local development
Regression testing
No API credentials
```

Default:

```text
AGENT_LLM_PROVIDER=deterministic
```

This mode must remain available.

The project should not make external LLM credentials mandatory for the normal test suite.

---

# 28. NVIDIA NIM Integration

NVIDIA NIM support was implemented using an OpenAI-compatible client.

Default endpoint:

```text
https://integrate.api.nvidia.com/v1
```

Default model configured during development:

```text
moonshotai/kimi-k3
```

Environment variables include:

```text
NVIDIA_NIM_BASE_URL
NVIDIA_NIM_API_KEY
NVIDIA_NIM_MODEL
NVIDIA_NIM_TIMEOUT_SECONDS
NVIDIA_NIM_REASONING_EFFORT
```

The NVIDIA client also supports:

```text
reasoning_effort
```

through the request payload.

---

# 29. NVIDIA Live-Test Result

A real NVIDIA NIM smoke test was attempted.

The request initially timed out.

The timeout was increased and the request was also configured with lower reasoning effort.

The request still timed out.

Therefore:

```text
NVIDIA client implementation:
implemented

NVIDIA live endpoint verification:
not completed successfully
```

This is why the architecture was expanded to support other providers instead of treating NVIDIA as the only available live model backend.

---

# 30. OpenRouter Integration

OpenRouter was selected as the next live-provider target.

OpenRouter uses an OpenAI-compatible API.

Default endpoint:

```text
https://openrouter.ai/api/v1
```

The client supports:

```text
OPENROUTER_API_KEY
OPENROUTER_MODEL
OPENROUTER_TIMEOUT_SECONDS
OPENROUTER_HTTP_REFERER
OPENROUTER_X_TITLE
```

The provider uses the same generic:

```text
OpenAICompatibleClient
```

request/response implementation.

The exact live OpenRouter model should be selected according to the currently available OpenRouter model catalog.

Do not hard-code a model name in this README as a guaranteed current model.

---

# 31. OpenAI Integration

OpenAI support is implemented through the OpenAI-compatible abstraction.

Configuration uses:

```text
OPENAI_API_KEY
OPENAI_MODEL
```

or the common:

```text
AGENT_LLM_MODEL
```

The client follows the same:

```text
system prompt
user prompt
temperature
max tokens
structured response
token usage
latency
```

contract.

---

# 32. Google Integration

Google support is included in the provider factory.

Configuration supports:

```text
GOOGLE_API_KEY
GOOGLE_MODEL
```

The provider-specific implementation is isolated from the agent workflow.

---

# 33. Azure OpenAI Integration

Azure OpenAI support is included in the provider abstraction.

The implementation supports Azure-specific endpoint/deployment configuration while preserving the same agent-level interface.

The agent should not need to know whether a model is hosted through:

```text
OpenAI
Azure OpenAI
```

---

# 34. Anthropic Integration

Anthropic is supported through its native messages API implementation rather than being forced into an incompatible request format.

The provider is still exposed through:

```text
create_llm_client(...)
```

and therefore remains interchangeable at the agent layer.

---

# 35. Common LLM Interface

The provider abstraction returns:

```text
LlmResponse
```

which contains:

```text
content
model
prompt_tokens
completion_tokens
latency_ms
```

This is important because provider-specific responses are normalized before reaching the agent.

The triage agent therefore receives a consistent representation regardless of provider.

---

# 36. LLM Request Flow

The common OpenAI-compatible request path is:

```text
TriageAgent
    ↓
create_llm_client(provider)
    ↓
LlmClient.chat(...)
    ↓
Provider endpoint
    ↓
JSON response
    ↓
LlmResponse
    ↓
parse_json_object(...)
    ↓
TriageResult
    ↓
StepTrace
```

---

# 37. Structured Triage Output

The LLM triage prompt requests structured JSON.

Expected logical structure:

```json
{
  "domain": "Network",
  "severity": "P2",
  "confidence": 0.91
}
```

The exact values are model-generated when a live provider is selected.

The application validates:

```text
domain
severity
confidence
```

before converting the response into the internal model.

---

# 38. Model Output Validation

LLM output is treated as untrusted input.

The application validates:

```text
JSON format
Required fields
Domain values
Severity values
Confidence range
Non-empty model response
```

Malformed output becomes a controlled failure.

The system does not silently convert malformed model output into an operational action.

---

# 39. JSON Parsing

The provider layer contains:

```text
parse_json_object(...)
```

It supports JSON returned directly by the provider and JSON wrapped in common Markdown code fences.

The parser is deliberately kept separate from the provider transport layer.

This allows response parsing to be unit tested without network access.

---

# 40. LLM Environment Configuration

Example environment configuration:

```text
AGENT_LLM_PROVIDER=deterministic
AGENT_LLM_MODEL=

NVIDIA_NIM_BASE_URL=https://integrate.api.nvidia.com/v1
NVIDIA_NIM_MODEL=moonshotai/kimi-k3
NVIDIA_NIM_TIMEOUT_SECONDS=180
NVIDIA_NIM_REASONING_EFFORT=low
NVIDIA_NIM_API_KEY=

OPENROUTER_MODEL=
OPENROUTER_TIMEOUT_SECONDS=60
OPENROUTER_API_KEY=

OPENAI_MODEL=
OPENAI_API_KEY=

ANTHROPIC_MODEL=
ANTHROPIC_API_KEY=

GOOGLE_MODEL=
GOOGLE_API_KEY=

AZURE_OPENAI_MODEL=
AZURE_OPENAI_API_KEY=
```

The actual `.env.example` should contain blank secret values.

Never commit a real API key.

---

# 41. Common Model Selection

The common model variable is:

```text
AGENT_LLM_MODEL
```

If it is set, it can provide the selected model for the chosen provider.

Provider-specific model variables are also supported.

The resolution order is designed so that an explicitly supplied model can override environment defaults.

---

# 42. Python Agent Models

The Python agent model layer contains:

```text
TriageResult
StepTrace
AgentTrace
```

Trace information now supports:

```text
agent
step_name
model
prompt_version
prompt_tokens
completion_tokens
latency_ms
summary
```

Default values are provided for trace fields that are not applicable to deterministic/no-op steps.

---

# 43. Phase 10 Triage Architecture

The triage flow is now:

```text
triage_ticket(...)
       │
       ▼
Read AGENT_LLM_PROVIDER
       │
       ├── deterministic
       │
       └── external provider
               │
               ▼
       create_llm_client(...)
               │
               ▼
          LlmClient.chat(...)
               │
               ▼
         Structured JSON
               │
               ▼
        Validate response
               │
               ▼
          TriageResult
               │
               ▼
           StepTrace
```

This replaces the earlier NIM-specific branching with a provider-neutral implementation.

---

# 44. Phase 10 Testing Strategy

Phase 10 deliberately separates two categories of tests.

## Deterministic tests

These verify exact behavior.

Examples:

```text
Known network incident → Network
Known identity incident → Identity
Unknown incident → Unknown
Deterministic confidence → expected value
Unsupported provider → controlled error
```

## Provider tests

These use mocked HTTP transport.

They verify:

```text
API key validation
Request construction
Response parsing
Token extraction
Latency measurement
Model propagation
Structured JSON handling
Provider-specific configuration
```

This keeps provider tests fast and repeatable.

---

# 45. Live Provider Testing

A live provider smoke test is different from a deterministic unit test.

A live smoke test should verify:

```text
Credentials work
Endpoint is reachable
Model is available
Response is valid
Response can be parsed
Triage result is valid
Trace contains model
Trace contains token counts when available
Trace contains latency
```

It should not require an exact severity or exact confidence because live LLM outputs are not deterministic contracts.

---

# 46. OpenRouter Test Procedure

From:

```text
D:\Project\AIOps.AgentSwarm\python
```

activate the virtual environment:

```powershell
.\.venv\Scripts\Activate.ps1
```

Set the provider:

```powershell
$env:AGENT_LLM_PROVIDER="openrouter"
```

Set the API key without putting it in source control:

```powershell
$env:OPENROUTER_API_KEY="YOUR_REAL_OPENROUTER_KEY"
```

Set a currently available OpenRouter model:

```powershell
$env:AGENT_LLM_MODEL="YOUR_OPENROUTER_MODEL"
```

Then run:

```powershell
python -c "from agent_service.agents.triage import triage_ticket; result, trace = triage_ticket('VPN outage in production', 'Users cannot connect to the corporate VPN and are unable to work.'); print('DOMAIN:', result.domain); print('SEVERITY:', result.severity); print('CONFIDENCE:', result.confidence); print('MODEL:', trace.model); print('PROMPT TOKENS:', trace.prompt_tokens); print('COMPLETION TOKENS:', trace.completion_tokens); print('LATENCY MS:', round(trace.latency_ms, 2)); print('SUMMARY:', trace.summary)"
```

After the live test:

```powershell
$env:AGENT_LLM_PROVIDER="deterministic"
```

Do not paste the API key into chat.

---

# 47. Discover OpenRouter Models

If the selected model is unknown, the OpenRouter catalog can be queried.

Example:

```powershell
python -c "import os,httpx; key=os.getenv('OPENROUTER_API_KEY',''); print('KEY:', 'SET' if key else 'NOT SET'); r=httpx.get('https://openrouter.ai/api/v1/models',headers={'Authorization':f'Bearer {key}'},timeout=30); print('STATUS:',r.status_code); print(r.text[:3000])"
```

Use the returned model identifier as:

```text
AGENT_LLM_MODEL
```

This keeps model selection separate from application code.

---

# 48. Python Virtual Environment

From:

```text
D:\Project\AIOps.AgentSwarm\python
```

create the environment if necessary:

```powershell
python -m venv .venv
```

Activate:

```powershell
.\.venv\Scripts\Activate.ps1
```

Install dependencies:

```powershell
pip install -r requirements.txt
```

Expected development Python version:

```text
Python 3.12.10
```

---

# 49. Run Python Service

From:

```text
D:\Project\AIOps.AgentSwarm\python
```

run:

```powershell
uvicorn agent_service.main:app --host 127.0.0.1 --port 8000
```

Health check:

```powershell
python -c "import httpx; r=httpx.get('http://127.0.0.1:8000/health',timeout=10); print(r.status_code); print(r.text)"
```

Expected health status:

```text
200
```

---

# 50. Run Python Tests

From:

```text
D:\Project\AIOps.AgentSwarm\python
```

run:

```powershell
pytest -q
```

The known Phase 9 baseline was:

```text
9 passed
```

During Phase 10, the Python suite expanded substantially with provider abstraction and LLM tests.

The exact current live count should be taken from the latest local `pytest -q` result rather than hard-coded into this README until the active Phase 10 batch is finalized.

---

# 51. .NET Configuration

The .NET AI configuration contains:

```text
AI:AgentGatewayMode
AI:AgentService:BaseUrl
AI:AgentService:TimeoutSeconds
AI:NvidiaNim:BaseUrl
AI:NvidiaNim:Model
AI:NvidiaNim:ApiKey
```

Default application configuration remains:

```json
{
  "AI": {
    "AgentGatewayMode": "Fake",
    "AgentService": {
      "BaseUrl": "http://127.0.0.1:8000/",
      "TimeoutSeconds": 60
    },
    "NvidiaNim": {
      "BaseUrl": "https://integrate.api.nvidia.com/v1",
      "Model": "moonshotai/kimi-k3",
      "ApiKey": ""
    }
  }
}
```

The NVIDIA configuration exists on the .NET side for the broader AI configuration model, while the actual Phase 10 provider execution currently happens in Python.

---

# 52. .NET Agent Gateway Selection

The infrastructure registration supports:

```text
AI:AgentGatewayMode=Fake
```

or:

```text
AI:AgentGatewayMode=Http
```

Fake mode:

```text
InProcessFakeAgentGateway
```

HTTP mode:

```text
PythonAgentGateway
```

The default remains:

```text
Fake
```

This is intentional.

It prevents the normal .NET test suite from requiring the Python service to be running.

---

# 53. PythonAgentGateway

The .NET HTTP gateway:

```text
src/AIOps.Infrastructure/Agents/PythonAgentGateway.cs
```

implements:

```text
IAgentGateway
```

It performs:

```text
POST /api/v1/agent/runs
POST /api/v1/agent/runs/resume
```

The gateway maps:

```text
Python outcome
→
AgentRunOutcome
```

and maps:

```text
Python trace
→
.NET StepTrace
```

The gateway also handles HTTP and transport failures as controlled agent failures.

---

# 54. Orchestration

The orchestration layer contains:

```text
Orchestrator
```

It is responsible for control-plane delegation.

Current agent operations include:

```text
StartAgentRunAsync(...)
ResumeAgentRunAsync(...)
```

The orchestrator:

```text
validates ticket existence
delegates to IAgentGateway
records audit events
```

The orchestrator does not directly depend on:

```text
LangGraph
OpenRouter
NVIDIA NIM
OpenAI
Anthropic
```

This is deliberate architectural separation.

---

# 55. Agent Run Contracts

The shared .NET contracts include:

```text
ToolManifestEntry
AgentTicketContext
AgentRunRequest
ToolProposal
StepTrace
AgentRunOutcome
AgentRunResult
ResumeAgentRunRequest
```

Possible outcomes:

```text
Resolved
AwaitingApproval
Escalated
Failed
```

The contracts form the stable boundary between:

```text
.NET control plane
```

and:

```text
Python agent implementation
```

---

# 56. Tool Proposal Contract

A tool proposal contains:

```text
ToolName
ArgumentsJson
Confidence
Justification
```

The proposal is not an execution record.

It is an instruction candidate for the control plane.

The control plane must still apply:

```text
Risk policy
Approval policy
Schema validation
Execution policy
Audit
```

---

# 57. Resume Flow

The agent can be resumed after an approval/execution decision.

The resume contract includes:

```text
CorrelationId
TicketId
ApprovalGranted
ApprovalDecidedBy
ToolResultJson
ToolExecutionSucceeded
```

The Python service uses this information to produce the next agent outcome.

The execution itself remains outside Python.

---

# 58. PostgreSQL and pgvector

PostgreSQL is used for:

```text
Audit
Evaluation
Knowledge
Action execution
Approval state
```

pgvector is used for knowledge vectors.

Knowledge search uses:

```text
vector similarity
cosine distance
HNSW indexing
```

The current embedding generator is deterministic.

---

# 59. RAG Architecture

Current authoritative RAG architecture:

```text
Knowledge Documents
        ↓
Knowledge Chunks
        ↓
Deterministic Embeddings
        ↓
PostgreSQL + pgvector
        ↓
Cosine Similarity Search
        ↓
Knowledge Search Results
```

Current Python architecture:

```text
LangGraph
   ↓
Knowledge boundary
   ↓
Future integration point
```

The next major step is to connect these two pieces.

---

# 60. Evaluation Architecture

Evaluation remains independent of the model provider.

Conceptually:

```text
Evaluation Dataset
       ↓
Agent Run
       ↓
Trace
       ↓
Metrics
       ↓
Evaluation Record
       ↓
PostgreSQL
```

Metrics can include:

```text
Resolution
Escalation
Approval behavior
Tool behavior
Latency
Token usage
Trace completeness
Repeatability
```

This becomes increasingly important as the number of LLM providers grows.

---

# 61. Audit Architecture

Audit is controlled by the .NET side.

Important events include:

```text
Ticket creation
Agent run start
Agent run resume
Approval lifecycle
Action execution
Tool result
Other important operational events
```

Each event is associated with a correlation context.

The purpose is to distinguish:

```text
What the model proposed
```

from:

```text
What the system actually executed
```

---

# 62. Safety Architecture

The safety model is:

```text
Model
 ↓
Proposal
 ↓
Policy
 ↓
Approval
 ↓
Execution
```

Not:

```text
Model
 ↓
Direct privileged API
```

This remains a fundamental design requirement.

---

# 63. Current Limitations

The following are intentionally incomplete:

```text
Python knowledge retrieval is not yet fully connected to .NET/PostgreSQL RAG.
Investigation is bootstrap/deterministic logic.
Decision logic is bootstrap/deterministic logic.
Python does not directly execute privileged tools.
Full model-driven tool selection is not yet complete.
Agent re-evaluation after real tool execution is not yet complete.
Production cloud/ITSM/directory integrations are not yet complete.
OpenRouter live verification is the current provider-testing task.
NVIDIA NIM live verification timed out.
```

These should not be hidden from future contributors.

---

# 64. Security and Secrets

Never commit:

```text
OPENROUTER_API_KEY
NVIDIA_NIM_API_KEY
OPENAI_API_KEY
ANTHROPIC_API_KEY
GOOGLE_API_KEY
Azure OpenAI credentials
```

Use:

```text
Environment variables
.NET user secrets
Secret manager
Deployment secret store
```

Never put a real secret in:

```text
README.md
appsettings.json
Python source
tests
Git
```

The `.env.example` file contains blank secret values only.

---

# 65. Development Principles

## Stable abstractions

Cross-layer communication should use stable interfaces and contracts.

## Deterministic local development

External AI services must not be required for normal tests.

## Safety before autonomy

Consequential AI actions must pass server-side policy and approval controls.

## Audit important operations

Important actions must produce persistent audit records.

## Evaluation before model expansion

New models/providers/prompts should be evaluated against repeatable cases.

## Provider independence

Agent logic should not depend on a single model vendor.

## No hidden execution

An agent result must not imply that a real operational action occurred unless the control plane recorded execution.

## Model output is untrusted input

All structured model output must be validated before entering operational logic.

---

# 66. Development Commands

## Build

```powershell
dotnet build AIOps.slnx
```

## Run all .NET tests

```powershell
dotnet test AIOps.slnx
```

## Run .NET tests without rebuilding

```powershell
dotnet test AIOps.slnx --no-build
```

## Run orchestration tests

```powershell
dotnet test .\tests\AIOps.Orchestration.Tests\AIOps.Orchestration.Tests.csproj
```

## Python tests

```powershell
cd D:\Project\AIOps.AgentSwarm\python
.\.venv\Scripts\Activate.ps1
pytest -q
```

## Git status

```powershell
git status
```

## Git history

```powershell
git log --oneline --decorate -10
```

## Tags

```powershell
git tag --list
```

---

# 67. Running the Complete Local Stack

## Step 1 — PostgreSQL

Start PostgreSQL.

Verify:

```powershell
Test-NetConnection 127.0.0.1 -Port 5432
```

## Step 2 — Python service

```powershell
cd D:\Project\AIOps.AgentSwarm\python
.\.venv\Scripts\Activate.ps1
$env:AGENT_LLM_PROVIDER="deterministic"
uvicorn agent_service.main:app --host 127.0.0.1 --port 8000
```

## Step 3 — .NET configuration

For real Python integration:

```text
AI:AgentGatewayMode=Http
```

For normal deterministic .NET tests:

```text
AI:AgentGatewayMode=Fake
```

## Step 4 — Start .NET host

From the repository root:

```powershell
cd D:\Project\AIOps.AgentSwarm
dotnet run --project .\src\AIOps.Host\AIOps.Host.csproj
```

---

# 68. Safe Default Development Mode

The preferred normal development configuration is:

```text
.NET:
AI:AgentGatewayMode=Fake

Python:
AGENT_LLM_PROVIDER=deterministic
```

This gives:

```text
No external LLM dependency
No API key requirement
Deterministic behavior
Repeatable tests
Fast local iteration
```

Real providers are enabled only for explicit integration testing.

---

# 69. Real Provider Development Mode

For live model testing:

```text
.NET
  ↓
PythonAgentGateway
  ↓
Python FastAPI
  ↓
LangGraph
  ↓
Selected LLM provider
```

Example provider selection:

```powershell
$env:AGENT_LLM_PROVIDER="openrouter"
```

The Python service can then use:

```text
AGENT_LLM_MODEL
OPENROUTER_API_KEY
```

without changing the LangGraph workflow.

---

# 70. Git Checkpoints

Completed checkpoints:

```text
phase-5-complete
phase-6-complete
phase-7-complete
phase-8-complete
phase-9-complete
```

The latest completed checkpoint is:

```text
phase-9-complete
```

Phase 10 should receive its own checkpoint only after the current Phase 10 work is fully verified and committed.

Do not claim:

```text
phase-10-complete
```

until that checkpoint actually exists in Git.

---

# 71. Phase History

```text
Phase 1
Foundation
        ↓
Phase 2
Ticket Lifecycle
        ↓
Phase 3
Persistence & Audit
        ↓
Phase 4
Tool Execution
        ↓
Phase 5
Agent Gateway
        ↓
Phase 6
Human Approval & Safety
        ↓
Phase 7
RAG / Knowledge Retrieval
        ↓
Phase 8
Evaluation & Observability
        ↓
Phase 9
Python / LangGraph
        ↓
Phase 10
Multi-Provider LLM Integration
        ↓
Phase 11
RAG Context Injection / Reasoning
```

---

# 72. Next Phase — Phase 11

The next major architectural step is to connect the authoritative .NET/PostgreSQL RAG system to the Python LangGraph reasoning flow.

Target:

```text
Ticket
   ↓
LLM Triage
   ↓
Knowledge Retrieval
   ↓
Relevant RAG Context
   ↓
Investigation / Reasoning
   ↓
Tool Proposal
   ↓
Risk Classification
   ↓
Approval
   ↓
Execution
```

The important change will be:

```text
KnowledgeAgent
```

becoming a real retrieval participant rather than a no-op boundary.

---

# 73. Phase 11 Goals

Planned work:

```text
Expose retrieval through a stable boundary
Retrieve relevant knowledge for an incident
Pass knowledge context into reasoning
Track retrieval results in trace
Track retrieval latency
Track retrieval relevance
Evaluate retrieval behavior
Preserve PostgreSQL/pgvector authority
```

---

# 74. Phase 12 — Tool Proposal Agent

After knowledge context is integrated:

```text
Reasoning
   ↓
Tool Selection
   ↓
Structured Tool Proposal
   ↓
Schema Validation
   ↓
Risk Classification
```

The model should be able to recommend an available tool based on:

```text
Ticket
Triage
Knowledge
Investigation
Tool manifest
Policy context
```

The model still should not execute the tool.

---

# 75. Phase 13 — Approval-Aware Agent Loop

The target loop becomes:

```text
Proposal
   ↓
Risk Classification
   ↓
Approval Request
   ↓
Human Decision
   ↓
Execution
   ↓
Execution Result
   ↓
Agent Re-evaluation
   ↓
Resolve / Escalate
```

This will turn the current start/resume architecture into a more complete agent execution loop.

---

# 76. Phase 14 — Production Integrations

Planned production integrations include:

```text
Real cloud APIs
Real directory services
Real ITSM systems
Real network diagnostics
Credential isolation
Operational telemetry
```

All integrations should remain behind stable abstractions.

---

# 77. Phase 15 — Advanced Evaluation

Planned evaluation expansion:

```text
Larger incident datasets
Model comparison
Provider comparison
Prompt comparison
Retrieval evaluation
Tool-selection evaluation
Approval-policy evaluation
Latency comparison
Token/cost measurement
Regression dashboards
```

The Phase 8 evaluation foundation should remain the baseline.

---

# 78. Long-Term Autonomous Architecture

The intended production flow is:

```text
                     ┌────────────────────┐
                     │     Operator       │
                     │    UI / API        │
                     └─────────┬──────────┘
                               │
                               ▼
                     ┌────────────────────┐
                     │ .NET Control Plane │
                     │                    │
                     │ Orleans            │
                     │ Ticket lifecycle   │
                     │ Agent orchestration│
                     │ Safety             │
                     │ Approval           │
                     │ Tools              │
                     │ Audit              │
                     └──────┬───────┬─────┘
                            │       │
                     HTTP   │       │ PostgreSQL
                            │       │
                            ▼       ▼
                  ┌──────────────┐ ┌─────────────────┐
                  │ Python       │ │ PostgreSQL      │
                  │ Agent        │ │ + pgvector      │
                  │ Service      │ │                 │
                  │              │ │ RAG             │
                  │ FastAPI      │ │ Audit           │
                  │ LangGraph    │ │ Evaluation      │
                  │ Triage       │ │ Approvals       │
                  │ Retrieval    │ │ Actions         │
                  │ Reasoning    │ └─────────────────┘
                  └──────┬───────┘
                         │
                         ▼
                  ┌────────────────────┐
                  │ Provider Layer     │
                  │                    │
                  │ OpenRouter         │
                  │ NVIDIA NIM         │
                  │ OpenAI             │
                  │ Anthropic          │
                  │ Google             │
                  │ Azure OpenAI       │
                  │ Deterministic      │
                  └────────────────────┘
```

---

# 79. Final Architecture Rules

## Rule 1 — .NET owns operational authority

The control plane owns:

```text
Tickets
Approvals
Policy
Tool execution
Audit
```

## Rule 2 — Python owns agent workflow logic

Python owns:

```text
LangGraph workflow
Agent nodes
Provider selection
LLM interaction
Reasoning state
```

but does not become the privileged execution authority.

## Rule 3 — PostgreSQL owns persistent operational data

PostgreSQL stores:

```text
Audit
Evaluation
Knowledge
Action state
Approval state
```

## Rule 4 — RAG is authoritative through the knowledge subsystem

The Python agent should consume knowledge through a stable boundary rather than duplicating the RAG implementation.

## Rule 5 — Providers are replaceable

Agent logic must not be rewritten when changing:

```text
OpenRouter
NVIDIA
OpenAI
Anthropic
Google
Azure
```

## Rule 6 — Deterministic mode remains available

External model availability must not block development or CI.

## Rule 7 — Model output is never trusted blindly

Every structured response must be validated.

## Rule 8 — No hidden tool execution

A proposal is not an execution.

## Rule 9 — Every consequential action is auditable

The system should be able to answer:

```text
What was proposed?
Which agent produced it?
Which model produced it?
What confidence was returned?
What knowledge was used?
Was approval required?
Who approved it?
What actually executed?
What was the result?
What happened afterward?
```

## Rule 10 — Completed phases remain regression baselines

Future phases should extend the completed architecture rather than reimplement completed work.

---

# 80. Current Development Checkpoint

```text
=============================================
             AIOps Agent Swarm
=============================================

Phase 1   COMPLETE
Phase 2   COMPLETE
Phase 3   COMPLETE
Phase 4   COMPLETE
Phase 5   COMPLETE
Phase 6   COMPLETE
Phase 7   COMPLETE
Phase 8   COMPLETE
Phase 9   COMPLETE

Phase 10  IN PROGRESS

Current completed checkpoint:
phase-9-complete

Latest completed Phase 9 commit:
6a15a32

Current Phase 10 architecture:
Provider-independent LLM layer

Providers implemented:
- deterministic
- NVIDIA NIM
- OpenRouter
- OpenAI
- Anthropic
- Google
- Azure OpenAI

NVIDIA live test:
Timed out

OpenRouter:
Selected for live provider testing

Next architectural phase:
Phase 11 — RAG Context Injection / Reasoning
=============================================
```

---

# 81. Important Status Accuracy Rule

This README intentionally distinguishes between:

```text
Implemented
```

and:

```text
Live verified
```

For example:

```text
NVIDIA client:
Implemented

NVIDIA live endpoint:
Timed out during testing
```

Similarly:

```text
OpenRouter client:
Implemented

OpenRouter live verification:
Pending until the smoke test succeeds
```

This distinction should be maintained for all future providers and integrations.

---

# 82. Baseline to Preserve

Before moving into the next implementation batch, preserve:

```text
.NET control-plane architecture
Orleans orchestration
IAgentGateway
Fake gateway
PythonAgentGateway
PostgreSQL persistence
pgvector RAG
Approval controls
Tool registry
Audit
Evaluation framework
FastAPI service
LangGraph workflow
Deterministic triage
Provider-independent LLM layer
```

The next changes should build on these components.

---

# 83. Final Summary

AIOps Agent Swarm has evolved from a .NET ticket-management prototype into a multi-layer AI operations platform.

The architecture now separates:

```text
Control plane
Agent workflow
Knowledge
LLM inference
Tools
Approval
Audit
Evaluation
```

The most important architectural evolution during the latest implementation work was the move from a single NVIDIA-oriented model integration to a provider-independent LLM layer.

Current direction:

```text
.NET Control Plane
        ↓
Python LangGraph
        ↓
Provider-independent LLM client
        ↓
OpenRouter / NVIDIA / OpenAI / Anthropic / Google / Azure
```

while maintaining:

```text
PostgreSQL + pgvector
Human approval
Server-side safety
Persistent audit
Deterministic testing
Evaluation
```

The immediate next step is to complete Phase 10 live-provider verification and then proceed to:

```text
Phase 11 — RAG Context Injection / Reasoning
```
