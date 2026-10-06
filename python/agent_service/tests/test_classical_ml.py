import pytest

from agent_service.evaluation.classical_ml import (
    ClassicalMLDomainClassifier,
)


def _training_data():
    titles = [
        "VPN connection failure",
        "Corporate network outage",
        "WiFi network unavailable",
        "Password reset required",
        "User login is locked",
        "Authentication access failure",
        "Database unavailable",
        "SQL query failure",
        "Database connection error",
        "Server CPU spike",
        "Disk space exhausted",
        "EC2 server failure",
    ]

    descriptions = [
        "The employee cannot connect to the corporate VPN.",
        "The office network is completely down.",
        "The wireless network is unavailable.",
        "The user needs a password reset.",
        "The account is locked and the user cannot login.",
        "The user cannot authenticate and access the system.",
        "The production database is unavailable.",
        "A SQL query is failing in production.",
        "The application cannot connect to the database.",
        "The production server CPU is critically high.",
        "The server disk is almost full.",
        "The EC2 server is not responding.",
    ]

    labels = [
        "Network",
        "Network",
        "Network",
        "Identity",
        "Identity",
        "Identity",
        "Database",
        "Database",
        "Database",
        "Infrastructure",
        "Infrastructure",
        "Infrastructure",
    ]

    return titles, descriptions, labels


def test_fit_and_predict_returns_supported_domain():
    titles, descriptions, labels = _training_data()

    classifier = ClassicalMLDomainClassifier()

    classifier.fit(
        titles,
        descriptions,
        labels,
    )

    prediction = classifier.predict(
        "VPN outage",
        "The corporate VPN connection is down.",
    )

    assert prediction.domain in {
        "Network",
        "Identity",
        "Database",
        "Infrastructure",
    }

    assert 0.0 <= prediction.confidence <= 1.0


def test_classifier_learns_expected_domains():
    titles, descriptions, labels = _training_data()

    classifier = ClassicalMLDomainClassifier()

    classifier.fit(
        titles,
        descriptions,
        labels,
    )

    cases = [
        (
            "VPN failure",
            "The employee cannot connect to the corporate VPN.",
            "Network",
        ),
        (
            "Password problem",
            "The user cannot login because the password is locked.",
            "Identity",
        ),
        (
            "Database outage",
            "The production database is unavailable.",
            "Database",
        ),
        (
            "CPU problem",
            "The production server CPU is critically high.",
            "Infrastructure",
        ),
    ]

    for title, description, expected_domain in cases:
        prediction = classifier.predict(
            title,
            description,
        )

        assert prediction.domain == expected_domain
        assert 0.0 <= prediction.confidence <= 1.0


def test_predict_batch_returns_prediction_for_each_case():
    titles, descriptions, labels = _training_data()

    classifier = ClassicalMLDomainClassifier()

    classifier.fit(
        titles,
        descriptions,
        labels,
    )

    predictions = classifier.predict_batch(
        [
            "VPN failure",
            "Database unavailable",
            "Server CPU spike",
        ],
        [
            "Corporate VPN is down.",
            "Production database is unavailable.",
            "Production server CPU is critically high.",
        ],
    )

    assert len(predictions) == 3

    assert all(
        0.0 <= prediction.confidence <= 1.0
        for prediction in predictions
    )


def test_fit_rejects_mismatched_training_lengths():
    classifier = ClassicalMLDomainClassifier()

    with pytest.raises(ValueError, match="same length"):
        classifier.fit(
            ["VPN failure"],
            ["VPN is down"],
            ["Network", "Database"],
        )


def test_fit_requires_two_training_samples():
    classifier = ClassicalMLDomainClassifier()

    with pytest.raises(
        ValueError,
        match="At least two training samples",
    ):
        classifier.fit(
            ["VPN failure"],
            ["VPN is down"],
            ["Network"],
        )


def test_fit_requires_two_distinct_labels():
    classifier = ClassicalMLDomainClassifier()

    with pytest.raises(
        ValueError,
        match="At least two distinct domain labels",
    ):
        classifier.fit(
            [
                "VPN failure",
                "Network outage",
            ],
            [
                "VPN is down",
                "Network is unavailable",
            ],
            [
                "Network",
                "Network",
            ],
        )


def test_predict_requires_fitted_classifier():
    classifier = ClassicalMLDomainClassifier()

    with pytest.raises(
        RuntimeError,
        match="must be fitted",
    ):
        classifier.predict(
            "VPN failure",
            "VPN is down.",
        )


def test_classes_requires_fitted_classifier():
    classifier = ClassicalMLDomainClassifier()

    with pytest.raises(
        RuntimeError,
        match="must be fitted",
    ):
        _ = classifier.classes


def test_classes_returns_training_labels():
    titles, descriptions, labels = _training_data()

    classifier = ClassicalMLDomainClassifier()

    classifier.fit(
        titles,
        descriptions,
        labels,
    )

    assert classifier.classes == (
        "Database",
        "Identity",
        "Infrastructure",
        "Network",
    )