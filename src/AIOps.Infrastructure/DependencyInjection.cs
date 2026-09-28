using AIOps.Abstractions;
using AIOps.Abstractions.Agents;
using AIOps.Abstractions.Audit;
using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Evaluation;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Knowledge;
using AIOps.Abstractions.Persistence;
using AIOps.Abstractions.Time;
using AIOps.Infrastructure.Agents;
using AIOps.Infrastructure.Audit;
using AIOps.Infrastructure.EfCore;
using AIOps.Infrastructure.Evaluation;
using AIOps.Infrastructure.Integrations;
using AIOps.Infrastructure.Knowledge;
using AIOps.Infrastructure.Persistence;
using AIOps.Infrastructure.Time;
using Amazon;
using Amazon.EC2;
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
    /// Phase 5: swap the agent gateway to the Python/LangGraph HTTP service based on configuration.
    /// </summary>
    public static IServiceCollection AddAIOpsInfrastructure(
        this IServiceCollection services,
        IConfiguration config)
    {
        services.Configure<AIOptions>(
            config.GetSection(AIOptions.SectionName));

        services.Configure<ApprovalOptions>(
            config.GetSection(ApprovalOptions.SectionName));

        services.Configure<AgentPolicyOptions>(
            config.GetSection(AgentPolicyOptions.SectionName));

        var cloudOptions = new CloudIntegrationOptions();
        config.GetSection(CloudIntegrationOptions.SectionName).Bind(cloudOptions);

        if (cloudOptions.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:Cloud:TimeoutSeconds must be greater than zero.");
        }

        services.Configure<CloudIntegrationOptions>(
            config.GetSection(CloudIntegrationOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        var providerKey = $"{CloudIntegrationOptions.SectionName}:Provider";
        var providerWasConfigured =
            config is IConfigurationRoot configurationRoot &&
            configurationRoot.Providers.Any(provider =>
                provider.TryGet(providerKey, out _));
        var cloudProvider = providerWasConfigured
            ? config[providerKey]
            : cloudOptions.Provider;

        if (cloudProvider is null)
        {
            throw new InvalidOperationException(
                "Integrations:Cloud:Provider must be 'Simulated' or 'AwsEc2'.");
        }

        switch (cloudProvider.Trim().ToUpperInvariant())
        {
            case "SIMULATED":
                services.AddSingleton<ICloudProvider, SimulatedCloudProvider>();
                break;
            case "AWSEC2":
                services.AddSingleton<IAmazonEC2>(_ =>
                {
                    var awsConfig = new AmazonEC2Config
                    {
                        Timeout = TimeSpan.FromSeconds(
                            cloudOptions.TimeoutSeconds)
                    };

                    if (!string.IsNullOrWhiteSpace(cloudOptions.Region))
                    {
                        awsConfig.RegionEndpoint =
                            RegionEndpoint.GetBySystemName(cloudOptions.Region);
                    }

                    return new AmazonEC2Client(awsConfig);
                });
                services.AddSingleton<ICloudProvider, AwsEc2CloudProvider>();
                break;
            default:
                throw new InvalidOperationException(
                    "Integrations:Cloud:Provider must be 'Simulated' or 'AwsEc2'.");
        }
        services.AddSingleton<IDirectoryService, SimulatedDirectoryService>();
        services.AddSingleton<IItsmConnector, SimulatedItsmConnector>();
        services.AddSingleton<INetworkDiagnostics, SimulatedNetworkDiagnostics>();

        var connectionString =
            config.GetConnectionString("PostgresConnection");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContext<AIOpsDbContext>(
                options =>
                    options.UseNpgsql(
                        connectionString,
                        npgsqlOptions =>
                            npgsqlOptions.UseVector()));

            services.AddScoped<IAuditStore, PostgresAuditStore>();
            services.AddScoped<IEvaluationStore, PostgresEvaluationStore>();

            // Phase 7: RAG / knowledge retrieval.
            services.AddScoped<
                IEmbeddingGenerator,
                DeterministicEmbeddingGenerator>();

            services.AddScoped<
                IKnowledgeStore,
                PostgresKnowledgeStore>();

            services.AddScoped<
                IActionExecutionStore,
                PostgresActionExecutionStore>();

            services.AddScoped<
                IApprovalStore,
                PostgresApprovalStore>();

            services.AddScoped<
                IApprovalValidator,
                PostgresApprovalStore>();

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

        var gatewayMode =
            config.GetValue<string>(
                $"{AIOptions.SectionName}:AgentGatewayMode")
            ?? "Fake";

        if (string.Equals(
                gatewayMode,
                "Http",
                StringComparison.OrdinalIgnoreCase))
        {
            services.AddHttpClient<
                IAgentGateway,
                PythonAgentGateway>(
                (serviceProvider, client) =>
                {
                    var options =
                        serviceProvider
                            .GetRequiredService<
                                Microsoft.Extensions.Options.IOptions<AIOptions>>()
                            .Value;

                    if (!Uri.TryCreate(
                            options.AgentService.BaseUrl,
                            UriKind.Absolute,
                            out var baseUri))
                    {
                        throw new InvalidOperationException(
                            $"AI:AgentService:BaseUrl is invalid: " +
                            $"{options.AgentService.BaseUrl}");
                    }

                    client.BaseAddress = baseUri;

                    client.Timeout =
                        TimeSpan.FromSeconds(
                            options.AgentService.TimeoutSeconds);
                });
        }
        else
        {
            services.AddSingleton<
                IAgentGateway,
                InProcessFakeAgentGateway>();
        }

        return services;
    }
}
