using AIOps.Abstractions.Evaluation;
using AIOps.Infrastructure.EfCore;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace AIOps.Api.Tests;

public sealed class ApiTestWebApplicationFactoryTests
{
    [Fact]
    public void Test_host_starts_without_postgres_configuration()
    {
        using var factory = new ApiTestWebApplicationFactory();
        using var client = factory.CreateClient();

        var configuration =
            factory.Services.GetRequiredService<IConfiguration>();
        Assert.Equal("Development", factory.Services
            .GetRequiredService<IWebHostEnvironment>()
            .EnvironmentName);
        Assert.Equal(
            string.Empty,
            configuration.GetConnectionString("PostgresConnection"));
        Assert.True(configuration.GetValue<bool>(
            "AIOps:Testing:UseInMemoryOrleansStorage"));
        Assert.Null(
            factory.Services.GetService<
                IDbContextFactory<AIOpsDbContext>>());

        using var scope = factory.Services.CreateScope();
        Assert.Equal(
            "InMemoryEvaluationStore",
            scope.ServiceProvider.GetRequiredService<IEvaluationStore>()
                .GetType()
                .Name);
    }
}
