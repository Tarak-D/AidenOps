using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Infrastructure.Agents;
using AIOps.Infrastructure.Audit;
using AIOps.Infrastructure.Persistence;
using AIOps.Infrastructure.Time;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIOps.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers development implementations of all platform seams.
    /// Phase 3 swaps persistence to EF Core/PostgreSQL; Phase 5 swaps the agent gateway
    /// to the Python/LangGraph HTTP service based on configuration (AI:AgentGatewayMode).
    /// </summary>
    public static IServiceCollection AddAIOpsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AIOptions>(config.GetSection(AIOptions.SectionName));
        services.Configure<ApprovalOptions>(config.GetSection(ApprovalOptions.SectionName));
        services.Configure<AgentPolicyOptions>(config.GetSection(AgentPolicyOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<IAuditStore, InMemoryAuditStore>();
        services.AddSingleton<ITicketRepository, InMemoryTicketRepository>();

        var gatewayMode = config.GetValue<string>($"{AIOptions.SectionName}:AgentGatewayMode") ?? "Fake";
        if (string.Equals(gatewayMode, "Http", StringComparison.OrdinalIgnoreCase))
        {
            // Phase 5: HttpAgentGateway targeting the Python/LangGraph service.
            throw new NotSupportedException(
                "AI:AgentGatewayMode=Http is not available until Phase 5. Use 'Fake'.");
        }
        services.AddSingleton<IAgentGateway, InProcessFakeAgentGateway>();

        return services;
    }
}
