from __future__ import annotations

from dataclasses import dataclass
from typing import Iterable

from agent_service.evaluation.llm_evaluator import (
    LLMEvaluationPrediction,
)


@dataclass(frozen=True)
class LLMEvaluationMetrics:
    provider: str
    sample_count: int
    domain_accuracy: float
    severity_accuracy: float
    triage_accuracy: float
    average_confidence: float
    total_latency_ms: float
    average_latency_ms: float
    prompt_tokens: int
    completion_tokens: int
    total_tokens: int

    @classmethod
    def calculate(
        cls,
        predictions: Iterable[LLMEvaluationPrediction],
    ) -> "LLMEvaluationMetrics":
        predictions = list(predictions)

        if not predictions:
            raise ValueError(
                "At least one prediction is required."
            )

        provider = predictions[0].provider

        if any(
            prediction.provider != provider
            for prediction in predictions
        ):
            raise ValueError(
                "All predictions must use the same provider."
            )

        sample_count = len(predictions)

        domain_accuracy = (
            sum(
                prediction.domain_correct
                for prediction in predictions
            )
            / sample_count
        )

        severity_accuracy = (
            sum(
                prediction.severity_correct
                for prediction in predictions
            )
            / sample_count
        )

        triage_accuracy = (
            sum(
                prediction.triage_correct
                for prediction in predictions
            )
            / sample_count
        )

        average_confidence = (
            sum(
                prediction.confidence
                for prediction in predictions
            )
            / sample_count
        )

        total_latency_ms = sum(
            prediction.latency_ms
            for prediction in predictions
        )

        average_latency_ms = (
            total_latency_ms / sample_count
        )

        prompt_tokens = sum(
            prediction.prompt_tokens
            for prediction in predictions
        )

        completion_tokens = sum(
            prediction.completion_tokens
            for prediction in predictions
        )

        return cls(
            provider=provider,
            sample_count=sample_count,
            domain_accuracy=domain_accuracy,
            severity_accuracy=severity_accuracy,
            triage_accuracy=triage_accuracy,
            average_confidence=average_confidence,
            total_latency_ms=total_latency_ms,
            average_latency_ms=average_latency_ms,
            prompt_tokens=prompt_tokens,
            completion_tokens=completion_tokens,
            total_tokens=(
                prompt_tokens + completion_tokens
            ),
        )