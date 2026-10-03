# AidenOps --- AIOps Agent Swarm

AidenOps is an enterprise-oriented **AI Operations (AIOps) agent
platform** for incident triage, knowledge retrieval, investigation,
controlled tool execution, human approval, enterprise integrations,
auditability, credential isolation, operational telemetry, and
evaluation.

The project deliberately separates **AI reasoning** from **operational
authority**.

The current architecture is:

``` text
User / Operator / Ticket Source
              │
              ▼
      ASP.NET Core / UI
              │
              ▼
      .NET CONTROL PLANE
      ├── Orleans orchestration
      ├── Ticket lifecycle
      ├── Safety / policy
      ├── Approval
      ├── Tool registry / execution
      ├── Audit
      └── Operational telemetry
              │
       ┌──────┴───────────────┐
       │                      │
       ▼                      ▼
Python Agent Service      PostgreSQL + pgvector
FastAPI + LangGraph       Knowledge / Audit / Evaluation
       │                  Actions / Approvals
       ▼
Provider-independent LLM layer
       ├── deterministic
       ├── OpenRouter
       ├── NVIDIA NIM
       ├── OpenAI
       ├── Anthropic
       ├── Google
       └── Azure OpenAI
       │
       ▼
Production Integration Layer
       ├── AWS EC2
       ├── Microsoft Graph / Entra ID
       ├── ServiceNow
       ├── Jira Service Management
       ├── Zendesk
       ├── Cisco Secure Access
       ├── Palo Alto GlobalProtect
       ├── Fortinet FortiGate
       └── Cisco ThousandEyes
```

**Core rule:** the model can recommend an action, but it is never the
authority that executes a consequential operational action. The .NET
control plane validates policy, approval, execution, audit, and
integration boundaries.

------------------------------------------------------------------------

# 1. Current Project Status

## Completed phases

``` text
Phase 1  — Foundation                                  COMPLETE
Phase 2  — Ticket Lifecycle                            COMPLETE
Phase 3  — Persistence & Audit                         COMPLETE
Phase 4  — Tool Execution                              COMPLETE
Phase 5  — Agent Gateway                               COMPLETE
Phase 6  — Human Approval & Safety Controls            COMPLETE
Phase 7  — RAG / Knowledge Retrieval                   COMPLETE
Phase 8  — Agent Evaluation & Observability Foundation COMPLETE
Phase 9  — Python / LangGraph Agent Swarm              COMPLETE
Phase 10 — Multi-Provider LLM Integration              COMPLETE
Phase 11 — RAG Context Injection / Reasoning            COMPLETE
Phase 12 — Tool Proposal Agent                           COMPLETE
Phase 13 — Approval-Aware Execution Resume               COMPLETE
Phase 14 — Production Integrations + Security + Telemetry COMPLETE
```

## Current checkpoint

``` text
CURRENT COMPLETED PHASE: Phase 14
STATUS: COMPLETE
NEXT PHASE: Phase 15 — Advanced Evaluation / ML + LLM Evaluation
```

## Phase 14 verification

``` text
Full test suite:        444 passed
Failed tests:           0
Skipped tests:          0
Build:                  SUCCESS
Working tree:           CLEAN
Git master:             UP TO DATE WITH origin/master
Latest commit:          1b9e750
Commit message:         Complete Phase 14 credential isolation and telemetry
```

Phase 14 completed production integration boundaries for cloud,
identity, ITSM, VPN/network diagnostics, plus credential isolation and
safe operational telemetry.

## What AidenOps can do now

``` text
Incident / Request
       ↓
Ticket / Operational Context
       ↓
.NET Control Plane
       ↓
Python Agent Gateway
       ↓
LangGraph Agent Workflow
       ↓
Triage
       ↓
Knowledge Retrieval
       ↓
Investigation / Decision
       ↓
Tool Proposal
       ↓
.NET Policy / Safety Validation
       ↓
Approval when required
       ↓
.NET Tool Execution
       ↓
Enterprise Integration
       ↓
Execution Result
       ↓
Audit + Telemetry
       ↓
Resolve / Escalate / Resume
```

Current capabilities include:

-   Incident/ticket context processing and triage.
-   PostgreSQL + pgvector knowledge retrieval.
-   LangGraph agent workflows.
-   Provider-independent LLM interaction.
-   Structured tool proposals.
-   Server-side validation and risk/policy controls.
-   Human approval for approval-required actions.
-   Controlled execution through the .NET ToolExecutor.
-   Resume handling after approval/execution decisions.
-   Persistent audit records.
-   Production integration boundaries for AWS, identity, ITSM, VPN, and
    network diagnostics.
-   Credential isolation and safe telemetry.
-   Deterministic execution for development and CI.

## What Phase 15 will add

Phase 15 will extend the existing evaluation foundation with:

``` text
Versioned golden datasets
        ↓
Experiment tracking
        ↓
Classical ML baseline
        ↓
LLM / agent evaluation
        ↓
Retrieval evaluation
        ↓
Tool-selection evaluation
        ↓
Provider / model comparison
        ↓
Regression analysis and reporting
```

Planned areas include TF-IDF + Logistic Regression baselines,
dataset/version tracking, model/provider comparison, retrieval and
tool-selection metrics, latency/token/cost measurement where available,
and reproducible evaluation runs.

------------------------------------------------------------------------

# 2. Project Vision\*\*

The long-term goal is a controlled autonomous AIOps workflow:

``` text

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

``` text

Safe autonomy

Provider independence

Deterministic testing

Persistent audit

Repeatable evaluation

Human approval for consequential actions

Separation of inference and execution
```

------------------------------------------------------------------------

# 3. Most Important Architectural Rule\*\*

The system follows:

``` text

AI decides what it recommends.

Control plane decides what is allowed.

Tools execute only through the control plane.

Audit records what actually happened.
```

A model output such as:

``` text

Restart EC2 instance i-123456
```

is only a proposal.

It is not evidence that the instance was restarted.

The actual execution path is:

``` text

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

------------------------------------------------------------------------

# 4. Architecture\*\*

## 4.1 Current logical architecture\*\*

