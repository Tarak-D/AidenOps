using AIOps.Abstractions.Grains;
using AIOps.Abstractions.Time;
using Microsoft.Extensions.DependencyInjection;
using Orleans.Hosting;
using Orleans.Serialization.Configuration;
using Orleans.TestingHost;

namespace AIOps.Grains.Tests;

public sealed class FixedClock : IClock
{
    public DateTimeOffset UtcNow { get; } = new(2026, 9, 7, 12, 0, 0, TimeSpan.Zero);
}

public sealed class TicketGrainFixture : IDisposable
{
    public TestCluster Cluster { get; }

    public TicketGrainFixture()
    {
        var builder = new TestClusterBuilder();
        builder.AddSiloBuilderConfigurator<SiloConfigurator>();
        Cluster = builder.Build();
        Cluster.Deploy();
    }

    /// <summary>
    /// Grain factory captured from the in-process silo. The test process' own client
    /// lacks client-side grain reference activators, but the in-process silo has full
    /// grain metadata, so tests resolve grains through the silo-hosted factory.
    /// </summary>
    public IGrainFactory GrainFactory =>
        SiloServices.Provider?.GetRequiredService<IGrainFactory>()
        ?? throw new InvalidOperationException("Silo service provider not captured.");

    /// <summary>Allow the grain-contract assemblies into the serializer type manifest.</summary>
    private static void AllowGrainAssemblies(IServiceCollection services) =>
        services.Configure<TypeManifestOptions>(o =>
        {
            o.AllowedAssemblies.Add(typeof(ITicketGrain).Assembly.GetName().Name!);
            o.AllowedAssemblies.Add(typeof(AIOps.Domain.TicketStatus).Assembly.GetName().Name!);
        });

    private sealed class SiloConfigurator : ISiloConfigurator
    {
        public void Configure(ISiloBuilder siloBuilder) =>
            siloBuilder
                .AddMemoryGrainStorage("ticketStore")
                .AddStartupTask<CaptureSiloStartupTask>()
                .ConfigureServices(s =>
                {
                    s.AddSingleton<IClock, FixedClock>();
                    AllowGrainAssemblies(s);
                });
    }

    public void Dispose() => Cluster.Dispose();
}

/// <summary>Static bridge set by the silo startup task so tests can resolve silo-side services.</summary>
public static class SiloServices
{
    public static IServiceProvider? Provider { get; set; }
}

/// <summary>Captures the silo service provider at startup.</summary>
public sealed class CaptureSiloStartupTask : IStartupTask
{
    private readonly IServiceProvider _services;

    public CaptureSiloStartupTask(IServiceProvider services) => _services = services;

    public Task Execute(CancellationToken cancellationToken)
    {
        SiloServices.Provider = _services;
        return Task.CompletedTask;
    }
}

[CollectionDefinition(Name)]
public sealed class GrainClusterCollection : ICollectionFixture<TicketGrainFixture>
{
    public const string Name = "GrainCluster";
}
