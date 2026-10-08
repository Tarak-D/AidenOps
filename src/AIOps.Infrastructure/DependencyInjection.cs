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
using Azure.Core;
using Azure.Identity;
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

        var directoryOptions = new DirectoryIntegrationOptions();
        config.GetSection(DirectoryIntegrationOptions.SectionName)
            .Bind(directoryOptions);

        if (directoryOptions.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:Directory:TimeoutSeconds must be greater than zero.");
        }

        services.Configure<DirectoryIntegrationOptions>(
            config.GetSection(DirectoryIntegrationOptions.SectionName));

        var ciscoSecureAccessOptions = new CiscoSecureAccessOptions();
        config.GetSection(CiscoSecureAccessOptions.SectionName)
            .Bind(ciscoSecureAccessOptions);

        if (ciscoSecureAccessOptions.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:NetworkDiagnostics:CiscoSecureAccess:TimeoutSeconds must be greater than zero.");
        }

        services.Configure<CiscoSecureAccessOptions>(
            config.GetSection(CiscoSecureAccessOptions.SectionName));

        var paloAltoOptions = new PaloAltoGlobalProtectOptions();
        config.GetSection(PaloAltoGlobalProtectOptions.SectionName)
            .Bind(paloAltoOptions);

        if (paloAltoOptions.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:NetworkDiagnostics:PaloAltoGlobalProtect:TimeoutSeconds must be greater than zero.");
        }

        services.Configure<PaloAltoGlobalProtectOptions>(
            config.GetSection(PaloAltoGlobalProtectOptions.SectionName));

        var fortinetOptions = new FortinetVpnOptions();
        config.GetSection(FortinetVpnOptions.SectionName)
            .Bind(fortinetOptions);

        if (fortinetOptions.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:NetworkDiagnostics:Fortinet:TimeoutSeconds must be greater than zero.");
        }

        services.Configure<FortinetVpnOptions>(
            config.GetSection(FortinetVpnOptions.SectionName));

        var thousandEyesOptions = new CiscoThousandEyesOptions();

config.GetSection(CiscoThousandEyesOptions.SectionName)
    .Bind(thousandEyesOptions);

if (thousandEyesOptions.TimeoutSeconds <= 0)
{
    throw new InvalidOperationException(
        "Integrations:NetworkDiagnostics:CiscoThousandEyes:TimeoutSeconds must be greater than zero.");
}

if (thousandEyesOptions.ResultPollAttempts <= 0)
{
    throw new InvalidOperationException(
        "Integrations:NetworkDiagnostics:CiscoThousandEyes:ResultPollAttempts must be greater than zero.");
}

if (thousandEyesOptions.ResultPollIntervalMilliseconds <= 0)
{
    throw new InvalidOperationException(
        "Integrations:NetworkDiagnostics:CiscoThousandEyes:ResultPollIntervalMilliseconds must be greater than zero.");
}

