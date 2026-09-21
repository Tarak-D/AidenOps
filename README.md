# AIOps Agent Swarm

An enterprise-oriented AI operations platform for incident triage, knowledge retrieval, safe tool execution, human approval, agent evaluation, observability, and autonomous operational workflows.

The project combines a .NET control plane with a Python/LangGraph agent swarm and a future NVIDIA NIM/Nemotron model integration.

## Project Status

### Current Phase

**Phase 9 — Python / LangGraph Agent Swarm — COMPLETE**

```text
Phase 1  — Foundation                         COMPLETE
Phase 2  — Ticket Lifecycle                   COMPLETE
Phase 3  — Persistence & Audit                COMPLETE
Phase 4  — Tool Execution                     COMPLETE
Phase 5  — Agent Gateway                      COMPLETE
Phase 6  — Human Approval & Safety Controls   COMPLETE
Phase 7  — RAG / Knowledge Retrieval          COMPLETE
Phase 8  — Agent Evaluation & Observability  COMPLETE
Phase 9  — Python / LangGraph Agent Swarm     COMPLETE
Phase 10 — NVIDIA NIM / Nemotron Integration  Next
```

### Current Checkpoint

```text
phase-9-complete
```

## 1. Vision

AIOps Agent Swarm is designed as a controlled autonomous operations platform.

The long-term goal is for an AI agent to:

1. Receive an incident or ticket
2. Understand and classify the incident
3. Retrieve operational knowledge
4. Reason about possible remediation
5. Select an appropriate tool
6. Validate the proposed action
7. Determine whether approval is required
8. Execute only permitted actions
9. Observe the result
10. Re-evaluate the incident
11. Resolve or escalate the incident
12. Record the complete execution history
13. Evaluate whether the agent performed correctly

Target flow:

```text
Incident / Ticket
       ↓
Ticket Grain
       ↓
Agent Run
       ↓
Triage
       ↓
Knowledge Retrieval
       ↓
Reasoning
       ↓
Tool Proposal
       ↓
Risk Classification
       ↓
Safe?
   ┌───┴───┐
   │       │
  Yes      No
   │       │
   │    Approval Request
   │       ↓
   │    Human Review
   │       ↓
   └───→ Tool Execution
             ↓
        Result / Evidence
             ↓
      Agent Re-evaluation
             ↓
      Resolve / Escalate
             ↓
            Audit
             ↓
        Evaluation
             ↓
       UI / SignalR
```

## 2. Architecture

```text
┌─────────────────────────────────────────────┐
│              ASP.NET Core / UI              │
│        APIs · Dashboard · SignalR           │
└──────────────────────┬──────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────┐
│             .NET Control Plane              │
│                                             │
│  Orleans                                     │
│  Ticket lifecycle                            │
│  Agent orchestration                         │
│  Approval / safety                           │
│  Tool execution                              │
│  Audit                                       │
│  Evaluation                                  │
└───────────────┬──────────────┬──────────────┘
                │              │
                ▼              ▼
       ┌────────────────┐   ┌────────────────┐
       │ PostgreSQL     │   │ RAG / Tools    │
       │ + pgvector     │   │ integrations   │
       └────────────────┘   └────────────────┘
                │
                ▼
┌─────────────────────────────────────────────┐
│          Python Agent Swarm                 │
│          LangGraph orchestration            │
└──────────────────────┬──────────────────────┘
                       │
                       ▼
┌─────────────────────────────────────────────┐
│          NVIDIA NIM / Nemotron              │
│          and future model providers          │
└─────────────────────────────────────────────┘
```

## 3. Technology Stack

### .NET

- .NET 10
- ASP.NET Core
- Orleans
- Entity Framework Core
- PostgreSQL
- pgvector
- xUnit
- Moq

### AI / Agent Layer

Target architecture:

- Python
- LangGraph
- NVIDIA NIM
- Nemotron
- Model-provider abstraction

