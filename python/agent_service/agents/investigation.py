from __future__ import annotations

import os
import time

from agent_service.llm import (
    create_llm_client,
    parse_json_object,
)
from agent_service.models.agent import StepTrace


_SUPPORTED_RECOMMENDATIONS = {
    "resolve",
    "escalate",
    "investigate_further",
}


def _deterministic_investigation(
    *,
    domain: str,
    severity: str,
    confidence: float,
    knowledge_context: str,
    knowledge_count: int,
) -> tuple[dict, StepTrace]:
    if knowledge_count == 0:
        summary = (
            "No knowledge evidence was retrieved "
            "for the incident."
        )

        recommendation = "investigate_further"
        evidence: list[str] = []

    else:
        summary = (
            f"Retrieved {knowledge_count} knowledge "
            f"result(s) for domain={domain}, "
            f"severity={severity}."
        )

        if confidence >= 0.75:
            recommendation = "resolve"
        else:
            recommendation = "investigate_further"

        evidence = [
            line
            for line in knowledge_context.splitlines()
            if (
                line.startswith("Source:")
                or line.startswith("Title:")
                or line.startswith("Content:")
            )
        ]

    result = {
        "summary": summary,
        "evidence": evidence,
        "recommendation": recommendation,
        "knowledge_count": knowledge_count,
        "knowledge_context": knowledge_context,
    }

    trace = StepTrace(
        agent="InvestigationAgent",
        step_name="deterministic_investigation",
        model="deterministic/bootstrap",
        prompt_version="v1-deterministic-rag",
        prompt_tokens=0,
        completion_tokens=0,
        latency_ms=0.0,
        summary=summary,
    )

    return result, trace


def _investigation_system_prompt(
    provider: str,
) -> str:
    if provider == "nvidia":
        return (
            "You are the InvestigationAgent in AidenOps. "
            "Analyze an IT incident using the provided "
            "retrieved knowledge context. "
            "Do not invent evidence. "
            "Return only valid JSON."
        )

    return (
        "You are the InvestigationAgent in AidenOps. "
        "Analyze the incident using the retrieved "
        "knowledge context. "
        "Base the investigation only on the supplied "
        "incident information and knowledge evidence. "
        "Do not invent evidence. "
        "Return only valid JSON."
    )


def _investigation_response_format(
    provider: str,
) -> dict:
    if provider == "openrouter":
        return {
            "type": "json_schema",
            "json_schema": {
                "name": "aidenops_investigation",
                "strict": True,
                "schema": {
                    "type": "object",
                    "properties": {
                        "summary": {
                            "type": "string",
                            "description": (
                                "Concise investigation summary."
                            ),
                        },
                        "evidence": {
                            "type": "array",
                            "items": {
                                "type": "string",
                            },
                            "description": (
                                "Evidence grounded in the "
                                "incident or retrieved knowledge."
                            ),
                        },
                        "recommendation": {
                            "type": "string",
                            "enum": [
                                "resolve",
                                "escalate",
                                "investigate_further",
                            ],
                            "description": (
                                "Investigation recommendation."
                            ),
                        },
                        "knowledge_count": {
                            "type": "integer",
                            "minimum": 0,
                            "description": (
                                "Number of supplied knowledge results."
                            ),
                        },
                    },
                    "required": [
                        "summary",
                        "evidence",
                        "recommendation",
                        "knowledge_count",
                    ],
                    "additionalProperties": False,
                },
            },
        }

    return {
        "type": "json_object",
    }


