using JobPortal.Application.Abstractions.Jobs;
using JobPortal.Application.Abstractions.Persistence;
using JobPortal.Application.Features.JobDiscovery;
using JobPortal.Application.Services;
using JobPortal.Domain.Enums;
using JobPortal.Infrastructure;
using JobPortal.Infrastructure.Services;
using JobPortal.Persistence;
using JobPortal.Persistence.Repositories;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace JobPortal.Application.Tests;

public sealed class JobAggregationDependencyInjectionTests
{
    [Fact]
    public void AggregationServicesResolveWithScopedRepositoriesAndExistingClock()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(
            new Dictionary<string, string?>
            {
                // DI construction only: no connection is opened.
                ["ConnectionStrings:DefaultConnection"] = "Host=localhost;Database=di_only"
            }).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton(TimeProvider.System);
        services.AddApplication();
        services.AddPersistence(configuration);
        services.AddInfrastructure(configuration);

        AssertScoped<IJobIngestionService, JobIngestionService>(services);
        AssertScoped<IJobSourceRunner, JobSourceRunner>(services);
        AssertScoped<IJobSourceRepository, JobSourceRepository>(services);
        Assert.Single(services, x => x.ServiceType == typeof(IJobRepository));
        Assert.Single(services, x => x.ServiceType == typeof(TimeProvider));
        AssertScoped<IExternalJobSourceProvider, AdzunaJobSourceProvider>(services);

        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        using var scope = provider.CreateScope();
        Assert.IsType<JobIngestionService>(scope.ServiceProvider.GetRequiredService<IJobIngestionService>());
        Assert.IsType<JobSourceRunner>(scope.ServiceProvider.GetRequiredService<IJobSourceRunner>());
        var providers = scope.ServiceProvider.GetServices<IExternalJobProvider>().ToArray();
        Assert.Equal(2, providers.Length);
        Assert.Contains(providers, x => x is GreenhouseExternalJobProvider);
        Assert.Contains(providers, x => x is LeverExternalJobProvider);
        Assert.DoesNotContain(providers, x => x.AtsType == AtsType.Custom);

        var clients = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>();
        using var greenhouse = clients.CreateClient(GreenhouseExternalJobProvider.HttpClientName);
        using var lever = clients.CreateClient(LeverExternalJobProvider.HttpClientName);
        Assert.Equal(new Uri("https://boards-api.greenhouse.io/"), greenhouse.BaseAddress);
        Assert.Equal(new Uri("https://api.lever.co/"), lever.BaseAddress);
    }

    private static void AssertScoped<TService, TImplementation>(IServiceCollection services)
    {
        var registration = Assert.Single(services, x => x.ServiceType == typeof(TService));
        Assert.Equal(typeof(TImplementation), registration.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, registration.Lifetime);
    }
}