Development currently uses a deterministic in-process fake agent gateway so local development and tests do not require external model access.

### Persistence

- PostgreSQL
- EF Core
- pgvector
- HNSW vector indexing
- Audit records
- Evaluation run persistence

## 4. Repository Structure

```text
src/
├── AIOps.Abstractions/
├── AIOps.Contracts/
├── AIOps.Domain/
├── AIOps.Grains/
├── AIOps.Host/
├── AIOps.Infrastructure/
├── AIOps.Orchestration/
└── AIOps.Tools/

tests/
├── AIOps.Abstractions.Tests/
├── AIOps.Api.Tests/
├── AIOps.Domain.Tests/
├── AIOps.Grains.Tests/
├── AIOps.Infrastructure.Tests/
├── AIOps.Orchestration.Tests/
└── AIOps.Tools.Tests/
```

### AIOps.Abstractions

Stable interfaces and cross-layer contracts.

Examples:

```text
IAgentGateway
IAuditStore
IEvaluationStore
IEmbeddingGenerator
IKnowledgeStore
```

### AIOps.Contracts

API and agent gateway DTOs.

### AIOps.Domain

Core domain models and lifecycle rules.

### AIOps.Grains

Orleans grains responsible for stateful ticket and operational workflows.

### AIOps.Infrastructure

Concrete infrastructure implementations:

```text
PostgreSQL
EF Core
Audit persistence
Evaluation persistence
Knowledge persistence
Fake agent gateway
External integration abstractions
```

### AIOps.Orchestration

Application-level orchestration including agent evaluation.

### AIOps.Tools

Tool definitions and execution infrastructure.

### AIOps.Host

ASP.NET Core host and Orleans co-hosting.

## 5. Development Environment

The project is developed and tested on Windows using PowerShell and Visual Studio Code.

Typical project location:

```text
D:\Project\AIOps.AgentSwarm
```

## 6. Build

From the repository root:

```powershell
dotnet build AIOps.slnx
```

## 7. Test

Run the complete test suite:

```powershell
dotnet test AIOps.slnx
```

For a faster run after a successful build:

```powershell
dotnet test AIOps.slnx --no-build
```

## 8. PostgreSQL

PostgreSQL is used for persistent operational data.

Primary areas include:

```text
Audit records
Evaluation runs
Action executions
Approval requests
Knowledge documents
Knowledge chunks
Vector embeddings
```

The knowledge retrieval layer uses PostgreSQL with pgvector.

## 9. Audit

Audit records are append-only by design.

Audit information includes:

```text
CorrelationId
ActorType
ActorId
EventType
EntityType
EntityId
PayloadJson
```

The correlation ID allows agent activity to be associated across the operational workflow.

## 10. Ticket Lifecycle

Tickets are managed through Orleans-backed lifecycle logic.

The platform models ticket states and validates transitions rather than allowing arbitrary state changes.

The lifecycle provides the foundation for:

```text
Creation
Triage
Investigation
Action execution
Approval
Resolution
Escalation
```

## 11. Agent Gateway

The .NET control plane communicates with agents through:

```text
IAgentGateway
```

The gateway exposes:

```text
StartRunAsync
ResumeRunAsync
```

Agent requests contain:

```text
CorrelationId
Ticket context
Allowed tools
Triage confidence threshold
Maximum attempts
```

Agent results contain:

```text
Outcome
Triage confidence
Domain
Severity
Tool proposal
Escalation summary
Resolution summary
Step trace
Error
```

Tool proposals contain:

```text
Tool name
Arguments JSON
Confidence
Justification
```

## 12. Deterministic Fake Agent

Development and CI use:

```text
InProcessFakeAgentGateway
```

The fake gateway is deterministic and offline.

It provides predictable classification for scenarios including:

```text
Network
Identity
Database
Infrastructure
Unknown
```

It also applies deterministic severity and tool-selection rules.

