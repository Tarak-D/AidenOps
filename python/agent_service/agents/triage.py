import os

from agent_service.llm import (
    create_llm_client,
    parse_json_object,
)
from agent_service.models.agent import (
    StepTrace,
    TriageResult,
)


_SUPPORTED_DOMAINS = {
    "Network",
    "Identity",
    "Database",
    "Infrastructure",
    "Unknown",
}

_SUPPORTED_SEVERITIES = {
    "P1",
    "P2",
    "P3",
}


def _deterministic_triage(
    title: str,
    description: str,
) -> tuple[TriageResult, StepTrace]:
    text = (
        f"{title} {description}"
    ).lower()

    if (
        "vpn" in text
        or "network" in text
        or "wifi" in text
    ):
        domain = "Network"
    elif (
        "password" in text
        or "login" in text
        or "access" in text
        or "locked" in text
    ):
        domain = "Identity"
    elif (
        "database" in text
        or "sql" in text
        or "query" in text
    ):
        domain = "Database"
    elif (
        "server" in text
        or "disk" in text
        or "cpu" in text
        or "ec2" in text
    ):
        domain = "Infrastructure"
    else:
        domain = "Unknown"

    if domain == "Unknown":
        severity = "P3"
        confidence = 0.35
    else:
        if (
            "down" in text
            or "outage" in text
            or "production" in text
        ):
            severity = "P1"
        elif (
            "urgent" in text
            or "cannot work" in text
        ):
            severity = "P2"
        else:
            severity = "P3"

        confidence = 0.85

    result = TriageResult(
        domain=domain,
        severity=severity,
        confidence=confidence,
    )

    trace = StepTrace(
        agent="TriageAgent",
        step_name="triage",
        model="deterministic/bootstrap",
        prompt_version="v0-deterministic",
        prompt_tokens=0,
        completion_tokens=0,
        latency_ms=0.0,
        summary=(
            "Deterministic triage: "
            f"domain={domain}, "
            f"severity={severity}, "
            f"confidence={confidence:.2f}"
        ),
    )

    return result, trace


def _nim_system_prompt() -> str:
    return """
You are the TriageAgent for an enterprise AIOps platform.

Classify the incident into exactly one domain:
- Network
- Identity
- Database
- Infrastructure
- Unknown

Classify severity as exactly one of:
- P1
- P2
- P3

Return ONLY a JSON object with exactly these fields:

{
  "domain": "Network|Identity|Database|Infrastructure|Unknown",
  "severity": "P1|P2|P3",
  "confidence": 0.0
}

The confidence must be a number from 0.0 to 1.0.

Do not include explanations, markdown, or additional fields.
""".strip()


def _provider_system_prompt(
    provider: str,
) -> str:
    return f"""
You are the TriageAgent for an enterprise AIOps platform.

LLM provider:
{provider}

Classify the incident into exactly one domain:
- Network
- Identity
- Database
- Infrastructure
- Unknown

Classify severity as exactly one of:
- P1
- P2
- P3

Return ONLY a JSON object with exactly these fields:

{{
  "domain": "Network|Identity|Database|Infrastructure|Unknown",
  "severity": "P1|P2|P3",
  "confidence": 0.0
}}

The confidence must be a number from 0.0 to 1.0.

Do not include explanations, markdown, or additional fields.
""".strip()


def _llm_triage(
    title: str,
    description: str,
    provider: str,
) -> tuple[TriageResult, StepTrace]:
    client = create_llm_client(
        provider=provider,
    )

    user_prompt = f"""
Incident title:
{title}

Incident description:
{description}
""".strip()

    response = client.chat(
        system_prompt=(
            _nim_system_prompt()
            if provider == "nvidia"
            else _provider_system_prompt(provider)
        ),
        user_prompt=user_prompt,
        temperature=0.0,
        max_tokens=512,
        response_format={
            "type": "json_object",
        },
    )

    parsed = parse_json_object(
        response.content,
    )

    domain = parsed.get(
        "domain",
    )

    severity = parsed.get(
        "severity",
    )

    confidence = parsed.get(
        "confidence",
    )

    if domain not in _SUPPORTED_DOMAINS:
        raise ValueError(
            f"Unsupported model domain: {domain!r}"
        )

    if severity not in _SUPPORTED_SEVERITIES:
        raise ValueError(
            f"Unsupported model severity: {severity!r}"
        )

    try:
        confidence = float(confidence)
    except (
        TypeError,
        ValueError,
    ) as exc:
        raise ValueError(
            "Model confidence must be numeric."
        ) from exc

    confidence = max(
        0.0,
        min(
            1.0,
            confidence,
        ),
    )

    result = TriageResult(
        domain=domain,
        severity=severity,
        confidence=confidence,
    )

    trace = StepTrace(
        agent="TriageAgent",
        step_name="triage",
        model=response.model,
        prompt_version=(
            f"v1-{provider}-triage"
        ),
        prompt_tokens=response.prompt_tokens,
        completion_tokens=response.completion_tokens,
        latency_ms=response.latency_ms,
        summary=(
            "LLM triage: "
            f"provider={provider}, "
            f"domain={domain}, "
            f"severity={severity}, "
            f"confidence={confidence:.2f}"
        ),
    )

    return result, trace


def triage_ticket(
    title: str,
    description: str,
) -> tuple[TriageResult, StepTrace]:
    """
    Triage an incident using the configured LLM provider.

    Provider configuration:

      AGENT_LLM_PROVIDER=deterministic
      AGENT_LLM_PROVIDER=nvidia
      AGENT_LLM_PROVIDER=openrouter
      AGENT_LLM_PROVIDER=openai
      AGENT_LLM_PROVIDER=anthropic
      AGENT_LLM_PROVIDER=google
      AGENT_LLM_PROVIDER=azure_openai

    Deterministic mode remains the default so CI and local
    development do not require external model credentials.
    """

    provider = os.getenv(
        "AGENT_LLM_PROVIDER",
        "deterministic",
    ).strip().lower()

    if provider == "deterministic":
        return _deterministic_triage(
            title=title,
            description=description,
        )

    supported_providers = {
        "nvidia",
        "openrouter",
        "openai",
        "anthropic",
        "google",
        "azure",
        "azure_openai",
        "azure-openai",
    }

    if provider not in supported_providers:
        raise RuntimeError(
            "LLM triage failed: unsupported "
            f"AGENT_LLM_PROVIDER='{provider}'. Expected one of: "
            "deterministic, nvidia, openrouter, openai, "
            "anthropic, google, azure_openai."
        )

    normalized_provider = (
        "azure_openai"
        if provider in {
            "azure",
            "azure-openai",
        }
        else provider
    )

    try:
        return _llm_triage(
            title=title,
            description=description,
            provider=normalized_provider,
        )
    except Exception as exc:
        raise RuntimeError(
            "LLM triage failed for provider "
            f"'{normalized_provider}'. Set "
            "AGENT_LLM_PROVIDER=deterministic for "
            "offline operation."
        ) from exc