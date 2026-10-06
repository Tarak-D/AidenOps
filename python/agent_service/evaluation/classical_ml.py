from dataclasses import dataclass
from typing import Sequence

from sklearn.feature_extraction.text import TfidfVectorizer
from sklearn.linear_model import LogisticRegression


@dataclass(frozen=True)
class ClassicalMLPrediction:
    domain: str
    confidence: float


class ClassicalMLDomainClassifier:
    """
    TF-IDF + Logistic Regression baseline for incident-domain classification.

    This classifier is intentionally independent from the existing LangGraph
    triage flow. It is used as a classical ML evaluation baseline.
    """

    def __init__(
        self,
        *,
        max_features: int = 5000,
        ngram_range: tuple[int, int] = (1, 2),
        random_state: int = 42,
    ) -> None:
        self._vectorizer = TfidfVectorizer(
            lowercase=True,
            strip_accents="unicode",
            max_features=max_features,
            ngram_range=ngram_range,
        )

        self._classifier = LogisticRegression(
            max_iter=1000,
            random_state=random_state,
        )

        self._fitted = False

    @staticmethod
    def _combine_text(
        title: str,
        description: str,
    ) -> str:
        return f"{title} {description}".strip()

    def fit(
        self,
        titles: Sequence[str],
        descriptions: Sequence[str],
        labels: Sequence[str],
    ) -> None:
        if not (
            len(titles)
            == len(descriptions)
            == len(labels)
        ):
            raise ValueError(
                "Titles, descriptions, and labels must have the same length."
            )

        if len(labels) < 2:
            raise ValueError(
                "At least two training samples are required."
            )

        if len(set(labels)) < 2:
            raise ValueError(
                "At least two distinct domain labels are required."
            )

        texts = [
            self._combine_text(title, description)
            for title, description in zip(
                titles,
                descriptions,
                strict=True,
            )
        ]

        features = self._vectorizer.fit_transform(texts)

        self._classifier.fit(features, labels)

        self._fitted = True

    def predict(
        self,
        title: str,
        description: str,
    ) -> ClassicalMLPrediction:
        if not self._fitted:
            raise RuntimeError(
                "The classical ML classifier must be fitted before prediction."
            )

        text = self._combine_text(title, description)

        features = self._vectorizer.transform([text])

        probabilities = self._classifier.predict_proba(features)[0]

        predicted_index = probabilities.argmax()

        domain = str(
            self._classifier.classes_[predicted_index]
        )

        confidence = float(
            probabilities[predicted_index]
        )

        return ClassicalMLPrediction(
            domain=domain,
            confidence=confidence,
        )

    def predict_batch(
        self,
        titles: Sequence[str],
        descriptions: Sequence[str],
    ) -> list[ClassicalMLPrediction]:
        if len(titles) != len(descriptions):
            raise ValueError(
                "Titles and descriptions must have the same length."
            )

        return [
            self.predict(title, description)
            for title, description in zip(
                titles,
                descriptions,
                strict=True,
            )
        ]

    @property
    def classes(self) -> tuple[str, ...]:
        if not self._fitted:
            raise RuntimeError(
                "The classical ML classifier must be fitted before accessing classes."
            )

        return tuple(
            str(label)
            for label in self._classifier.classes_
        )