services.Configure<CiscoThousandEyesOptions>(
    config.GetSection(CiscoThousandEyesOptions.SectionName));

        var itsmOptions = new ItsmIntegrationOptions();
        config.GetSection(ItsmIntegrationOptions.SectionName).Bind(itsmOptions);

        if (itsmOptions.TimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:Itsm:TimeoutSeconds must be greater than zero.");
        }

        var jiraTimeoutSeconds = itsmOptions.JiraServiceManagement.TimeoutSeconds
            ?? itsmOptions.TimeoutSeconds;

        if (jiraTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:Itsm:JiraServiceManagement:TimeoutSeconds must be greater than zero.");
        }

        var zendeskTimeoutSeconds = itsmOptions.Zendesk.TimeoutSeconds
            ?? itsmOptions.TimeoutSeconds;

        if (zendeskTimeoutSeconds <= 0)
        {
            throw new InvalidOperationException(
                "Integrations:Itsm:Zendesk:TimeoutSeconds must be greater than zero.");
        }

        var itsmProviderKey =
            $"{ItsmIntegrationOptions.SectionName}:Provider";

        var itsmProviderWasConfigured =
            config is IConfigurationRoot itsmConfigurationRoot &&
            itsmConfigurationRoot.Providers.Any(provider =>
                provider.TryGet(itsmProviderKey, out _));

        var configuredItsmProvider = itsmProviderWasConfigured
            ? config[itsmProviderKey]
            : itsmOptions.Provider;

        itsmOptions.Provider =
            ItsmProviderNames.Normalize(configuredItsmProvider);

        services.Configure<ItsmIntegrationOptions>(
            config.GetSection(ItsmIntegrationOptions.SectionName));

        services.AddSingleton<IClock, SystemClock>();
        services.AddSingleton<TimeProvider>(TimeProvider.System);

        var providerKey =
            $"{CloudIntegrationOptions.SectionName}:Provider";

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
                            RegionEndpoint.GetBySystemName(
                                cloudOptions.Region);
                    }

                    return new AmazonEC2Client(awsConfig);
                });

                services.AddSingleton(serviceProvider =>
                    new Lazy<IAmazonEC2>(
                        () => serviceProvider.GetRequiredService<IAmazonEC2>()));

                services.AddSingleton<ICloudProvider, AwsEc2CloudProvider>();
                break;

            default:
                throw new InvalidOperationException(
                    "Integrations:Cloud:Provider must be 'Simulated' or 'AwsEc2'.");
        }

        services.AddSingleton<
            ICloudProviderStatusService,
            CloudProviderStatusService>();

        var directoryProviderKey =
            $"{DirectoryIntegrationOptions.SectionName}:Provider";

        var directoryProviderWasConfigured =
            config is IConfigurationRoot directoryConfigurationRoot &&
            directoryConfigurationRoot.Providers.Any(provider =>
                provider.TryGet(directoryProviderKey, out _));

        var directoryProvider = directoryProviderWasConfigured
            ? config[directoryProviderKey]
            : directoryOptions.Provider;

        if (directoryProvider is null)
        {
            throw new InvalidOperationException(
                "Integrations:Directory:Provider must be 'Simulated' or 'MicrosoftGraph'.");
        }

        switch (directoryProvider.Trim().ToUpperInvariant())
        {
            case "SIMULATED":
                services.AddSingleton<
                    IDirectoryService,
                    SimulatedDirectoryService>();
                break;

            case "MICROSOFTGRAPH":
                services.AddHttpClient(
                    "MicrosoftGraphDirectory",
                    client => client.Timeout =
                        TimeSpan.FromSeconds(
                            directoryOptions.TimeoutSeconds));

                services.AddSingleton<TokenCredential>(_ =>
                {
                    var credentialOptions =
                        new DefaultAzureCredentialOptions
                        {
                            TenantId = directoryOptions.TenantId,
                            ManagedIdentityClientId =
                                directoryOptions.ClientId,
                            WorkloadIdentityClientId =
                                directoryOptions.ClientId,
                            ExcludeAzureCliCredential = true,
                            ExcludeAzurePowerShellCredential = true,
                            ExcludeVisualStudioCredential = true,
                            ExcludeVisualStudioCodeCredential = true,
                            ExcludeInteractiveBrowserCredential = true,
                            ExcludeAzureDeveloperCliCredential = true,
                            ExcludeBrokerCredential = true
                        };

                    return new DefaultAzureCredential(
                        credentialOptions);
                });

                services.AddSingleton(serviceProvider =>
                {
                    var httpClient = serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("MicrosoftGraphDirectory");

                    return new Microsoft.Graph.GraphServiceClient(
                        httpClient,
                        serviceProvider.GetRequiredService<TokenCredential>(),
                        ["https://graph.microsoft.com/.default"]);
                });

                services.AddSingleton<
                    IDirectoryService,
                    MicrosoftGraphDirectoryService>();

                break;

            default:
                throw new InvalidOperationException(
                    "Integrations:Directory:Provider must be 'Simulated' or 'MicrosoftGraph'.");
        }

        services.AddSingleton<
            IItsmProviderConnector,
            SimulatedItsmConnector>();

        services.AddHttpClient(
            "ServiceNowItsm",
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<ItsmIntegrationOptions>>()
                    .Value;

                client.Timeout =
                    TimeSpan.FromSeconds(
                        options.TimeoutSeconds);
            });

        services.AddSingleton<ServiceNowItsmConnector>(
            serviceProvider =>
                new ServiceNowItsmConnector(
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("ServiceNowItsm"),
                    serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<ItsmIntegrationOptions>>(),
                    serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<IItsmProviderConnector>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<ServiceNowItsmConnector>());

        services.AddHttpClient(
            "JiraServiceManagementItsm",
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<ItsmIntegrationOptions>>()
                    .Value;

                var timeoutSeconds =
                    options.JiraServiceManagement.TimeoutSeconds
                    ?? options.TimeoutSeconds;

                client.Timeout =
                    TimeSpan.FromSeconds(timeoutSeconds);
            });

        services.AddSingleton<JiraServiceManagementItsmConnector>(
            serviceProvider =>
                new JiraServiceManagementItsmConnector(
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("JiraServiceManagementItsm"),
                    serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<ItsmIntegrationOptions>>()));

        services.AddSingleton<IItsmProviderConnector>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<JiraServiceManagementItsmConnector>());

        services.AddHttpClient(
            "ZendeskItsm",
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<ItsmIntegrationOptions>>()
                    .Value;

                client.Timeout =
                    TimeSpan.FromSeconds(
                        options.Zendesk.TimeoutSeconds
                        ?? options.TimeoutSeconds);
            });

        services.AddSingleton<ZendeskItsmConnector>(
            serviceProvider =>
                new ZendeskItsmConnector(
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("ZendeskItsm"),
                    serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<ItsmIntegrationOptions>>()));

        services.AddSingleton<IItsmProviderConnector>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<ZendeskItsmConnector>());

        services.AddSingleton<ItsmProviderSelectionService>();

        services.AddSingleton<IItsmProviderStatusService>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<ItsmProviderSelectionService>());

        services.AddSingleton<
            IItsmConnector,
            ConfiguredItsmConnector>();

        // ------------------------------------------------------------
        // Cisco Secure Access
        // ------------------------------------------------------------

        services.AddHttpClient(
            "CiscoSecureAccess",
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<CiscoSecureAccessOptions>>()
                    .Value;

                client.Timeout =
                    TimeSpan.FromSeconds(
                        options.TimeoutSeconds);
            });

        services.AddSingleton<CiscoSecureAccessVpnProvider>(
            serviceProvider =>
                new CiscoSecureAccessVpnProvider(
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("CiscoSecureAccess"),
                    serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<CiscoSecureAccessOptions>>(),
                    serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<INetworkDiagnosticProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<CiscoSecureAccessVpnProvider>());

        // ------------------------------------------------------------
        // Palo Alto GlobalProtect
        // ------------------------------------------------------------

        services.AddHttpClient(
            "PaloAltoGlobalProtect",
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<PaloAltoGlobalProtectOptions>>()
                    .Value;

                client.Timeout =
                    TimeSpan.FromSeconds(
                        options.TimeoutSeconds);
            });

        services.AddSingleton<PaloAltoGlobalProtectVpnProvider>(
            serviceProvider =>
                new PaloAltoGlobalProtectVpnProvider(
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("PaloAltoGlobalProtect"),
                    serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<PaloAltoGlobalProtectOptions>>(),
                    serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<INetworkDiagnosticProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<PaloAltoGlobalProtectVpnProvider>());

        // ------------------------------------------------------------
        // Fortinet FortiGate
        // ------------------------------------------------------------

        services.AddHttpClient(
            "FortinetVpn",
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<FortinetVpnOptions>>()
                    .Value;

                client.Timeout =
                    TimeSpan.FromSeconds(
                        options.TimeoutSeconds);
            });

        services.AddSingleton<FortinetVpnProvider>(
            serviceProvider =>
                new FortinetVpnProvider(
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("FortinetVpn"),
                    serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<FortinetVpnOptions>>(),
                    serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<INetworkDiagnosticProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<FortinetVpnProvider>());

        // ------------------------------------------------------------
        // Cisco ThousandEyes
        // ------------------------------------------------------------

        services.AddHttpClient(
            "CiscoThousandEyes",
            (serviceProvider, client) =>
            {
                var options = serviceProvider
                    .GetRequiredService<
                        Microsoft.Extensions.Options.IOptions<CiscoThousandEyesOptions>>()
                    .Value;

                client.Timeout =
                    TimeSpan.FromSeconds(
                        options.TimeoutSeconds);
            });

        services.AddSingleton<CiscoThousandEyesNetworkProvider>(
            serviceProvider =>
                new CiscoThousandEyesNetworkProvider(
                    serviceProvider
                        .GetRequiredService<IHttpClientFactory>()
                        .CreateClient("CiscoThousandEyes"),
                    serviceProvider
                        .GetRequiredService<
                            Microsoft.Extensions.Options.IOptions<CiscoThousandEyesOptions>>(),
                    serviceProvider.GetRequiredService<TimeProvider>()));

        services.AddSingleton<INetworkDiagnosticProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<CiscoThousandEyesNetworkProvider>());

        services.AddSingleton<IGeneralNetworkDiagnosticsProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<CiscoThousandEyesNetworkProvider>());

        // ------------------------------------------------------------
        // Remaining VPN provider slots
        // ------------------------------------------------------------

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.Vpn,
                "Cloudflare",
                "Cloudflare"));

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.Vpn,
                "Citrix",
                "Citrix"));

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.Vpn,
                "OpenVPN",
                "OpenVPN"));

        services.AddSingleton<
            INetworkDiagnosticProvider,
            SimulatedVpnDiagnosticsProvider>();

        // ------------------------------------------------------------
        // VPN provider execution registrations
        // ------------------------------------------------------------

        services.AddSingleton<IVpnDiagnosticsProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<CiscoSecureAccessVpnProvider>());

        services.AddSingleton<IVpnDiagnosticsProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<PaloAltoGlobalProtectVpnProvider>());

        services.AddSingleton<IVpnDiagnosticsProvider>(
            serviceProvider =>
                serviceProvider
                    .GetRequiredService<FortinetVpnProvider>());

        services.AddSingleton<IVpnDiagnosticsProvider>(
            _ =>
                new UnconfiguredVpnDiagnosticsProvider(
                    "Cloudflare",
                    "Cloudflare"));

        services.AddSingleton<IVpnDiagnosticsProvider>(
            _ =>
                new UnconfiguredVpnDiagnosticsProvider(
                    "Citrix",
                    "Citrix"));

        services.AddSingleton<IVpnDiagnosticsProvider>(
            _ =>
                new UnconfiguredVpnDiagnosticsProvider(
                    "OpenVPN",
                    "OpenVPN"));

        services.AddSingleton<
            IVpnDiagnosticsProvider,
            SimulatedVpnDiagnosticsProvider>();

        // ------------------------------------------------------------
        // General network provider slots
        // ------------------------------------------------------------

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.GeneralNetwork,
                "Datadog",
                "Datadog"));

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.GeneralNetwork,
                "Dynatrace",
                "Dynatrace"));

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.GeneralNetwork,
                "Zabbix",
                "Zabbix"));

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.GeneralNetwork,
                "PRTG",
                "PRTG"));

        services.AddSingleton<INetworkDiagnosticProvider>(
            new UnconfiguredNetworkDiagnosticProvider(
                NetworkDiagnosticCategories.GeneralNetwork,
                "Simulated",
                "Simulated",
                isSimulated: true));

        services.AddSingleton<
            INetworkDiagnosticProviderSelectionService,
            NetworkDiagnosticProviderSelectionService>();

        services.AddSingleton<SimulatedNetworkDiagnostics>();

        services.AddSingleton<
            INetworkDiagnostics,
            ConfiguredVpnDiagnostics>();

        services.AddSingleton<
            IGeneralNetworkDiagnostics,
            ConfiguredGeneralNetworkDiagnostics>();

        var connectionString =
            config.GetConnectionString("PostgresConnection");

        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            services.AddDbContextFactory<AIOpsDbContext>(
                options =>
                    options.UseNpgsql(
                        connectionString,
                        npgsqlOptions =>
                            npgsqlOptions.UseVector()));

            services.AddSingleton<
                IItsmProviderSelectionStore,
                PostgresItsmProviderSelectionStore>();

            services.AddSingleton<
                INetworkDiagnosticProviderSelectionStore,
                PostgresNetworkDiagnosticProviderSelectionStore>();

            services.AddScoped<
                IAuditStore,
                PostgresAuditStore>();

            services.AddScoped<
                IEvaluationStore,
                PostgresEvaluationStore>();

                services.AddScoped<
                IEvaluationDatasetStore,
                PostgresEvaluationDatasetStore>();
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
            services.AddSingleton<
                IAuditStore,
                InMemoryAuditStore>();

            services.AddSingleton<
                IEvaluationStore,
                PostgresEvaluationStore>();

            services.AddSingleton<
                IItsmProviderSelectionStore,
                UnavailableItsmProviderSelectionStore>();

            services.AddSingleton<
                INetworkDiagnosticProviderSelectionStore,
                UnavailableNetworkDiagnosticProviderSelectionStore>();
        }

        services.AddSingleton<
            ITicketRepository,
            InMemoryTicketRepository>();

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

services.AddHttpClient<
    ILlmEvaluationGateway,
    PythonLlmEvaluationGateway>(
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

        return services;
    }
}