This allows the control plane, approval flow, and evaluation system to be tested without an external LLM.

## 13. Tool Execution

Tool manifests describe:

```text
Name
Description
Risk level
Approval requirement
Input schema
```

Example tools include:

```text
Aws.RestartEc2Instance
Network.RunVpnDiagnostics
Identity.ResetPassword
```

The control plane determines whether a proposed tool is safe to execute immediately or requires human approval.

## 14. Human Approval & Safety Controls

The safety flow is:

```text
Agent
  ↓
ActionExecution
  ↓
ApprovalRequest
  ↓
Human
  ↓
Approve / Reject
  ↓
Server Validation
  ↓
Tool Executor
  ↓
Tool
```

Approval must be:

```text
Server-side
Approved
Unexpired
Bound to the exact ActionExecution
Bound to the exact Ticket
```

The AI agent cannot self-approve a risky action.

Approval states:

```text
Pending
Approved
Rejected
Expired
```

Action execution states:

```text
Proposed
AwaitingApproval
Approved
Executing
Succeeded
Failed
Rejected
```

## 15. RAG / Knowledge Retrieval

Phase 7 introduced operational knowledge retrieval.

Architecture:

```text
Knowledge Document
       ↓
Chunking
       ↓
Embedding
       ↓
pgvector
       ↓
Similarity Search
       ↓
Ranked Results
       ↓
Agent Context
```

Core concepts:

```text
KnowledgeDocument
KnowledgeChunk
KnowledgeSearchResult
```

## 16. Embeddings

The retrieval layer is provider-independent.

The current implementation uses a deterministic local embedding generator for development and testing.

Current vector dimension:

```text
64
```

PostgreSQL stores embeddings using:

```text
vector(64)
```

The knowledge chunk table uses an HNSW index with cosine-distance operators.

## 17. Knowledge Persistence

Knowledge data is persisted through PostgreSQL.

Primary tables:

```text
knowledge_documents
knowledge_chunks
```

The knowledge chunk model includes:

```text
DocumentId
ChunkIndex
Content
Metadata
Embedding
CreatedAt
```

Relationship:

```text
KnowledgeDocument
       │
       └── KnowledgeChunk
              ├── Content
              ├── Metadata
              └── Embedding
```

## 18. Agent Evaluation & Observability

### Phase 8

Phase 8 introduces a deterministic evaluation framework for measuring agent behavior.

Evaluation flow:

```text
Evaluation Dataset
       ↓
Evaluation Runner
       ↓
Agent Gateway
       ↓
Prediction
       ↓
Expected Result
       ↓
Metric Calculation
       ↓
EvaluationRun
       ↓
PostgreSQL
```

The purpose is to measure whether changes to agent behavior improve operational performance.

## 19. Evaluation Metrics

Phase 8 measures:

```text
Triage accuracy
Domain classification accuracy
Severity classification accuracy
Tool selection accuracy
Tool argument validity
Approval rate
Execution success rate
Resolution rate
Escalation rate
Latency
Prompt token usage
Completion token usage
Retrieval relevance
```

Metrics are calculated from individual evaluation case results.

Latency is measured during evaluation execution.

Token usage is aggregated from agent StepTrace records.

Retrieval relevance is recorded when retrieval evaluation data is available.

## 20. Agent Observability

The existing `StepTrace` model provides the foundation for agent observability.

Trace information includes:

```text
Agent
Step name
Model
Prompt version
Prompt tokens
Completion tokens
Latency
Summary
```

Agent runs also preserve:

```text
CorrelationId
```

This allows multiple agent steps to be associated with one evaluation case.

## 21. Evaluation Dataset

Phase 8 contains seven deterministic incident scenarios:

```text
CPU spike
Disk full
VPN failure
Password compromise
Database unavailable
Deployment failure
Network outage
```

Each evaluation case specifies:

