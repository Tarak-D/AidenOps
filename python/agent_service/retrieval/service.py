import os
from typing import Any

import httpx

from agent_service.models.agent import AgentTrace


DEFAULT_CONTROL_PLANE_URL = "http://localhost:5166"


def _control_plane_url() -> str:
    return os.getenv(
        "AIOPS_CONTROL_PLANE_URL",
        DEFAULT_CONTROL_PLANE_URL,
    ).rstrip("/")


def _knowledge_search_url() -> str:
    return f"{_control_plane_url()}/api/v1/knowledge/search"


def retrieve_knowledge(
    query: str,
    limit: int = 5,
) -> tuple[list[dict[str, Any]], AgentTrace]:
    """
    Retrieve authoritative knowledge from the .NET control plane.

    The .NET/PostgreSQL knowledge store remains the authoritative
    retrieval implementation. Python consumes the retrieved context
    and does not access PostgreSQL directly.
    """

    normalized_query = query.strip()

    if not normalized_query:
        return [], AgentTrace(
            agent="KnowledgeAgent",
            step_name="knowledge",
            model="none",
            prompt_version="v1-http",
            summary="Knowledge retrieval skipped because the query was empty.",
        )

    if limit <= 0:
        raise ValueError("limit must be greater than zero")

    url = _knowledge_search_url()

    try:
        response = httpx.get(
            url,
            params={
                "q": normalized_query,
                "limit": limit,
            },
            timeout=10.0,
        )
        response.raise_for_status()

        payload = response.json()

        if not isinstance(payload, list):
            raise RuntimeError(
                "Knowledge API returned an unexpected response shape."
            )

        knowledge: list[dict[str, Any]] = []

        for item in payload:
            if not isinstance(item, dict):
                continue

            knowledge.append(
                {
                    "chunk_id": item.get("chunkId"),
                    "document_id": item.get("documentId"),
                    "source": item.get("source"),
                    "title": item.get("title"),
                    "chunk_index": item.get("chunkIndex"),
                    "content": item.get("content"),
                    "similarity": item.get("similarity"),
                    "metadata_json": item.get("metadataJson"),
                }
            )

        trace = AgentTrace(
            agent="KnowledgeAgent",
            step_name="knowledge",
            model="none",
            prompt_version="v1-http",
            summary=(
                f"Retrieved {len(knowledge)} knowledge result(s) "
                f"from the .NET control plane."
            ),
        )

        return knowledge, trace

    except httpx.HTTPError as exc:
        raise RuntimeError(
            f"Knowledge retrieval request failed: {exc}"
        ) from exc