``` text

┌──────────────────────────────────────────────────────────────┐

│                         User / Operator                      │

│                    UI / API / Ticket Source                  │

└──────────────────────────────┬───────────────────────────────┘

                               │

                               ▼

┌──────────────────────────────────────────────────────────────┐

│                       .NET Control Plane                      │

│                                                              │

│ ASP.NET Core                                                │

│ Razor Components                                             │

│ SignalR                                                      │

│ Orleans                                                      │

│ Ticket lifecycle                                             │

│ Agent orchestration                                          │

│ Approval / safety policy                                     │

│ Tool registry                                                │

│ Tool execution                                               │

│ Audit                                                        │

│ Evaluation                                                   │

└───────────────┬───────────────────────────┬──────────────────┘

                │                           │

                │ HTTP                      │ PostgreSQL

                ▼                           ▼

┌──────────────────────────────┐   ┌────────────────────────────┐

│      Python Agent Service    │   │         PostgreSQL          │

│                              │   │                            │

│ FastAPI                      │   │ Audit                       │

│ LangGraph                   │   │ Evaluation                  │

│ TriageAgent                 │   │ Knowledge documents         │

│ Knowledge boundary          │   │ Knowledge chunks            │

│ InvestigationAgent          │   │ pgvector                    │

│ DecisionAgent               │   │ Action executions            │

│ LLM provider abstraction    │   │ Approval requests            │

└──────────────┬───────────────┘   └────────────────────────────┘

               │

               ▼

┌──────────────────────────────────────────────────────────────┐

│                    LLM Provider Layer                        │

│                                                              │

│ deterministic                                                │

│ OpenRouter                                                   │

│ NVIDIA NIM                                                   │

│ OpenAI                                                       │

│ Anthropic                                                    │

│ Google                                                       │

│ Azure OpenAI                                                 │

└──────────────────────────────────────────────────────────────┘
```

------------------------------------------------------------------------

# 5. Architectural Evolution\*\*

The architecture changed significantly during implementation.

## Initial AI direction\*\*

The original direction was centered around:

``` text

.NET

   ↓

Python / LangGraph

   ↓

NVIDIA NIM / Nemotron
```

Phase 10 initially implemented NVIDIA NIM support.

During live testing, the NVIDIA endpoint did not complete within the
configured timeout.

The architecture was therefore expanded instead of making the system
dependent on one provider.

## Current direction\*\*

The Python service now uses:

``` text

Agent logic

    ↓

Provider-independent LLM client

    ↓

Selected provider
```

The provider is selected using environment configuration:

``` text

AGENT_LLM_PROVIDER
```

The model is selected using:

``` text

AGENT_LLM_MODEL
```

Provider-specific model variables remain supported.

This means the agent logic does not need to know whether the model came
from:

``` text

OpenRouter

NVIDIA NIM

OpenAI

Anthropic

Google

Azure OpenAI
```

This is a major architectural improvement because provider availability,
latency, cost, model capability, and operational reliability can change
without rewriting the agent workflow.

------------------------------------------------------------------------

# 6. Repository Structure\*\*

``` text

AIOps.AgentSwarm/

│

├── src/

│   ├── AIOps.Abstractions/

│   │   ├── Agents/

│   │   ├── Audit/

│   │   ├── Configuration/

│   │   ├── Evaluation/

│   │   ├── Integrations/

│   │   ├── Knowledge/

│   │   ├── Persistence/

│   │   └── Time/

│   │

│   ├── AIOps.Contracts/

│   │   ├── AgentGateway/

│   │   └── Api/

│   │

│   ├── AIOps.Domain/

│   │

│   ├── AIOps.Grains/

│   │

│   ├── AIOps.Host/

│   │

│   ├── AIOps.Infrastructure/

│   │   ├── Agents/

│   │   ├── Audit/

│   │   ├── EfCore/

│   │   ├── Evaluation/

│   │   ├── Integrations/

│   │   ├── Knowledge/

│   │   ├── Persistence/

│   │   └── Time/

│   │

│   └── AIOps.Orchestration/

│

├── tests/

│   ├── AIOps.Abstractions.Tests/

│   ├── AIOps.Api.Tests/

│   ├── AIOps.Domain.Tests/

│   ├── AIOps.Grains.Tests/

│   ├── AIOps.Infrastructure.Tests/

│   ├── AIOps.Orchestration.Tests/

│   └── AIOps.Tools.Tests/

│

├── python/

│   ├── requirements.txt

│   ├── .env.example

│   └── agent_service/

│       ├── \_\_init\_\_.py

│       ├── main.py

│       ├── graph.py

│       ├── llm.py

│       ├── agents/

│       │   ├── \_\_init\_\_.py

│       │   └── triage.py

│       ├── models/

│       │   ├── \_\_init\_\_.py

│       │   └── agent.py

│       ├── retrieval/

│       │   ├── \_\_init\_\_.py

│       │   └── service.py

│       ├── tools/

│       │   └── \_\_init\_\_.py

│       └── tests/

│           ├── \_\_init\_\_.py

│           ├── test_health.py

│           ├── test_triage.py

│           ├── test_graph.py

│           └── test_llm.py

│

└── README.md
```

------------------------------------------------------------------------

# 7. Technology Stack\*\*

## .NET\*\*

``` text

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

## Python\*\*

``` text

Python 3.12

FastAPI

Uvicorn

LangGraph

Pydantic

httpx

pytest
```

## Database\*\*

``` text

PostgreSQL

pgvector
```

## AI\*\*

``` text

Deterministic provider

OpenRouter

NVIDIA NIM

OpenAI

Anthropic

Google

Azure OpenAI
```

------------------------------------------------------------------------

# 8. Phase 1 --- Foundation\*\*

Phase 1 established the initial solution structure.

The project was separated into:

``` text

Domain

Contracts

Abstractions

Infrastructure

Orchestration

Host

Tests
```

Initial operational functionality included:

``` text

ASP.NET Core host

Health endpoint

Application shell

Initial project boundaries

Initial test structure
```

Checkpoint:

``` text

Phase 1 complete
```

------------------------------------------------------------------------

# 9. Phase 2 --- Ticket Lifecycle\*\*

Phase 2 introduced the operational ticket lifecycle.

The system models ticket state and transitions through the
control-plane/application layer.

The lifecycle supports states such as:

``` text

New

Triaging

WaitingForApproval

Executing

Resolved

Escalated
```

Ticket processing is kept separate from AI provider implementation.

Checkpoint:

``` text

Phase 2 complete
```

------------------------------------------------------------------------

# 10. Phase 3 --- Persistence & Audit\*\*

Phase 3 introduced persistent operational state.

PostgreSQL became the persistence foundation.

The system introduced:

``` text

Entity Framework Core

Npgsql

PostgreSQL

Audit records

Correlation IDs

Persistent evaluation records
```

Audit records provide a durable record of important operations.

Important audit information includes:

``` text

correlation_id

entity_id

entity_type

event_type

occurred_at

payload

sequence
```

The audit layer is important because an AI explanation alone cannot
establish what actually happened.

Checkpoint:

``` text

Phase 3 complete
```

------------------------------------------------------------------------

# 11. Phase 4 --- Tool Execution\*\*

Phase 4 introduced operational tools.

Tools are registered through a tool registry.

Current tool examples include:

``` text

