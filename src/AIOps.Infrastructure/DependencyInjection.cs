using AIOps.Infrastructure.Integrations;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Infrastructure.Agents;
using AIOps.Infrastructure.Audit;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Evaluation;
using AIOps.Infrastructure.Persistence;
using AIOps.Infrastructure.Time;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace AIOps.Infrastructure;

public static class DependencyInjection
{
    /// <summary>
    /// Registers development implementations of all platform seams.
    /// Phase 3: persistence is EF Core + PostgreSQL for audit and evaluation.
    /// Phase 5: swap the agent gateway to the Python/LangGraph HTTP service based on configuration (AI:AgentGatewayMode).
    /// </summary>
    public static IServiceCollection AddAIOpsInfrastructure(this IServiceCollection services, IConfiguration config)
    {
        services.Configure<AIOptions>(config.GetSection(AIOptions.SectionName));
        services.Configure<ApprovalOptions>(config.GetSection(ApprovalOptions.SectionName));
        services.Configure<AgentPolicyOptions>(config.GetSection(AgentPolicyOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<ICloudProvider, SimulatedCloudProvider>();
        services.AddSingleton<IDirectoryService, SimulatedDirectoryService>();

        var connectionString = config.GetConnectionString("PostgresConnection");
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AIOpsDbContext>(options =>
                options.UseNpgsql(connectionString));

            services.AddSingleton<IAuditStore, PostgresAuditStore>();
            services.AddSingleton<IEvaluationStore, PostgresEvaluationStore>();

            services.AddHealthChecks()
                .AddNpgSql(connectionString);
        }
        else
        {
            // Fallback for dev/tests when no PostgreSQL connection is configured.
            services.AddSingleton<IAuditStore, InMemoryAuditStore>();
            services.AddSingleton<IEvaluationStore, PostgresEvaluationStore>();
        }

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
