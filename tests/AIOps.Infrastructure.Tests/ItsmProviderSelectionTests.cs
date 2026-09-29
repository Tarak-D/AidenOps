using AIOps.Abstractions.Configuration;
using AIOps.Abstractions.Integrations;
using AIOps.Abstractions.Persistence;
using AIOps.Infrastructure;
using AIOps.Infrastructure.Integrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace AIOps.Infrastructure.Tests;

public sealed class ItsmProviderSelectionTests
{
    private sealed class MemorySelectionStore(string? provider = null)
        : IItsmProviderSelectionStore
    {
        public string? Provider { get; private set; } = provider;

        public Task<string?> GetProviderAsync(CancellationToken ct = default) =>
            Task.FromResult(Provider);

        public Task SetProviderAsync(
            string provider,
            string changedBy,
            CancellationToken ct = default)
        {
            Provider = provider;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public void MissingConfiguration_DefaultsToSimulated()
    {
        var services = new ServiceCollection();
        services.AddAIOpsInfrastructure(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        using var provider = services.BuildServiceProvider();

        Assert.Equal(
            "Simulated",
            provider.GetRequiredService<IOptions<ItsmIntegrationOptions>>().Value.Provider);
    }

    [Fact]
    public async Task InfrastructureRegistrationResolvesRealJiraConnectorWithoutNetworkWhenUnconfigured()
    {
        var services = new ServiceCollection();
        services.AddAIOpsInfrastructure(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        using var provider = services.BuildServiceProvider();

        var connector = provider.GetRequiredService<JiraServiceManagementItsmConnector>();

        Assert.Equal("JiraServiceManagement", connector.ProviderName);
        Assert.Equal("Real", connector.Mode);
        Assert.Equal("NotConfigured", await connector.GetConnectionStatusAsync());
    }

    [Fact]
    public async Task InfrastructureRegistrationResolvesRealZendeskConnectorWithoutNetworkWhenUnconfigured()
    {
        var services = new ServiceCollection();
        services.AddAIOpsInfrastructure(
            new ConfigurationBuilder().AddInMemoryCollection().Build());
        using var provider = services.BuildServiceProvider();

        var connector = provider.GetRequiredService<ZendeskItsmConnector>();

        Assert.Equal("Zendesk", connector.ProviderName);
        Assert.Equal("Real", connector.Mode);
        Assert.Equal("NotConfigured", await connector.GetConnectionStatusAsync());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Unknown")]
    public void InvalidConfiguredProvider_FailsClearly(string? configuredProvider)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Itsm:Provider"] = configuredProvider
            })
            .Build();
        var services = new ServiceCollection();

        var exception = Assert.Throws<InvalidOperationException>(() =>
            services.AddAIOpsInfrastructure(configuration));

        Assert.Equal(ItsmProviderNames.ConfigurationError, exception.Message);
    }

    [Theory]
    [InlineData("Simulated", "Simulated", "Simulated", false, "Simulated")]
    [InlineData("ServiceNow", "ServiceNow", "ServiceNow", true, "NotConfigured")]
    [InlineData("JiraServiceManagement", "JiraServiceManagement", "Jira Service Management", true, "NotConfigured")]
    [InlineData("Zendesk", "Zendesk", "Zendesk", true, "NotConfigured")]
    public async Task SelectionResolvesMatchingProviderAndTruthfulStatus(
        string selected,
        string expectedActive,
        string expectedDisplay,
        bool expectedProduction,
        string expectedConnectionStatus)
    {
        var store = new MemorySelectionStore();
        var providers = CreateProviders();
        var service = new ItsmProviderSelectionService(
            store,
            providers,
            Options.Create(new ItsmIntegrationOptions()));

        var status = await service.SelectProviderAsync(selected, "admin-user");

        Assert.Equal(selected, status.ConfiguredProvider);
        Assert.Equal(expectedActive, status.ActiveProvider);
        Assert.Equal(expectedDisplay, status.DisplayName);
        Assert.Equal(expectedProduction ? "Real" : "Simulated", status.Mode);
        Assert.Equal(expectedProduction, status.IsProduction);
        Assert.Equal(expectedConnectionStatus, status.ConnectionStatus);
        Assert.Equal(selected, await store.GetProviderAsync());
    }

    [Theory]
    [InlineData("ServiceNow")]
    [InlineData("JiraServiceManagement")]
    [InlineData("Zendesk")]
    public async Task RealProviderNeverFallsBackOrReturnsFabricatedSuccess(string selected)
    {
        var selection = new ItsmProviderSelectionService(
            new MemorySelectionStore(selected),
            CreateProviders(),
            Options.Create(new ItsmIntegrationOptions()));
        var router = new ConfiguredItsmConnector(selection, CreateProviders());

        var exception = await Record.ExceptionAsync(
            () => router.UpdateTicketAsync("INC-1", "note"));

        Assert.IsAssignableFrom<InvalidOperationException>(exception);
        var expectedCategory = selected switch
        {
            "ServiceNow" => "ServiceNowNotConfigured",
            "JiraServiceManagement" => "JiraNotConfigured",
            "Zendesk" => "ZendeskNotConfigured",
            _ => "ItsmProviderNotConfigured"
        };
        Assert.Contains(expectedCategory, exception.Message);
        if (selected != "JiraServiceManagement")
        {
            Assert.Contains(selected, exception.Message);
        }
    }

    private static IItsmProviderConnector[] CreateProviders() =>
    [
        new SimulatedItsmConnector(),
        new ServiceNowItsmConnector(
            new HttpClient(),
            Options.Create(new ItsmIntegrationOptions()),
            TimeProvider.System),
        new JiraServiceManagementItsmConnector(
            new HttpClient(),
            Options.Create(new ItsmIntegrationOptions())),
        new ZendeskItsmConnector(
            new HttpClient(),
            Options.Create(new ItsmIntegrationOptions()))
    ];
}
