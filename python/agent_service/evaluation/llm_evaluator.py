from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from agent_service.agents.triage import triage_ticket
from agent_service.models.agent import StepTrace


@dataclass(frozen=True)
class LLMEvaluationExample:
    case_id: str
    title: str
    description: str
    expected_domain: str
    expected_severity: str


@dataclass(frozen=True)
class LLMEvaluationPrediction:
    case_id: str
    provider: str
    domain: str
    severity: str
    confidence: float
    domain_correct: bool
    severity_correct: bool
    triage_correct: bool
    model: str
    prompt_version: str
    prompt_tokens: int
    completion_tokens: int
    latency_ms: float
    trace: StepTrace

    @property
    def total_tokens(self) -> int:
        return self.prompt_tokens + self.completion_tokens


class LLMEvaluator:
    """
    Evaluates an existing AidenOps LLM provider against
    classification cases.

    This evaluator does not execute tools and does not alter
    the existing agent workflow.
    """

    def __init__(self, provider: str) -> None:
        provider = provider.strip().lower()

        if not provider:
            raise ValueError("Provider is required.")

        if provider == "deterministic":
            raise ValueError(
                "LLMEvaluator requires an LLM provider, "
                "not deterministic mode."
            )

        self.provider = provider

    def evaluate_case(
        self,
        example: LLMEvaluationExample,
    ) -> LLMEvaluationPrediction:
        if not example.case_id.strip():
            raise ValueError("Case id is required.")

        if not example.title.strip() and not example.description.strip():
            raise ValueError(
                "Title or description is required."
            )

        result, trace = triage_ticket(
            title=example.title,
            description=example.description,
            provider=self.provider,
)

        domain_correct = (
            result.domain == example.expected_domain
        )

        severity_correct = (
            result.severity == example.expected_severity
        )

        return LLMEvaluationPrediction(
            case_id=example.case_id,
            provider=self.provider,
            domain=result.domain,
            severity=result.severity,
            confidence=result.confidence,
            domain_correct=domain_correct,
            severity_correct=severity_correct,
            triage_correct=(
                domain_correct and severity_correct
            ),
            model=trace.model,
            prompt_version=trace.prompt_version,
            prompt_tokens=trace.prompt_tokens,
            completion_tokens=trace.completion_tokens,
            latency_ms=trace.latency_ms,
            trace=trace,
        )

    def evaluate(
        self,
        examples: Iterable[LLMEvaluationExample],
    ) -> list[LLMEvaluationPrediction]:
        examples = list(examples)

        if not examples:
            raise ValueError(
                "At least one evaluation example is required."
            )

        return [
            self.evaluate_case(example)
            for example in examples
        ]