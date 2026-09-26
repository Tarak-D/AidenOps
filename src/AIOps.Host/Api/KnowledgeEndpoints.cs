using AIOps.Abstractions.Knowledge;

namespace AIOps.Host.Api;

public static class KnowledgeEndpoints
{
    public static IEndpointRouteBuilder MapKnowledgeApi(
        this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints
            .MapGroup("/api/v1/knowledge")
            .WithTags("Knowledge");

        group.MapGet(
            "/search",
            async (
                string? q,
                int? limit,
                IKnowledgeStore knowledgeStore,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(q))
                {
                    return Results.BadRequest(new
                    {
                        error = "Query parameter 'q' is required."
                    });
                }

                var requestedLimit = limit ?? 5;

                if (requestedLimit <= 0 || requestedLimit > 50)
                {
                    return Results.BadRequest(new
                    {
                        error = "Query parameter 'limit' must be between 1 and 50."
                    });
                }

                var results = await knowledgeStore.SearchAsync(
                    q.Trim(),
                    requestedLimit,
                    cancellationToken);

                return Results.Ok(results);
            })
            .WithName("SearchKnowledge")
            .Produces<IReadOnlyList<KnowledgeSearchResult>>(
                StatusCodes.Status200OK)
            .Produces(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
