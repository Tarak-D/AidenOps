from __future__ import annotations

from agent_service.evaluation.golden_dataset import (
    GoldenDataset,
)
from agent_service.evaluation.llm_evaluator import (
    LLMEvaluationExample,
    LLMEvaluationPrediction,
    LLMEvaluator,
)


class LLMGoldenDatasetEvaluator:
    """
    Evaluates an LLM against the versioned golden dataset.

    The golden dataset must provide expected severity values
    for every case because LLM evaluation checks both domain
    and severity.
    """

    def __init__(
        self,
        evaluator: LLMEvaluator,
    ) -> None:
        self._evaluator = evaluator

    def evaluate(
        self,
        dataset: GoldenDataset,
    ) -> list[LLMEvaluationPrediction]:
        dataset.validate()

        evaluation_cases: list[LLMEvaluationExample] = []

        for case in dataset.cases:
            if case.expected_severity is None:
                raise ValueError(
                    "Expected severity is required for LLM "
                    f"evaluation case '{case.case_id}'."
                )

            evaluation_cases.append(
                LLMEvaluationExample(
                    case_id=case.case_id,
                    title=case.title,
                    description=case.description,
                    expected_domain=case.expected_domain,
                    expected_severity=case.expected_severity,
                )
            )

        return self._evaluator.evaluate(
            evaluation_cases
        )