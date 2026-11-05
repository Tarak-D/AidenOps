using AIOps.Abstractions.Audit;
using AIOps.Host.Security;

namespace AIOps.Host.Api;

/// <summary>
/// Read-only, administrator-authorized access to persisted audit events.
/// The endpoint deliberately exposes the existing append-only audit store only;
/// it does not alter or synthesize lifecycle records.
/// </summary>
public static class AuditEndpoints
{
    public static IEndpointRouteBuilder MapAuditApi(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/api/v1/audit/{entityType}/{entityId}",
                async (
                    string entityType,
                    string entityId,
                    int? take,
                    IAuditStore auditStore,
                    CancellationToken ct) =>
                {
                    if (take is < 1 or > 100)
                    {
                        return Results.ValidationProblem(
                            new Dictionary<string, string[]>
                            {
                                ["take"] = ["Take must be between 1 and 100."]
                            });
                    }

                    return Results.Ok(await auditStore.QueryAsync(
                        entityType,
                        entityId,
                        take: take ?? 100,
                        ct: ct));
                })
            .WithName("GetAuditEvents")
            .WithTags("Audit")
            .RequireAuthorization(AuthPolicies.CanViewAudit)
            .Produces<IReadOnlyList<AuditRecordView>>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces(StatusCodes.Status403Forbidden);

        return endpoints;
    }
}
