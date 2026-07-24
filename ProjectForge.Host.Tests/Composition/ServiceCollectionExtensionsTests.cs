using Microsoft.Extensions.DependencyInjection;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Application.Artifacts;
using ProjectForge.Application.Workflows;
using ProjectForge.Core.Providers;
using ProjectForge.Host.Composition;
using ProjectForge.Infrastructure.Artifacts;
using ProjectForge.Infrastructure.Providers;

namespace ProjectForge.Host.Tests.Composition;

public sealed class ServiceCollectionExtensionsTests
{
    [Fact]
    public void RegistersOneCoherentExecutionServiceGraph()
    {
        var services = new ServiceCollection();
        services.AddProjectForgeExecution(
            Path.Combine("test-data", "artifacts"),
            TimeSpan.FromSeconds(30));

        using var provider = services.BuildServiceProvider();

        var capabilityProvider =
            Assert.Single(provider.GetServices<ICapabilityProvider>());
        var registry = provider.GetRequiredService<IProviderRegistry>();
        var scheduler =
            provider.GetRequiredService<PolicyResourceScheduler>();

        Assert.IsType<LocalMockCapabilityProvider>(capabilityProvider);
        Assert.Same(capabilityProvider, Assert.Single(registry.Providers));
        Assert.Same(
            scheduler,
            provider.GetRequiredService<IExplainableResourceScheduler>());
        Assert.Same(
            scheduler,
            provider.GetRequiredService<IResourceScheduler>());
        Assert.IsType<ProviderHealthChecker>(
            provider.GetRequiredService<IProviderHealthChecker>());
        Assert.IsType<FileSystemExecutionArtifactWriter>(
            provider.GetRequiredService<IExecutionArtifactWriter>());

        var executionRegistration = Assert.Single(
            services,
            service =>
                service.ServiceType ==
                typeof(IWorkflowExecutionService));
        Assert.Equal(ServiceLifetime.Singleton, executionRegistration.Lifetime);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_801)]
    public void RejectsAnExecutionTimeoutOutsideTheBoundedRange(
        int timeoutSeconds)
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentOutOfRangeException>(
            () => services.AddProjectForgeExecution(
                "artifacts",
                TimeSpan.FromSeconds(timeoutSeconds)));
    }

    [Fact]
    public void RejectsAnEmptyArtifactRoot()
    {
        var services = new ServiceCollection();

        Assert.Throws<ArgumentException>(
            () => services.AddProjectForgeExecution(" "));
    }
}