GetInstanceStatus

RestartInstance

ResetPassword

GrantGroupAccess

UpdateTicket

RunVpnDiagnostics
```

Tool metadata includes:

``` text

Name

Description

Risk

RequiresApproval

Input schema
```

The AI layer does not receive unrestricted direct execution authority.

Checkpoint:

``` text

Phase 4 complete
```

------------------------------------------------------------------------

# 12. Phase 5 --- Agent Gateway\*\*

Phase 5 established the stable AI gateway contract.

Core abstraction:

``` text

IAgentGateway
```

Operations:

``` text

StartRunAsync(...)

ResumeRunAsync(...)
```

Shared contracts include:

``` text

AgentRunRequest

AgentTicketContext

ToolManifestEntry

ToolProposal

StepTrace

AgentRunResult

ResumeAgentRunRequest

AgentRunOutcome
```

The gateway allows the control plane to communicate with different agent
implementations.

Two implementations exist:

``` text

InProcessFakeAgentGateway

PythonAgentGateway
```

The fake gateway is retained for deterministic local development and CI.

Checkpoint:

``` text

phase-5-complete
```

------------------------------------------------------------------------

# 13. Phase 6 --- Human Approval & Safety Controls\*\*

Phase 6 added the safety boundary.

The system introduced:

``` text

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

``` text

LLM proposal != execution
```

A consequential proposal can become:

``` text

AwaitingApproval
```

A human decision is then recorded by the control plane.

Possible approval states include:

``` text

Approved

Rejected

Expired
```

The Python agent is not allowed to approve its own privileged action.

Checkpoint:

``` text

phase-6-complete
```

------------------------------------------------------------------------

# 14. Phase 7 --- RAG / Knowledge Retrieval\*\*

Phase 7 introduced PostgreSQL-backed knowledge retrieval.

The RAG subsystem includes:

``` text

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

``` text

IEmbeddingGenerator

IKnowledgeStore

KnowledgeDocument

KnowledgeChunk

KnowledgeSearchResult
```

The initial embedding implementation is deterministic.

This allows reproducible tests without requiring an external embedding
provider.

The PostgreSQL vector index uses:

``` text

HNSW

vector_cosine_ops
```

The .NET/PostgreSQL RAG implementation remains the authoritative RAG
system.

Checkpoint:

``` text

phase-7-complete
```

------------------------------------------------------------------------

# 15. Phase 8 --- Agent Evaluation & Observability\*\*

Phase 8 introduced repeatable evaluation.

The evaluation framework includes:

``` text

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

A new provider should be evaluated against repeatable cases rather than
judged only from a single live request.

Checkpoint:

``` text

phase-8-complete
```

------------------------------------------------------------------------

# 16. Phase 9 --- Python / LangGraph Agent Swarm\*\*

Phase 9 introduced the real Python agent service.

The Python service uses:

``` text

FastAPI

LangGraph

Pydantic

httpx

pytest
```

The service is exposed over HTTP.

The .NET control plane communicates with it through:

``` text

PythonAgentGateway
```

------------------------------------------------------------------------

# 17. Phase 9 LangGraph Workflow\*\*

The current LangGraph bootstrap workflow is:

``` text

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

``` text

TriageAgent

KnowledgeAgent

InvestigationAgent

DecisionAgent
```

------------------------------------------------------------------------

# 18. Phase 9 TriageAgent\*\*

The bootstrap TriageAgent classifies:

``` text

Domain

Severity

Confidence
```

The deterministic implementation recognizes patterns such as:

``` text

VPN

network

wifi
```

as Network-related.

Other categories include:

``` text

Identity

Database

Infrastructure

Unknown
```

Deterministic severity classification uses incident text such as:

``` text

down

outage

production

urgent

cannot work
```

This deterministic implementation remains useful for tests.

------------------------------------------------------------------------

# 19. Phase 9 Knowledge Boundary\*\*

The Python service contains:

``` text

agent_service/retrieval/service.py
```

Phase 9 established the retrieval boundary. Phase 11 subsequently
connected that boundary to the authoritative .NET/PostgreSQL RAG system,
so knowledge retrieval is now an active participant in the agent
reasoning flow.

The intended future architecture is:

``` text

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

------------------------------------------------------------------------

# 20. Phase 9 Investigation and Decision\*\*

Investigation is currently deterministic/bootstrap logic.

Decision behavior is intentionally simple.

Current bootstrap rule:

``` text

confidence < 0.50

    ↓

escalate

confidence >= 0.50

    ↓

resolve
```

This is a foundation for future model-driven reasoning.

It is not intended to be the final autonomous reasoning policy.

------------------------------------------------------------------------

# 21. Phase 9 Python API\*\*

Health:

``` http

GET /health
```

Start:

``` http

POST /api/v1/agent/runs
```

Resume:

``` http

POST /api/v1/agent/runs/resume
```

The resume endpoint accepts control-plane execution/approval
information.

Python does not directly execute privileged .NET tools.

------------------------------------------------------------------------

# 22. Phase 9 .NET ↔ Python Boundary\*\*

The .NET gateway sends an agent run request containing:

``` text

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

``` text

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

This keeps the .NET orchestration layer independent of Python
implementation details.

------------------------------------------------------------------------

# 23. Phase 9 Verification\*\*

Phase 9 was verified with:

``` text

Python tests:

9 passed

.NET regression:

140 passed

Real .NET → Python → LangGraph HTTP smoke test:

passed
```

The HTTP smoke test verified the actual cross-process boundary rather
than only mocking the Python service.

Checkpoint:

``` text

phase-9-complete
```

------------------------------------------------------------------------

# 24. Phase 10 --- Multi-Provider LLM Integration\*\*

Phase 10 changes the AI architecture from a provider-specific model
integration to a provider-independent model layer.

The major new file is:

``` text

python/agent_service/llm.py
```

The core abstraction includes:

``` text

LlmClient

LlmResponse

OpenAICompatibleClient

create_llm_client(...)

parse_json_object(...)
```

Provider-specific clients are built around this abstraction.

------------------------------------------------------------------------

# 25. Why the Provider Abstraction Was Added\*\*

The earlier architecture was effectively:

``` text

Agent

  ↓

NVIDIA NIM
```

That created an unnecessary dependency between agent logic and one model
provider.

The new architecture is:

``` text

Agent

  ↓

LlmClient

  ↓

Provider
```

Provider selection is configuration-driven.

This means the same triage code can execute against:

``` text

deterministic

openrouter

nvidia

openai

anthropic

google

azure_openai
```

without rewriting the triage workflow.