```text
Domain
Severity
Initial outcome
Expected tool
Expected resolution
Approval requirement
Allowed tools
Triage confidence threshold
Maximum attempts
```

Dataset name:

```text
phase-8-incident-evaluation
```

Dataset version:

```text
1.0
```

## 22. Evaluation Runner

The evaluation runner executes each dataset case against the configured agent gateway.

Process:

```text
Create evaluation case
       ↓
Start agent run
       ↓
Capture prediction
       ↓
Compare expected domain/severity/tool
       ↓
Calculate execution metrics
       ↓
Resume approval-required cases
       ↓
Capture final result
       ↓
Aggregate traces
       ↓
Calculate evaluation metrics
       ↓
Persist evaluation run
```

Approval-required evaluation cases use a synthetic successful tool result.

The evaluation runner does not execute real external operations.

This keeps the initial evaluation system safe and deterministic.

## 23. Evaluation Persistence

Evaluation runs use:

```text
IEvaluationStore
```

The PostgreSQL implementation is:

```text
PostgresEvaluationStore
```

Evaluation runs are stored in:

```text
evaluation_runs
```

Persisted information includes:

```text
Id
ExperimentId
ModelType
ModelName
PromptVersion
DatasetName
DatasetVersion
ConfigJson
SampleCount
StartedAt
FinishedAt
MetricsJson
Notes
```

## 24. Evaluation Repeatability

The deterministic gateway allows the same evaluation dataset to be executed repeatedly without:

```text
External model APIs
External tool systems
Production infrastructure
Network-dependent AI services
```

The evaluation tests verify that the important evaluation metrics remain stable across repeated runs.

Runtime latency is treated as environment-dependent rather than as a deterministic equality assertion.

## 25. Testing Strategy

Tests are organized by architectural layer:

```text
AIOps.Domain.Tests
AIOps.Abstractions.Tests
AIOps.Orchestration.Tests
AIOps.Infrastructure.Tests
AIOps.Grains.Tests
AIOps.Tools.Tests
AIOps.Api.Tests
```

Phase 8 added evaluation tests covering:

```text
Evaluation metric calculation
Evaluation runner execution
Evaluation repeatability
Evaluation run persistence
Evaluation dataset structure
Expected incident coverage
Tool selection
Tool argument validation
```

## 26. Phase History

### Phase 1 — Foundation

Initial platform foundation:

```text
Solution structure
Domain
Contracts
Abstractions
Host
Initial Orleans integration
Initial API foundation
```

### Phase 2 — Ticket Lifecycle

```text
Ticket creation
Ticket retrieval
Ticket lifecycle
Domain validation
Severity
Status transitions
```

### Phase 3 — Persistence & Audit

```text
PostgreSQL
EF Core
Audit persistence
Evaluation persistence contract
Health checks
```

### Phase 4 — Tool Execution

```text
Tool contracts
Tool manifests
Tool execution
Tool results
Risk metadata
Integration boundaries
```

### Phase 5 — Agent Gateway

```text
IAgentGateway
AgentRunRequest
AgentRunResult
ToolProposal
StepTrace
Fake agent gateway
Python/LangGraph gateway contract
```

### Phase 6 — Human Approval & Safety Controls

```text
ActionExecution
ApprovalRequest
Approval lifecycle
Risk classification
Server-side validation
Approval API
Audit events
```

Checkpoint:

```text
phase-6-complete
```

### Phase 7 — RAG / Knowledge Retrieval

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

Checkpoint:

```text
phase-7-complete
```

### Phase 8 — Agent Evaluation & Observability

```text
Evaluation cases
Evaluation datasets
Evaluation runner
Evaluation metrics
Agent trace aggregation
Latency measurement
Token measurement
Tool validation
Approval metrics
Resolution metrics
Escalation metrics
Evaluation persistence
Repeatability tests
```

Checkpoint:

```text
phase-8-complete
```

## 27. Development Commands

### Build

