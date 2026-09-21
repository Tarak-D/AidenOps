from pydantic import BaseModel, Field


class ToolProposal(BaseModel):
    tool_name: str
    arguments_json: str
    confidence: float
    justification: str


class AgentTrace(BaseModel):
    agent: str
    step_name: str
    model: str
    prompt_version: str
    prompt_tokens: int = 0
    completion_tokens: int = 0
    latency_ms: float = 0.0
    summary: str = ""


class TriageResult(BaseModel):
    domain: str
    severity: str
    confidence: float


class AgentResult(BaseModel):
    outcome: str
    triage: TriageResult
    tool_proposal: ToolProposal | None = None
    trace: list[AgentTrace] = Field(default_factory=list)
    error: str | None = None