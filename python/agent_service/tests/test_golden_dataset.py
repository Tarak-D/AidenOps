import pytest

from agent_service.evaluation.classical_ml import (
    ClassicalMLDomainClassifier,
)
from agent_service.evaluation.classical_ml_evaluator import (
    ClassicalMLEvaluator,
)
from agent_service.evaluation.golden_dataset import (
    GoldenDataset,
    GoldenDatasetCase,
    GoldenDatasetEvaluator,
)


def _evaluator():
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

    return GoldenDatasetEvaluator(
        ClassicalMLEvaluator(classifier)
    )


def test_golden_dataset_evaluation_returns_result():
    evaluator = _evaluator()

    dataset = GoldenDataset(
        dataset_id="golden-incidents",
        name="Golden Incident Dataset",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="vpn-001",
                title="VPN failure",
                description="Corporate VPN is unavailable.",
                expected_domain="Network",
            ),
            GoldenDatasetCase(
                case_id="db-001",
                title="Database outage",
                description="Production database is unavailable.",
                expected_domain="Database",
            ),
        ),
    )

    result = evaluator.evaluate(dataset)

    assert result.total_cases == 2
    assert result.correct_cases == 2
    assert result.accuracy == 1.0


def test_golden_dataset_preserves_case_order():
    evaluator = _evaluator()

    dataset = GoldenDataset(
        dataset_id="golden-incidents",
        name="Golden Incident Dataset",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="identity-001",
                title="Password reset",
                description="User needs a password reset.",
                expected_domain="Identity",
            ),
            GoldenDatasetCase(
                case_id="network-001",
                title="VPN failure",
                description="Corporate VPN is unavailable.",
                expected_domain="Network",
            ),
            GoldenDatasetCase(
                case_id="infra-001",
                title="CPU spike",
                description="Production CPU is critically high.",
                expected_domain="Infrastructure",
            ),
        ),
    )

    result = evaluator.evaluate(dataset)

    assert result.expected_domains == (
        "Identity",
        "Network",
        "Infrastructure",
    )

    assert result.predictions == (
        "Identity",
        "Network",
        "Infrastructure",
    )


def test_golden_dataset_requires_metadata():
    evaluator = _evaluator()

    dataset = GoldenDataset(
        dataset_id="",
        name="Golden Incident Dataset",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="case-001",
                title="VPN failure",
                description="VPN is unavailable.",
                expected_domain="Network",
            ),
        ),
    )

    with pytest.raises(
        ValueError,
        match="Dataset id is required",
    ):
        evaluator.evaluate(dataset)


def test_golden_dataset_requires_cases():
    evaluator = _evaluator()

    dataset = GoldenDataset(
        dataset_id="golden-incidents",
        name="Golden Incident Dataset",
        version="1.0.0",
        cases=(),
    )

    with pytest.raises(
        ValueError,
        match="at least one case",
    ):
        evaluator.evaluate(dataset)


def test_golden_dataset_requires_unique_case_ids():
    evaluator = _evaluator()

    dataset = GoldenDataset(
        dataset_id="golden-incidents",
        name="Golden Incident Dataset",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="duplicate",
                title="VPN failure",
                description="Corporate VPN is unavailable.",
                expected_domain="Network",
            ),
            GoldenDatasetCase(
                case_id="duplicate",
                title="Database outage",
                description="Production database is unavailable.",
                expected_domain="Database",
            ),
        ),
    )

    with pytest.raises(
        ValueError,
        match="case ids must be unique",
    ):
        evaluator.evaluate(dataset)


def test_golden_dataset_requires_case_ids():
    evaluator = _evaluator()

    dataset = GoldenDataset(
        dataset_id="golden-incidents",
        name="Golden Incident Dataset",
        version="1.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="",
                title="VPN failure",
                description="Corporate VPN is unavailable.",
                expected_domain="Network",
            ),
        ),
    )

    with pytest.raises(
        ValueError,
        match="Every golden dataset case must have an id",
    ):
        evaluator.evaluate(dataset)


def test_golden_dataset_version_is_preserved():
    dataset = GoldenDataset(
        dataset_id="golden-incidents",
        name="Golden Incident Dataset",
        version="2.0.0",
        cases=(
            GoldenDatasetCase(
                case_id="case-001",
                title="VPN failure",
                description="Corporate VPN is unavailable.",
                expected_domain="Network",
            ),
        ),
    )

    assert dataset.dataset_id == "golden-incidents"
    assert dataset.name == "Golden Incident Dataset"
    assert dataset.version == "2.0.0"
    assert len(dataset.cases) == 1