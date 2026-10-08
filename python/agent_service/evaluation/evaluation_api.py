from __future__ import annotations

from dataclasses import dataclass

from pydantic import BaseModel, ConfigDict, Field

from agent_service.evaluation.golden_dataset import (
    GoldenDataset,
    GoldenDatasetCase,
)
from agent_service.evaluation.llm_evaluator import (
    LLMEvaluator,
)
from agent_service.evaluation.llm_golden_dataset import (
    LLMGoldenDatasetEvaluator,
)
from agent_service.evaluation.llm_report import (
    LLMEvaluationReport,
    build_llm_evaluation_report,
)


class LLMEvaluationCaseRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    case_id: str = Field(min_length=1, alias="caseId")
    title: str
    description: str
    expected_domain: str = Field(alias="expectedDomain")
    expected_severity: str = Field(alias="expectedSeverity")


class LLMEvaluationRequest(BaseModel):
    model_config = ConfigDict(populate_by_name=True)

    dataset_id: str = Field(min_length=1, alias="datasetId")
    dataset_name: str = Field(min_length=1, alias="datasetName")
    dataset_version: str = Field(min_length=1, alias="datasetVersion")
    provider: str = Field(min_length=1)
    cases: list[LLMEvaluationCaseRequest] = Field(
        min_length=1
    )


def _to_camel_case(value: str) -> str:
    parts = value.split("_")
    return parts[0] + "".join(
        part[:1].upper() + part[1:]
        for part in parts[1:]
    )


def _to_api_dict(value):
    if isinstance(value, dict):
        return {
            _to_camel_case(key): _to_api_dict(item)
            for key, item in value.items()
        }

    if isinstance(value, list):
        return [_to_api_dict(item) for item in value]

    return value


@dataclass(frozen=True)
class LLMEvaluationService:
    def evaluate(
        self,
        request: LLMEvaluationRequest,
    ) -> LLMEvaluationReport:
        dataset = GoldenDataset(
            dataset_id=request.dataset_id,
            name=request.dataset_name,
            version=request.dataset_version,
            cases=tuple(
                GoldenDatasetCase(
                    case_id=case.case_id,
                    title=case.title,
                    description=case.description,
                    expected_domain=case.expected_domain,
                    expected_severity=case.expected_severity,
                )
                for case in request.cases
            ),
        )

        evaluator = LLMGoldenDatasetEvaluator(
            LLMEvaluator(request.provider)
        )

        predictions = evaluator.evaluate(dataset)

        return build_llm_evaluation_report(
            dataset_id=dataset.dataset_id,
            dataset_name=dataset.name,
            dataset_version=dataset.version,
            predictions=predictions,
        )


def serialize_llm_evaluation_response(
    report: LLMEvaluationReport,
) -> dict:
    return _to_api_dict(report.to_dict())