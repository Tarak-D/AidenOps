from dataclasses import dataclass


@dataclass(frozen=True)
class TriageResult:
    domain: str
    severity: str
    confidence: float


@dataclass(frozen=True)
class StepTrace:
    agent: str
    step_name: str
    model: str
    prompt_version: str
    prompt_tokens: int
    completion_tokens: int
    latency_ms: float
    summary: str


@dataclass(frozen=True)
class AgentTrace:
    agent: str
    step_name: str
    model: str
    prompt_version: str
    summary: str
    prompt_tokens: int = 0
    completion_tokens: int = 0
    latency_ms: float = 0.0