def _llm_investigation(
    *,
    title: str,
    description: str,
    domain: str,
    severity: str,
    confidence: float,
    knowledge_context: str,
    knowledge_count: int,
    provider: str,
) -> tuple[dict, StepTrace]:
    client = create_llm_client(
        provider,
    )

    system_prompt = _investigation_system_prompt(
        provider,
    )

    user_prompt = f"""
Incident title:
{title}

Incident description:
{description}

Domain:
{domain}

Severity:
{severity}

Triage confidence:
{confidence}

Retrieved knowledge count:
{knowledge_count}

Retrieved knowledge context:
{knowledge_context}

Analyze the incident using the retrieved knowledge.

Return JSON with:
- summary
- evidence
- recommendation
- knowledge_count

The recommendation must be one of:
- resolve
- escalate
- investigate_further

The knowledge_count must exactly match the supplied
retrieved knowledge count.

Do not invent evidence that is not present in the
incident information or retrieved knowledge.
""".strip()

    started = time.perf_counter()

    response = client.chat(
        system_prompt=system_prompt,
        user_prompt=user_prompt,
        temperature=0.0,
        max_tokens=768,
        response_format=_investigation_response_format(
            provider,
        ),
    )

    elapsed_ms = (
        time.perf_counter() - started
    ) * 1000.0

    payload = parse_json_object(
        response.content,
    )

    summary = payload.get(
        "summary",
    )

    if (
        not isinstance(summary, str)
        or not summary.strip()
    ):
        raise RuntimeError(
            "Investigation response contains "
            "an invalid summary."
        )

    evidence = payload.get(
        "evidence",
    )

    if not isinstance(
        evidence,
        list,
    ):
        raise RuntimeError(
            "Investigation response contains "
            "invalid evidence."
        )

    if not all(
        isinstance(item, str)
        for item in evidence
    ):
        raise RuntimeError(
            "Investigation evidence must contain "
            "only strings."
        )

    recommendation = payload.get(
        "recommendation",
    )

    if (
        recommendation
        not in _SUPPORTED_RECOMMENDATIONS
    ):
        raise RuntimeError(
            "Investigation response contains "
            "an invalid recommendation."
        )

    response_knowledge_count = payload.get(
        "knowledge_count",
    )

    if (
        not isinstance(
            response_knowledge_count,
            int,
        )
        or isinstance(
            response_knowledge_count,
            bool,
        )
    ):
        raise RuntimeError(
            "Investigation response contains "
            "an invalid knowledge_count."
        )

    if (
        response_knowledge_count
        != knowledge_count
    ):
        raise RuntimeError(
            "LLM investigation failed: "
            "response knowledge count does not match "
            "retrieved knowledge count."
        )

    result = {
        "summary": summary,
        "evidence": evidence,
        "recommendation": recommendation,
        "knowledge_count": response_knowledge_count,
        "knowledge_context": knowledge_context,
    }

    trace = StepTrace(
        agent="InvestigationAgent",
        step_name="llm_investigation",
        model=response.model,
        prompt_version=(
            f"v1-{provider}-rag-investigation"
        ),
        prompt_tokens=response.prompt_tokens,
        completion_tokens=response.completion_tokens,
        latency_ms=(
            response.latency_ms
            if response.latency_ms
            else elapsed_ms
        ),
        summary=summary,
    )

    return result, trace


def investigate_ticket(
    *,
    title: str,
    description: str,
    domain: str,
    severity: str,
    confidence: float,
    knowledge_context: str,
    knowledge_count: int,
) -> tuple[dict, StepTrace]:
    provider = os.getenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    ).strip().lower()

    if provider in {
        "",
        "deterministic",
    }:
        return _deterministic_investigation(
            domain=domain,
            severity=severity,
            confidence=confidence,
            knowledge_context=knowledge_context,
            knowledge_count=knowledge_count,
        )

    if provider == "azure":
        provider = "azure_openai"

    if provider == "azure-openai":
        provider = "azure_openai"

    supported_providers = {
        "nvidia",
        "openrouter",
        "openai",
        "anthropic",
        "google",
        "azure_openai",
    }

    if provider not in supported_providers:
        raise RuntimeError(
            f"Unsupported investigation provider: {provider}"
        )

    try:
        return _llm_investigation(
            title=title,
            description=description,
            domain=domain,
            severity=severity,
            confidence=confidence,
            knowledge_context=knowledge_context,
            knowledge_count=knowledge_count,
            provider=provider,
        )
    except RuntimeError:
        raise
    except Exception as exc:
        raise RuntimeError(
            f"Investigation failed using provider "
            f"'{provider}': {exc}"
        ) from exc