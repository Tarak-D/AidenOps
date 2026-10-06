import pytest

from agent_service.evaluation.classical_ml import (
    ClassicalMLDomainClassifier,
)
from agent_service.evaluation.classical_ml_evaluator import (
    ClassicalMLEvaluationExample,
    ClassicalMLEvaluator,
)


def _classifier():
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


def test_evaluate_returns_accuracy():
    evaluator = ClassicalMLEvaluator(_classifier())

    cases = [
        ClassicalMLEvaluationExample(
            "VPN outage",
            "Corporate VPN is unavailable.",
            "Network",
        ),
        ClassicalMLEvaluationExample(
            "Password problem",
            "The user's account password is locked.",
            "Identity",
        ),
        ClassicalMLEvaluationExample(
            "Database outage",
            "The production database is unavailable.",
            "Database",
        ),
        ClassicalMLEvaluationExample(
            "CPU problem",
            "Production CPU usage is critically high.",
            "Infrastructure",
        ),
    ]

    result = evaluator.evaluate(cases)

    assert result.total_cases == 4
    assert result.correct_cases == 4
    assert result.accuracy == 1.0


def test_evaluate_returns_predictions_and_expected_domains():
    evaluator = ClassicalMLEvaluator(_classifier())

    cases = [
        ClassicalMLEvaluationExample(
            "VPN outage",
            "Corporate VPN is unavailable.",
            "Network",
        ),
        ClassicalMLEvaluationExample(
            "Database outage",
            "The production database is unavailable.",
            "Database",
        ),
    ]

    result = evaluator.evaluate(cases)

    assert result.predictions == (
        "Network",
        "Database",
    )

    assert result.expected_domains == (
        "Network",
        "Database",
    )


def test_evaluate_calculates_partial_accuracy():
    evaluator = ClassicalMLEvaluator(_classifier())

    cases = [
        ClassicalMLEvaluationExample(
            "VPN outage",
            "Corporate VPN is unavailable.",
            "Network",
        ),
        ClassicalMLEvaluationExample(
            "Password problem",
            "The user's account password is locked.",
            "Database",
        ),
    ]

    result = evaluator.evaluate(cases)

    assert result.total_cases == 2
    assert result.correct_cases == 1
    assert result.accuracy == 0.5


def test_evaluate_rejects_empty_dataset():
    evaluator = ClassicalMLEvaluator(_classifier())

    with pytest.raises(
        ValueError,
        match="At least one evaluation case",
    ):
        evaluator.evaluate([])
def test_evaluation_result_contains_model_metadata():
    evaluator = ClassicalMLEvaluator(_classifier())

    cases = [
        ClassicalMLEvaluationExample(
            "VPN outage",
            "Corporate VPN is unavailable.",
            "Network",
        ),
    ]

    result = evaluator.evaluate(cases)

    assert result.model_type == "classical_ml"
    assert result.model_name == "tfidf-logistic-regression"


def test_evaluation_result_to_dict_is_serializable():
    evaluator = ClassicalMLEvaluator(_classifier())

    cases = [
        ClassicalMLEvaluationExample(
            "VPN outage",
            "Corporate VPN is unavailable.",
            "Network",
        ),
        ClassicalMLEvaluationExample(
            "Database outage",
            "Production database is unavailable.",
            "Database",
        ),
    ]

    result = evaluator.evaluate(cases)

    payload = result.to_dict()

    assert payload["model_type"] == "classical_ml"
    assert payload["model_name"] == "tfidf-logistic-regression"
    assert payload["total_cases"] == 2
    assert payload["correct_cases"] == 2
    assert payload["accuracy"] == 1.0
    assert payload["predictions"] == [
        "Network",
        "Database",
    ]
    assert payload["expected_domains"] == [
        "Network",
        "Database",
    ]


def test_evaluation_result_to_dict_contains_no_model_object():
    evaluator = ClassicalMLEvaluator(_classifier())

    cases = [
        ClassicalMLEvaluationExample(
            "CPU spike",
            "Production server CPU is critically high.",
            "Infrastructure",
        ),
    ]

    result = evaluator.evaluate(cases)

    payload = result.to_dict()

    assert isinstance(payload, dict)
    assert all(
        not hasattr(value, "predict")
        for value in payload.values()
    )