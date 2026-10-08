from dataclasses import dataclass
from typing import Sequence

from agent_service.evaluation.classical_ml_evaluator import (
    ClassicalMLEvaluationExample,
    ClassicalMLEvaluationResult,
    ClassicalMLEvaluator,
)


@dataclass(frozen=True)
class GoldenDatasetCase:
    case_id: str
    title: str
    description: str
    expected_domain: str
    expected_severity: str | None = None


@dataclass(frozen=True)
class GoldenDataset:
    dataset_id: str
    name: str
    version: str
    cases: tuple[GoldenDatasetCase, ...]

    def validate(self) -> None:
        if not self.dataset_id.strip():
            raise ValueError("Dataset id is required.")

        if not self.name.strip():
            raise ValueError("Dataset name is required.")

        if not self.version.strip():
            raise ValueError("Dataset version is required.")

        if not self.cases:
            raise ValueError(
                "Golden dataset must contain at least one case."
            )

        case_ids = [case.case_id for case in self.cases]

        if any(not case_id.strip() for case_id in case_ids):
            raise ValueError(
                "Every golden dataset case must have an id."
            )

        if len(case_ids) != len(set(case_ids)):
            raise ValueError(
                "Golden dataset case ids must be unique."
            )


class GoldenDatasetEvaluator:
    """
    Converts a versioned golden dataset into the generic evaluation
    examples consumed by the classical ML evaluator.
    """

    def __init__(
        self,
        evaluator: ClassicalMLEvaluator,
    ) -> None:
        self._evaluator = evaluator

    def evaluate(
        self,
        dataset: GoldenDataset,
    ) -> ClassicalMLEvaluationResult:
        dataset.validate()

        evaluation_cases = tuple(
            ClassicalMLEvaluationExample(
                title=case.title,
                description=case.description,
                expected_domain=case.expected_domain,
            )
            for case in dataset.cases
        )

        return self._evaluator.evaluate(
            evaluation_cases
        )