```powershell
dotnet build AIOps.slnx
```

### Run all tests

```powershell
dotnet test AIOps.slnx
```

### Run all tests without rebuilding

```powershell
dotnet test AIOps.slnx --no-build
```

### Run orchestration tests

```powershell
dotnet test .\tests\AIOps.Orchestration.Tests\AIOps.Orchestration.Tests.csproj
```

### Check Git state

```powershell
git status
```

### View recent history

```powershell
git log --oneline --decorate -10
```

## 28. Development Principles

### Stable abstractions

Cross-layer communication should happen through stable interfaces and contracts.

### Deterministic local development

External AI services should not be required for the core test suite.

### Safety before autonomy

AI-generated actions must pass server-side policy and approval controls before consequential execution.

### Audit important operations

Important agent and operational actions should have correlation identifiers and persistent audit records.

### Evaluation before model expansion

New models, prompts, and agent behavior should be evaluated against a repeatable dataset.

### Provider independence

Model and embedding providers should remain replaceable behind abstractions.

### No hidden execution

An agent result should not imply that an external action actually occurred unless the control plane records the execution result.

## 29. Future Direction

Future work will build toward:

```text
Real Python/LangGraph agent service
NVIDIA NIM integration
Nemotron models
Real RAG context injection into agent workflows
Production tool integrations
Advanced evaluation datasets
Model comparison
Prompt/version tracking
Agent performance dashboards
SignalR live execution views
Continuous evaluation
Agent improvement loops
```

The Phase 8 evaluation framework should remain as the regression baseline while these capabilities are introduced.

## 30. Long-Term Autonomous Flow

```text
Incident
   ↓
Ticket
   ↓
Ticket Grain
   ↓
Agent Run
   ↓
Triage Agent
   ↓
Knowledge Agent
   ↓
Reasoning Agent
   ↓
Tool Selection
   ↓
Risk Classification
   ↓
Approval Decision
   ↓
Human Approval when required
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

The system should remain controlled, observable, auditable, and testable as autonomy increases.

## 31. Phase 9 — Python / LangGraph Agent Swarm

Phase 9 introduced the real Python agent-service boundary while keeping the .NET control plane authoritative.

Architecture:

```text
.NET Control Plane
       ↓ HTTP
Python Agent Service
       ↓
FastAPI
       ↓
LangGraph
       ↓
Triage → Knowledge → Investigation → Decision
       ↓
Agent Result
       ↓ HTTP
.NET Control Plane
```

The Python service is located under:

```text
python/
├── requirements.txt
└── agent_service/
    ├── main.py
    ├── graph.py
    ├── agents/
    │   └── triage.py
    ├── models/
    │   └── agent.py
    ├── retrieval/
    │   └── service.py
    └── tests/
        ├── test_graph.py
        ├── test_health.py
        └── test_triage.py
```

### Python Agent Service

The service exposes:

```text
GET  /health
POST /api/v1/agent/runs
POST /api/v1/agent/runs/resume
```

The current LangGraph workflow is:

```text
START
  ↓
Triage
  ↓
Knowledge
  ↓
Investigation
  ↓
Decision
  ├── resolve → END
  └── escalate → END
```

The current implementation uses deterministic/bootstrap reasoning so the service remains offline-testable. NVIDIA NIM/Nemotron integration remains a future phase.

### .NET Agent Gateway

The existing `IAgentGateway` contract is reused for the Python boundary:

```text
IAgentGateway
├── StartRunAsync
└── ResumeRunAsync
```

`PythonAgentGateway` translates between the .NET agent contracts and the Python HTTP API.

The gateway is selected through:

```json
{
  "AI": {
    "AgentGatewayMode": "Fake"
  }
}
```

Supported modes:

```text
Fake  → deterministic in-process gateway
Http  → Python/LangGraph agent service
```

The default remains `Fake` so the normal development and CI test suite does not require the Python service or external model access.

### Control-plane authority

Python agents can:

```text
Reason
Retrieve knowledge
Investigate
Propose outcomes/actions
Return step traces
```

Python agents cannot independently:

```text
Approve actions
Execute privileged .NET tools
Change approval state
Bypass server-side policy
Directly mutate ticket lifecycle state
```

The .NET control plane remains authoritative for safety, approval, execution, audit, and ticket state.

### Phase 9 Verification

Python test suite:

```text
9 passed
1 warning
```

The warning is an upstream Starlette/AnyIO deprecation warning and does not represent an application test failure.

.NET regression suite:

```text
140 total
0 failed
140 succeeded
0 skipped
```

End-to-end HTTP smoke test:

```text
.NET Orchestrator
    ↓