------------------------------------------------------------------------

# 26. Current Provider List\*\*

The supported provider identifiers are:

``` text

deterministic

nvidia

openrouter

openai

anthropic

google

azure_openai
```

------------------------------------------------------------------------

# 27. Deterministic Provider\*\*

Deterministic mode is not an external provider.

It is the offline execution path.

Use it for:

``` text

CI

Unit tests

Local development

Regression testing

No API credentials
```

Default:

``` text

AGENT_LLM_PROVIDER=deterministic
```

This mode must remain available.

The project should not make external LLM credentials mandatory for the
normal test suite.

------------------------------------------------------------------------

# 28. NVIDIA NIM Integration\*\*

NVIDIA NIM support was implemented using an OpenAI-compatible client.

Default endpoint:

``` text

https://integrate.api.nvidia.com/v1
```

Default model configured during development:

``` text

moonshotai/kimi-k3
```

Environment variables include:

``` text

NVIDIA_NIM_BASE_URL

NVIDIA_NIM_API_KEY

NVIDIA_NIM_MODEL

NVIDIA_NIM_TIMEOUT_SECONDS

NVIDIA_NIM_REASONING_EFFORT
```

The NVIDIA client also supports:

``` text

reasoning_effort
```

through the request payload.

------------------------------------------------------------------------

# 29. NVIDIA Live-Test Result\*\*

A real NVIDIA NIM smoke test was attempted.

The request initially timed out.

The timeout was increased and the request was also configured with lower
reasoning effort.

The request still timed out.

Therefore:

``` text

NVIDIA client implementation:

implemented

NVIDIA live endpoint verification:

not completed successfully
```

This is why the architecture was expanded to support other providers
instead of treating NVIDIA as the only available live model backend.

------------------------------------------------------------------------

# 30. OpenRouter Integration\*\*

OpenRouter was selected as the next live-provider target.

OpenRouter uses an OpenAI-compatible API.

Default endpoint:

``` text

https://openrouter.ai/api/v1
```

The client supports:

``` text

OPENROUTER_API_KEY

OPENROUTER_MODEL

OPENROUTER_TIMEOUT_SECONDS

OPENROUTER_HTTP_REFERER

OPENROUTER_X_TITLE
```

The provider uses the same generic:

``` text

OpenAICompatibleClient
```

request/response implementation.

The exact live OpenRouter model should be selected according to the
currently available OpenRouter model catalog.

Do not hard-code a model name in this README as a guaranteed current
model.

------------------------------------------------------------------------

# 31. OpenAI Integration\*\*

OpenAI support is implemented through the OpenAI-compatible abstraction.

Configuration uses:

``` text

OPENAI_API_KEY

OPENAI_MODEL
```

or the common:

``` text

AGENT_LLM_MODEL
```

The client follows the same:

``` text

system prompt

user prompt

temperature

max tokens

structured response

token usage

latency
```

contract.

------------------------------------------------------------------------

# 32. Google Integration\*\*

Google support is included in the provider factory.

Configuration supports:

``` text

GOOGLE_API_KEY

GOOGLE_MODEL
```

The provider-specific implementation is isolated from the agent
workflow.

------------------------------------------------------------------------

# 33. Azure OpenAI Integration\*\*

Azure OpenAI support is included in the provider abstraction.

The implementation supports Azure-specific endpoint/deployment
configuration while preserving the same agent-level interface.

The agent should not need to know whether a model is hosted through:

``` text

OpenAI

Azure OpenAI
```

------------------------------------------------------------------------

# 34. Anthropic Integration\*\*

Anthropic is supported through its native messages API implementation
rather than being forced into an incompatible request format.

The provider is still exposed through:

``` text

create_llm_client(...)
```

and therefore remains interchangeable at the agent layer.

------------------------------------------------------------------------

# 35. Common LLM Interface\*\*

The provider abstraction returns:

``` text

LlmResponse
```

which contains:

``` text

content

model

prompt_tokens

completion_tokens

latency_ms
```

This is important because provider-specific responses are normalized
before reaching the agent.

The triage agent therefore receives a consistent representation
regardless of provider.

------------------------------------------------------------------------

# 36. LLM Request Flow\*\*

The common OpenAI-compatible request path is:

``` text

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

------------------------------------------------------------------------

# 37. Structured Triage Output\*\*

The LLM triage prompt requests structured JSON.

Expected logical structure:

``` json

{

  "domain": "Network",

  "severity": "P2",

  "confidence": 0.91

}
```

The exact values are model-generated when a live provider is selected.

The application validates:

``` text

domain

severity

confidence
```

before converting the response into the internal model.

------------------------------------------------------------------------

# 38. Model Output Validation\*\*

LLM output is treated as untrusted input.

The application validates:

``` text

JSON format

Required fields

Domain values

Severity values

Confidence range

Non-empty model response
```

Malformed output becomes a controlled failure.

The system does not silently convert malformed model output into an
operational action.

------------------------------------------------------------------------

# 39. JSON Parsing\*\*

The provider layer contains:

``` text

parse_json_object(...)
```

It supports JSON returned directly by the provider and JSON wrapped in
common Markdown code fences.

The parser is deliberately kept separate from the provider transport
layer.

This allows response parsing to be unit tested without network access.

------------------------------------------------------------------------

# 40. LLM Environment Configuration\*\*

Example environment configuration:

``` text

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

------------------------------------------------------------------------

# 41. Common Model Selection\*\*

The common model variable is:

``` text

AGENT_LLM_MODEL
```

If it is set, it can provide the selected model for the chosen provider.

Provider-specific model variables are also supported.

The resolution order is designed so that an explicitly supplied model
can override environment defaults.

------------------------------------------------------------------------

# 42. Python Agent Models\*\*

The Python agent model layer contains:

``` text

TriageResult

StepTrace

AgentTrace
```

Trace information now supports:

``` text

agent

step_name

model

prompt_version

prompt_tokens

completion_tokens

latency_ms

summary
```

Default values are provided for trace fields that are not applicable to
deterministic/no-op steps.

------------------------------------------------------------------------

# 43. Phase 10 Triage Architecture\*\*

The triage flow is now:

``` text

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

This replaces the earlier NIM-specific branching with a provider-neutral
implementation.

------------------------------------------------------------------------

# 44. Phase 10 Testing Strategy\*\*

Phase 10 deliberately separates two categories of tests.

## Deterministic tests\*\*

These verify exact behavior.

Examples:

``` text

Known network incident → Network

Known identity incident → Identity

Unknown incident → Unknown

Deterministic confidence → expected value

Unsupported provider → controlled error
```

## Provider tests\*\*

These use mocked HTTP transport.

They verify:

``` text

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

