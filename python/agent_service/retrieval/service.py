from agent_service.models.agent import AgentTrace


def retrieve_knowledge(
    query: str,
) -> tuple[list[dict], AgentTrace]:
    """
    Phase 9 retrieval boundary.

    The .NET/PostgreSQL RAG implementation remains authoritative for
    the existing Phase 7 retrieval system. This Python boundary will
    be connected to it in a later batch.
    """

    return [], AgentTrace(
        agent="KnowledgeAgent",
        step_name="knowledge",
        model="none",
        prompt_version="v0-bootstrap",
        summary=f"No Python retrieval provider configured for query: {query}",
    )