PythonAgentGateway
    ↓ HTTP
Python FastAPI
    ↓
LangGraph
    ↓
Agent result
    ↓ HTTP
.NET Orchestrator
```

Result:

```text
1 passed
```

The temporary HTTP integration test was removed after verification, and the default gateway configuration was restored to `Fake`.

### Phase 9 Checkpoint

```text
phase-9-complete
```

## 32. Current Checkpoint

Current checkpoint:

```text
phase-9-complete
```

Next:

```text
Phase 10 — NVIDIA NIM / Nemotron Integration
```

## 33. Development Commands

### Build

```powershell
dotnet build AIOps.slnx
```

### Run all .NET tests

```powershell
dotnet test AIOps.slnx
```

### Run all .NET tests without rebuilding

```powershell
dotnet test AIOps.slnx --no-build
```

### Run orchestration tests

```powershell
dotnet test .\tests\AIOps.Orchestration.Tests\AIOps.Orchestration.Tests.csproj
```

### Run Python tests

From the Python service directory:

```powershell
Set-Location .\python
.\.venv\Scripts\Activate.ps1
python -m pytest
```

### Run the Python agent service

```powershell
Set-Location .\python
.\.venv\Scripts\Activate.ps1
uvicorn agent_service.main:app --host 127.0.0.1 --port 8000
```

### Check Python health

```powershell
Invoke-RestMethod http://127.0.0.1:8000/health
```

### Check Git state

```powershell
git status
```

### View recent history

```powershell
git log --oneline --decorate -10
```

## 34. Remaining Roadmap

The next phases build on the Phase 9 checkpoint:

```text
Phase 10 — NVIDIA NIM / Nemotron integration
Phase 11 — Real RAG context integration
Phase 12 — Full autonomous tool proposal/re-evaluation loop
Phase 13 — Approval/resume integration
Phase 14 — End-to-end autonomous operational workflow
Phase 15 — Reliability, security, evaluation, and production hardening
```

The deterministic Python workflow and the Phase 8 evaluation framework should remain regression baselines while model-backed autonomy is introduced.

## 35. Development Principles

### Stable abstractions

Cross-layer communication should happen through stable interfaces and contracts.

### Deterministic local development

External AI services should not be required for the core test suite.

### Safety before autonomy

AI-generated actions must pass server-side policy and approval controls before consequential execution.

### Audit important operations

Important agent and operational actions should have correlation identifiers and persistent audit records.

### Evaluation before model expansion

New models, prompts, and agent behavior should be evaluated against a repeatable dataset.

### Provider independence

Model and embedding providers should remain replaceable behind abstractions.

### No hidden execution

An agent result should not imply that an external action actually occurred unless the control plane records the execution result.

## 36. Long-Term Autonomous Flow

```text
Incident
   ↓
Ticket
   ↓
Ticket Grain
   ↓
Agent Run
   ↓
Triage Agent
   ↓
Knowledge Agent
   ↓
Reasoning Agent
   ↓
Tool Selection
   ↓
Risk Classification
   ↓
Approval Decision
   ↓
Human Approval when required
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

The system should remain controlled, observable, auditable, and testable as autonomy increases.