## Phase 10 completion verification\*\*

Phase 10 was validated with:

``` text
Python test suite: 47 passed, 1 warning
git diff --check: passed
Commit: 1026ddd
Tag: phase-10-complete
GitHub master: pushed
GitHub phase-10-complete tag: pushed
```

The remaining warning is a Starlette/AnyIO dependency deprecation
warning and does not fail the Python test suite.

------------------------------------------------------------------------

# 45. Live Provider Testing\*\*

A live provider smoke test is different from a deterministic unit test.

A live smoke test should verify:

``` text

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

It should not require an exact severity or exact confidence because live
LLM outputs are not deterministic contracts.

------------------------------------------------------------------------

# 46. OpenRouter Test Procedure\*\*

From:

``` text

D:\Project\AIOps.AgentSwarm\python
```

activate the virtual environment:

``` powershell

.\\.venv\Scripts\Activate.ps1
```

Set the provider:

``` powershell

$env:AGENT_LLM_PROVIDER="openrouter"
```

Set the API key without putting it in source control:

``` powershell

$env:OPENROUTER_API_KEY="YOUR_REAL_OPENROUTER_KEY"
```

Set a currently available OpenRouter model:

``` powershell

$env:AGENT_LLM_MODEL="YOUR_OPENROUTER_MODEL"
```

Then run:

``` powershell

python -c "from agent_service.agents.triage import triage_ticket; result, trace = triage_ticket('VPN outage in production', 'Users cannot connect to the corporate VPN and are unable to work.'); print('DOMAIN:', result.domain); print('SEVERITY:', result.severity); print('CONFIDENCE:', result.confidence); print('MODEL:', trace.model); print('PROMPT TOKENS:', trace.prompt_tokens); print('COMPLETION TOKENS:', trace.completion_tokens); print('LATENCY MS:', round(trace.latency_ms, 2)); print('SUMMARY:', trace.summary)"
```

After the live test:

``` powershell

$env:AGENT_LLM_PROVIDER="deterministic"
```

Do not paste the API key into chat.

------------------------------------------------------------------------

# 47. Discover OpenRouter Models\*\*

If the selected model is unknown, the OpenRouter catalog can be queried.

Example:

``` powershell

python -c "import os,httpx; key=os.getenv('OPENROUTER_API_KEY',''); print('KEY:', 'SET' if key else 'NOT SET'); r=httpx.get('https://openrouter.ai/api/v1/models',headers={'Authorization':f'Bearer {key}'},timeout=30); print('STATUS:',r.status_code); print(r.text[:3000])"
```

Use the returned model identifier as:

``` text

AGENT_LLM_MODEL
```

This keeps model selection separate from application code.

------------------------------------------------------------------------

# 48. Python Virtual Environment\*\*

From:

``` text

D:\Project\AIOps.AgentSwarm\python
```

create the environment if necessary:

``` powershell

python -m venv .venv
```

Activate:

``` powershell

.\\.venv\Scripts\Activate.ps1
```

Install dependencies:

``` powershell

pip install -r requirements.txt
```

Expected development Python version:

``` text

Python 3.12.10
```

------------------------------------------------------------------------

# 49. Run Python Service\*\*

From:

``` text

D:\Project\AIOps.AgentSwarm\python
```

run:

``` powershell

uvicorn agent_service.main:app --host 127.0.0.1 --port 8000
```

Health check:

``` powershell

python -c "import httpx; r=httpx.get('http://127.0.0.1:8000/health',timeout=10); print(r.status_code); print(r.text)"
```

Expected health status:

``` text

200
```

------------------------------------------------------------------------

# 50. Run Python Tests\*\*

From:

``` text

D:\Project\AIOps.AgentSwarm\python
```

run:

``` powershell

pytest -q
```

The known Phase 9 baseline was:

``` text

9 passed
```

During Phase 10, the Python suite expanded substantially with provider
abstraction and LLM tests.

The current Phase 10 Python test suite result is:

``` text

47 passed, 1 warning
```

The warning is a Starlette/AnyIO dependency deprecation warning and does
not fail the test suite.

The Phase 10 validation also passed `git diff --check`.

------------------------------------------------------------------------

# 51. .NET Configuration\*\*

The .NET AI configuration contains:

``` text

AI:AgentGatewayMode

AI:AgentService:BaseUrl

AI:AgentService:TimeoutSeconds

AI:NvidiaNim:BaseUrl

AI:NvidiaNim:Model

AI:NvidiaNim:ApiKey
```

Default application configuration remains:

``` json

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

The NVIDIA configuration exists on the .NET side for the broader AI
configuration model, while the actual Phase 10 provider execution
currently happens in Python.

------------------------------------------------------------------------

# 52. .NET Agent Gateway Selection\*\*

The infrastructure registration supports:

``` text

AI:AgentGatewayMode=Fake
```

or:

``` text

AI:AgentGatewayMode=Http
```

Fake mode:

``` text

InProcessFakeAgentGateway
```

HTTP mode:

``` text

PythonAgentGateway
```

The default remains:

``` text

Fake
```

This is intentional.

It prevents the normal .NET test suite from requiring the Python service
to be running.

------------------------------------------------------------------------

# 53. PythonAgentGateway\*\*

The .NET HTTP gateway:

``` text

src/AIOps.Infrastructure/Agents/PythonAgentGateway.cs
```

implements:

``` text

IAgentGateway
```

It performs:

``` text

POST /api/v1/agent/runs

POST /api/v1/agent/runs/resume
```

The gateway maps:

``` text

Python outcome

→

AgentRunOutcome
```

and maps:

``` text

Python trace

→

.NET StepTrace
```

The gateway also handles HTTP and transport failures as controlled agent
failures.

------------------------------------------------------------------------

# 54. Orchestration\*\*

The orchestration layer contains:

``` text

Orchestrator
```

It is responsible for control-plane delegation.

Current agent operations include:

``` text

StartAgentRunAsync(...)

ResumeAgentRunAsync(...)
```

The orchestrator:

``` text

validates ticket existence

delegates to IAgentGateway

records audit events
```

The orchestrator does not directly depend on:

``` text

LangGraph

OpenRouter

NVIDIA NIM

OpenAI

Anthropic
```

This is deliberate architectural separation.

------------------------------------------------------------------------

# 55. Agent Run Contracts\*\*

The shared .NET contracts include:

``` text

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

``` text

Resolved

AwaitingApproval

Escalated

Failed
```

The contracts form the stable boundary between:

``` text

.NET control plane
```

and:

``` text

Python agent implementation
```

------------------------------------------------------------------------

# 56. Tool Proposal Contract\*\*

A tool proposal contains:

``` text

