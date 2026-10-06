from dataclasses import dataclass

from agent_service.evaluation.classical_ml import (
    ClassicalMLDomainClassifier,
)
from agent_service.evaluation.classical_ml_evaluator import (
    ClassicalMLEvaluationResult,
    ClassicalMLEvaluator,
)
from agent_service.evaluation.golden_dataset import (
    GoldenDataset,
    GoldenDatasetCase,
    GoldenDatasetEvaluator,
)


@dataclass(frozen=True)
class ClassicalMLBaselineReport:
    dataset_id: str
    dataset_name: str
    dataset_version: str
    model_type: str
    model_name: str
    total_cases: int
    correct_cases: int
    accuracy: float

    def to_dict(self) -> dict[str, object]:
        return {
            "dataset_id": self.dataset_id,
            "dataset_name": self.dataset_name,
            "dataset_version": self.dataset_version,
            "model_type": self.model_type,
            "model_name": self.model_name,
            "total_cases": self.total_cases,
            "correct_cases": self.correct_cases,
            "accuracy": self.accuracy,
        }


def build_baseline_classifier() -> ClassicalMLDomainClassifier:
    classifier = ClassicalMLDomainClassifier()

    classifier.fit(
        [
            "VPN failure",
            "Network outage",
            "Password reset",
            "Account locked",
            "Database unavailable",
            "SQL query failure",
            "CPU spike",
            "Disk full",
        ],
        [
            "Corporate VPN is down.",
            "Office network is unavailable.",
            "User needs a password reset.",
            "User account is locked.",
            "Production database is unavailable.",
            "Production SQL query failed.",
            "Production server CPU is critically high.",
            "Production server disk is full.",
        ],
        [
            "Network",
            "Network",
            "Identity",
            "Identity",
            "Database",
            "Database",
            "Infrastructure",
            "Infrastructure",
        ],
    )

    return classifier


def build_baseline_dataset() -> GoldenDataset:
    return GoldenDataset(
        dataset_id="phase15-classical-ml-baseline",
        name="Phase 15 Classical ML Baseline",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="network-001",
                title="VPN connection failure",
                description="The corporate VPN connection is unavailable.",
                expected_domain="Network",
            ),
            GoldenDatasetCase(
                case_id="network-002",
                title="Network outage",
                description="The office network is completely unavailable.",
                expected_domain="Network",
            ),
            GoldenDatasetCase(
                case_id="identity-001",
                title="Password reset",
                description="The user needs a password reset.",
                expected_domain="Identity",
            ),
            GoldenDatasetCase(
                case_id="identity-002",
                title="Locked account",
                description="The user account is locked and cannot login.",
                expected_domain="Identity",
            ),
            GoldenDatasetCase(
                case_id="database-001",
                title="Database unavailable",
                description="The production database is unavailable.",
                expected_domain="Database",
            ),
            GoldenDatasetCase(
                case_id="database-002",
                title="SQL query failure",
                description="A production SQL query is failing.",
                expected_domain="Database",
            ),
            GoldenDatasetCase(
                case_id="infrastructure-001",
                title="CPU spike",
                description="Production server CPU usage is critically high.",
                expected_domain="Infrastructure",
            ),
            GoldenDatasetCase(
                case_id="infrastructure-002",
                title="Disk full",
                description="Production server disk space is exhausted.",
                expected_domain="Infrastructure",
            ),
        ),
    )


def run_classical_ml_baseline() -> ClassicalMLBaselineReport:
    classifier = build_baseline_classifier()
    dataset = build_baseline_dataset()

    evaluator = GoldenDatasetEvaluator(
        ClassicalMLEvaluator(classifier)
    )

    result: ClassicalMLEvaluationResult = evaluator.evaluate(
        dataset
    )

    return ClassicalMLBaselineReport(
        dataset_id=dataset.dataset_id,
        dataset_name=dataset.name,
        dataset_version=dataset.version,
        model_type=result.model_type,
        model_name=result.model_name,
        total_cases=result.total_cases,
        correct_cases=result.correct_cases,
        accuracy=result.accuracy,
    )