ToolName

ArgumentsJson

Confidence

Justification
```

The proposal is not an execution record.

It is an instruction candidate for the control plane.

The control plane must still apply:

``` text

Risk policy

Approval policy

Schema validation

Execution policy

Audit
```

------------------------------------------------------------------------

# 57. Resume Flow\*\*

The agent can be resumed after an approval/execution decision.

The resume contract includes:

``` text

CorrelationId

TicketId

ApprovalGranted

ApprovalDecidedBy

ToolResultJson

ToolExecutionSucceeded
```

The Python service uses this information to produce the next agent
outcome.

The execution itself remains outside Python.

------------------------------------------------------------------------

# 58. PostgreSQL and pgvector\*\*

PostgreSQL is used for:

``` text

Audit

Evaluation

Knowledge

Action execution

Approval state
```

pgvector is used for knowledge vectors.

Knowledge search uses:

``` text

vector similarity

cosine distance

HNSW indexing
```

The current embedding generator is deterministic.

------------------------------------------------------------------------

# 59. RAG Architecture\*\*

Current authoritative RAG architecture:

``` text

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

``` text

LangGraph

   ↓

Knowledge boundary

   ↓

Future integration point
```

The next major step is to connect these two pieces.

------------------------------------------------------------------------

# 60. Evaluation Architecture\*\*

Evaluation remains independent of the model provider.

Conceptually:

``` text

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

``` text

Resolution

Escalation

Approval behavior

Tool behavior

Latency

Token usage

Trace completeness

Repeatability
```

This becomes increasingly important as the number of LLM providers
grows.

------------------------------------------------------------------------

# 61. Audit Architecture\*\*

Audit is controlled by the .NET side.

Important events include:

``` text

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

``` text

What the model proposed
```

from:

``` text

What the system actually executed
```

------------------------------------------------------------------------

# 62. Safety Architecture\*\*

The safety model is:

``` text

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

``` text

Model

 ↓

Direct privileged API
```

This remains a fundamental design requirement.

------------------------------------------------------------------------

# 63. Current Limitations and Scope

Phase 14 is complete, but the platform is not presented as unlimited
autonomous remediation. The following remain future/expansion areas:

``` text
Broader model-driven post-execution reasoning
Larger autonomous remediation coverage
More enterprise providers as needed
Live verification of providers where credentials/accounts are unavailable
Advanced evaluation program from Phase 15
```

The following are already implemented and should not be described as
merely planned:

``` text
Python LangGraph workflow
.NET/PostgreSQL RAG integration
Tool proposal flow
Approval-aware execution/resume
Production integration abstractions/providers
Credential isolation
Operational telemetry
```

------------------------------------------------------------------------

# 64. Security and Secrets

Never commit real credentials such as:

``` text
NVIDIA NIM API keys
authorization tokens
Cisco Secure Access client secrets
Palo Alto API keys
Fortinet API tokens
Cisco ThousandEyes API tokens
AWS credentials
Microsoft Graph credentials
ITSM credentials
```

Use:

``` text
Environment variables
.NET user secrets
Deployment secret stores
Managed identity / secret-management systems where applicable
```

Credentials must not become:

``` text
Model prompt data
Agent trace data
Ordinary telemetry attributes
Git content
README content
```

Phase 14 added defense-in-depth serialization protection for sensitive
option values and safe telemetry boundaries.

------------------------------------------------------------------------

# 65. Phase 14 Credential Isolation

Sensitive option properties are protected from ordinary JSON
serialization where appropriate.

The security boundary is:

``` text
Provider credential
      ↓
Secure application configuration
      ↓
Provider implementation
      ↓
External API
```

not:

``` text
Credential
   ↓
Agent state
   ↓
LLM prompt
   ↓
Telemetry
```

The project also retains provider-specific security tests that verify
sensitive values are not exposed through normal results/contracts.

------------------------------------------------------------------------

# 66. Phase 14 Operational Telemetry

AidenOps now has a centralized safe telemetry helper:

``` text
AIOps.AgentSwarm
```

Instrumentation includes:

``` text
aiops.operations
aiops.operation.duration
```

Safe dimensions include:

``` text
aiops.category
aiops.provider
aiops.operation
aiops.outcome
error.type
aiops.correlation_id (when applicable)
```

Supported operation outcomes include:

``` text
success
failure
cancelled
timeout
```

Telemetry intentionally avoids recording:

``` text
Request payloads
Credentials
Passwords
Secrets
Tokens
Authorization material
Sensitive exception messages
```

The helper also bounds identifiers so arbitrary payload-like values
cannot become metric dimensions.

------------------------------------------------------------------------

# 67. Phase 14 Production Integrations

The production integration layer now includes:

## Cloud

``` text
AWS EC2
```

## Directory / Identity

``` text
Microsoft Graph / Entra ID
```

## ITSM

``` text
ServiceNow
Jira Service Management
Zendesk
```

## VPN / Network

``` text
Cisco Secure Access
Palo Alto GlobalProtect
Fortinet FortiGate
```

## Network Diagnostics

``` text
Cisco ThousandEyes
```

All providers remain behind stable interfaces and provider-selection
boundaries.

------------------------------------------------------------------------

# 68. Cisco ThousandEyes Integration

The ThousandEyes integration uses the Agent-to-Server Instant Test API
flow.

Conceptually:

``` text
AidenOps
   ↓
ThousandEyes provider
   ↓
Create agent-to-server instant test
   ↓
Poll result
   ↓
Normalize result
   ↓
GeneralNetworkDiagnosticResult
```

The normalized diagnostic result can represent:

``` text
Latency
Packet loss
Target/server
Server IP
Agent name
Agent location
Observation/test identifiers
Provider status
```

The implementation supports account-group and agent configuration. Live
verification requires valid ThousandEyes credentials and account/agent
configuration.

------------------------------------------------------------------------

# 69. Current Agent Execution Flow

The complete controlled flow is:

``` text
                         INCIDENT
                            │
                            ▼
                     ┌─────────────┐
                     │    Triage   │
                     └──────┬──────┘
                            │
                            ▼
                 ┌────────────────────┐
                 │ Knowledge Retrieval│
                 │ PostgreSQL+pgvector│
                 └─────────┬──────────┘
                           │
                           ▼
                 ┌────────────────────┐
                 │ Investigation      │
                 └─────────┬──────────┘
                           │
                           ▼
                 ┌────────────────────┐
                 │ Decision           │
                 └─────────┬──────────┘
                           │
                    ┌──────┴──────┐
                    │             │
                    ▼             ▼
                 Resolve      Tool Proposal
                                  │
                                  ▼
                         ┌─────────────────┐
                         │ .NET Validation │
                         └────────┬────────┘
                                  │
                         ┌────────┴────────┐
                         │                 │
                         ▼                 ▼
                    Safe Execute      Approval
                         │                 │
                         │          Human Decision
                         │                 │
                         └────────┬────────┘
                                  ▼
                           .NET ToolExecutor
                                  │
                                  ▼
                           Execution Result
                                  │
                                  ▼
                            Audit + Telemetry
                                  │
                                  ▼
                           Resume / Resolve /
                              Escalate
```

------------------------------------------------------------------------

# 70. Development Commands

## Build

``` powershell
dotnet build AIOps.slnx
```

## Full .NET test suite

``` powershell
dotnet test AIOps.slnx
```

## Test without rebuilding

``` powershell
dotnet test AIOps.slnx --no-build
```

## Python tests

``` powershell
cd D:\Project\AIOps.AgentSwarm\python
.\.venv\Scripts\Activate.ps1
pytest -q
```

## Git status

``` powershell
git status
```

## Git history

``` powershell
git log --oneline --decorate -10
```

------------------------------------------------------------------------

# 71. Safe Default Development Mode

Preferred normal development configuration:

``` text
.NET:
AI:AgentGatewayMode=Fake

Python:
AGENT_LLM_PROVIDER=deterministic
```

This provides:

``` text
No external LLM dependency
No API key requirement
Deterministic behavior
Repeatable tests
Fast local iteration
```

Real providers are enabled only for explicit integration testing.

------------------------------------------------------------------------

# 72. Real Provider Development Mode

The live model path is:

``` text
.NET
  ↓
PythonAgentGateway
  ↓
Python FastAPI
  ↓
LangGraph
  ↓
Provider-independent LLM client
  ↓
Selected provider
```

Example:

``` powershell
$env:AGENT_LLM_PROVIDER="openrouter"
$env:AGENT_LLM_MODEL="YOUR_CURRENT_MODEL"
$env:OPENROUTER_API_KEY="YOUR_REAL_KEY"
```

Never commit or paste a real API key into source control or README
files.

------------------------------------------------------------------------

# 73. Phase History --- Complete

``` text
Phase 1  — Foundation
Phase 2  — Ticket Lifecycle
Phase 3  — Persistence & Audit
Phase 4  — Tool Execution
Phase 5  — Agent Gateway
Phase 6  — Human Approval & Safety Controls
Phase 7  — RAG / Knowledge Retrieval
Phase 8  — Agent Evaluation & Observability Foundation
Phase 9  — Python / LangGraph Agent Swarm
Phase 10 — Multi-Provider LLM Integration
Phase 11 — RAG Context Injection / Reasoning
Phase 12 — Tool Proposal Agent
Phase 13 — Approval-Aware Execution Resume
Phase 14 — Production Integrations + Credential Isolation + Telemetry
Phase 15 — Advanced Evaluation / ML + LLM Evaluation   [NEXT]
```

------------------------------------------------------------------------

# 74. Phase 11 --- Completed

Phase 11 made the authoritative RAG subsystem an active participant in
Python reasoning.

Implemented flow:

``` text
Ticket
   ↓
Triage
   ↓
Knowledge Retrieval
   ↓
Relevant Knowledge Context
   ↓
Investigation
   ↓
Decision
   ↓
Resolve / Escalate
```

PostgreSQL + pgvector remains authoritative for knowledge retrieval.

------------------------------------------------------------------------

# 75. Phase 12 --- Completed

Phase 12 introduced the Tool Proposal Agent.

``` text
Reasoning
   ↓
Tool Selection
   ↓
Structured Tool Proposal
   ↓
.NET validation
   ↓
Risk classification
```

The model proposes a tool from the server-provided manifest/context.
Python does not execute privileged tools.

------------------------------------------------------------------------

# 76. Phase 13 --- Completed

Phase 13 completed the approval/execution resume loop.

``` text
Proposal
   ↓
Risk Classification
   ↓
Safe execution OR ApprovalRequest
   ↓
Human decision when required
   ↓
.NET ToolExecutor
   ↓
Persist execution/approval state
   ↓
Resume workflow when required
   ↓
Resolve / Escalate
```

The orchestrator validates ticket/action/approval state before resuming
and audits resume requests, decisions, responses, and failures.

------------------------------------------------------------------------

# 77. Phase 14 --- Completed

Phase 14 delivered three major groups of work:

### Production integrations

``` text
AWS EC2
Microsoft Graph / Entra ID
ServiceNow
Jira Service Management
Zendesk
Cisco Secure Access
Palo Alto GlobalProtect
Fortinet FortiGate
Cisco ThousandEyes
```

### Credential isolation

``` text
Sensitive configuration protection
Provider credential boundaries
Credential-safe contracts/results
Credential-safe integration tests
```

### Operational telemetry

``` text
Centralized safe instrumentation
Operation counters
Operation duration histograms
Outcome/error classification
Correlation support
Payload/secret avoidance
```

------------------------------------------------------------------------

# 78. Phase 15 --- Next Phase

Phase 15 is **Advanced Evaluation / ML + LLM Evaluation**.

It should build on the existing Phase 8 evaluation foundation.

Target areas:

``` text
Versioned golden datasets
Experiment tracking
TF-IDF + Logistic Regression baseline
LLM triage evaluation
RAG/retrieval evaluation
Investigation/decision evaluation
Tool-selection evaluation
Approval-policy evaluation
Provider/model comparison
Prompt comparison
Latency measurement
Token/cost measurement where available
Regression analysis
Persisted evaluation results
```

The objective is to answer measurable questions such as:

``` text
How accurately is the incident classified?
How relevant is the retrieved knowledge?
How often is the correct tool proposed?
Does the proposal respect policy/approval requirements?
How do providers/models compare on the same dataset?
Did a change introduce a regression?
What is the latency/token/cost impact?
```

Phase 15 does not redesign the .NET + Python architecture.

------------------------------------------------------------------------

# 79. Phase 15 Target Architecture

``` text
                    ┌─────────────────────┐
                    │ Versioned Golden    │
                    │ Dataset             │
                    └──────────┬──────────┘
                               ↓
                    ┌─────────────────────┐
                    │ Evaluation Runner   │
                    └──────────┬──────────┘
                               ↓
              ┌────────────────┼────────────────┐
              ↓                ↓                ↓
       ML Baseline        AidenOps Agent    Provider/Model
       TF-IDF + LR             Run           Variant
              │                │                │
              └────────────────┼────────────────┘
                               ↓
                    ┌─────────────────────┐
                    │ Metrics / Results   │
                    └──────────┬──────────┘
                               ↓
                    ┌─────────────────────┐
                    │ PostgreSQL          │
                    │ Experiments / Runs  │
                    └─────────────────────┘
```

------------------------------------------------------------------------

# 80. Long-Term End-to-End Architecture

``` text
                              USER / OPERATOR
                                    │
                                    ▼
                         ┌────────────────────┐
                         │ UI / API / ITSM    │
                         └─────────┬──────────┘
                                   │
                                   ▼
                  ┌────────────────────────────────┐
                  │        .NET CONTROL PLANE       │
                  │                                │
                  │ ASP.NET Core / UI              │
                  │ Orleans                        │
                  │ Ticket lifecycle               │
                  │ Agent orchestration            │
                  │ Policy / Safety                │
                  │ Approval                       │
                  │ Tool Registry                  │
                  │ Tool Executor                  │
                  │ Audit                          │
                  │ Evaluation                     │
                  │ Telemetry                      │
                  └───────┬───────────────┬────────┘
                          │               │
                          │ HTTP          │
                          ▼               ▼
               ┌────────────────┐   ┌────────────────────┐
               │ Python Agent   │   │ PostgreSQL         │
               │ Service        │   │ + pgvector         │
               │                │   │                    │
               │ FastAPI        │   │ Knowledge          │
               │ LangGraph      │   │ Audit              │
               │ Triage         │   │ Approvals          │
               │ RAG context    │   │ Actions            │
               │ Investigation  │   │ Evaluation         │
               │ Decision       │   │ Experiments        │
               │ Tool proposal  │   └────────────────────┘
               └───────┬────────┘
                       │
                       ▼
               ┌────────────────────┐
               │ LLM Provider Layer │
               │                    │
               │ Deterministic      │
               │ OpenRouter         │
               │ NVIDIA NIM         │
               │ OpenAI             │
               │ Anthropic          │
               │ Google             │
               │ Azure OpenAI       │
               └─────────┬──────────┘
                         │
                         ▼
               ┌────────────────────┐
               │ Enterprise         │
               │ Integrations       │
               │                    │
               │ AWS EC2            │
               │ Entra ID           │
               │ ServiceNow         │
               │ Jira SM            │
               │ Zendesk            │
               │ Cisco Secure       │
               │ Palo Alto          │
               │ Fortinet           │
               │ ThousandEyes       │
               └────────────────────┘
```

The future controlled loop remains:

``` text
AI reasoning
    ↓
Proposal
    ↓
Server-side validation
    ↓
Approval / policy
    ↓
Execution
    ↓
Audit
    ↓
Telemetry
    ↓
Evaluation
    ↓
Continuous improvement
```

------------------------------------------------------------------------

# 81. Final Architecture Rules

## Rule 1 --- .NET owns operational authority

The control plane owns:

``` text
Tickets
Approvals
Policy
Tool execution
Audit
Integration authority
Telemetry
```

## Rule 2 --- Python owns agent workflow logic

Python owns:

``` text
LangGraph workflow
Agent nodes
Triage
Knowledge-context handling
Investigation
Decision logic
Tool proposal generation
LLM interaction
Provider selection
```

It does not become the privileged execution authority.

## Rule 3 --- PostgreSQL owns persistent operational data

``` text
Knowledge
Audit
Evaluation
Action state
Approval state
Experiment state
```

## Rule 4 --- RAG is authoritative through the knowledge subsystem

Python consumes knowledge through the established boundary.

## Rule 5 --- Providers are replaceable

The agent workflow must not be rewritten when changing LLM or enterprise
providers.

## Rule 6 --- Deterministic mode remains available

External model availability must not block normal development and CI.

## Rule 7 --- Model output is untrusted input

Structured model output must be validated before entering operational
logic.

## Rule 8 --- No hidden tool execution

A proposal is never treated as evidence that an action occurred.

## Rule 9 --- Every consequential action is auditable

The system should be able to answer:

``` text
What was proposed?
Which agent produced it?
Which model produced it?
What knowledge was used?
Was approval required?
Who approved it?
What actually executed?
What was the result?
What happened afterward?
```

## Rule 10 --- Completed phases remain regression baselines

Future phases extend the completed architecture rather than
reimplementing it.

------------------------------------------------------------------------

# 82. Current Development Checkpoint

``` text
=============================================
                 AIDENOPS
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
Phase 10  COMPLETE
Phase 11  COMPLETE
Phase 12  COMPLETE
Phase 13  COMPLETE
Phase 14  COMPLETE

---------------------------------------------
CURRENT
---------------------------------------------

Phase 14
Production Integrations
Credential Isolation
Operational Telemetry

STATUS: COMPLETE

Full regression:
444 passed
0 failed
0 skipped

Latest commit:
1b9e750

---------------------------------------------
NEXT
---------------------------------------------

Phase 15
Advanced Evaluation / ML + LLM Evaluation

---------------------------------------------
ARCHITECTURE TO PRESERVE
---------------------------------------------

.NET Control Plane
        ↓
Python LangGraph Agent Service
        ↓
Provider-independent LLM Layer

with:

PostgreSQL + pgvector
Human Approval
Server-side Safety
Tool Registry
.NET Tool Execution
Persistent Audit
Production Integrations
Credential Isolation
Operational Telemetry
Deterministic Testing
Evaluation Foundation

=============================================
```

------------------------------------------------------------------------

# 83. Final Summary

AidenOps has evolved from a .NET operational prototype into a
multi-layer **enterprise AIOps agent platform**.

The completed architecture combines:

``` text
.NET Control Plane
        +
Orleans Orchestration
        +
Python FastAPI
        +
LangGraph
        +
RAG / pgvector
        +
Multi-provider LLMs
        +
Tool Proposal
        +
Server-side Safety
        +
Human Approval
        +
Controlled Execution
        +
Enterprise Integrations
        +
Credential Isolation
        +
Audit
        +
Operational Telemetry
        +
Evaluation Foundation
```

The most important boundary remains:

``` text
┌───────────────────────┐
│       AI / LLM        │
│                       │
│ Understands           │
│ Reasons               │
│ Retrieves context     │
│ Proposes actions      │
└───────────┬───────────┘
            │
            │ proposal
            ▼
┌───────────────────────┐
│    .NET CONTROL PLANE │
│                       │
│ Validates             │
│ Applies policy        │
│ Requests approval     │
│ Executes              │
│ Audits                │
│ Measures              │
└───────────────────────┘
```

**Phase 14 is the current completed implementation baseline.**

**Phase 15 builds on that baseline by making AidenOps substantially more
measurable through versioned datasets, experiment tracking, classical ML
baselines, agent/LLM evaluation, retrieval evaluation, provider
comparison, and regression analysis.**
