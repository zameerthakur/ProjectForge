using Microsoft.Extensions.DependencyInjection;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Application.Artifacts;
using ProjectForge.Application.Workflows;
using ProjectForge.Core.Providers;
using ProjectForge.Host.Composition;
using ProjectForge.Host.Configuration;
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

    [Fact]
    public void RegistersOllamaAsTheOnlyProviderWhenExplicitlySelected()
    {
        var services = new ServiceCollection();
        var configuration = CreateOllamaConfiguration();

        services.AddProjectForgeExecution(
            "artifacts",
            TimeSpan.FromSeconds(30),
            configuration);

        using var provider = services.BuildServiceProvider();

        var capabilityProvider =
            Assert.Single(provider.GetServices<ICapabilityProvider>());
        var ollamaProvider =
            Assert.IsType<OllamaCapabilityProvider>(capabilityProvider);

        Assert.Same(
            ollamaProvider,
            provider.GetRequiredService<OllamaCapabilityProvider>());
        Assert.Empty(provider.GetServices<LocalMockCapabilityProvider>());
        Assert.Equal(
            configuration.Ollama.Endpoint,
            provider.GetRequiredService<HttpClient>().BaseAddress);
        Assert.Equal(
            TimeSpan.FromSeconds(45),
            provider.GetRequiredService<HttpClient>().Timeout);
    }

    [Theory]
    [InlineData("https://127.0.0.1:11434")]
    [InlineData("http://example.test:11434")]
    [InlineData("http://user:password@127.0.0.1:11434")]
    public void RejectsAnUnsafeOllamaEndpoint(string endpoint)
    {
        var services = new ServiceCollection();
        var configuration = CreateOllamaConfiguration();
        configuration.Ollama.Endpoint = new Uri(endpoint);

        Assert.Throws<InvalidOperationException>(
            () => services.AddProjectForgeExecution(
                "artifacts",
                providerConfiguration: configuration));
    }

    [Theory]
    [InlineData("", "sha256:abc", "0.10", "0.11", 30)]
    [InlineData("model", "", "0.10", "0.11", 30)]
    [InlineData("model", "sha256:abc", "0.11", "0.11", 30)]
    [InlineData("model", "sha256:abc", "0.10", "0.11", 0)]
    [InlineData("model", "sha256:abc", "0.10", "0.11", 1_801)]
    public void RejectsIncompleteOrUnboundedOllamaConfiguration(
        string model,
        string digest,
        string minimumVersion,
        string maximumVersion,
        int timeoutSeconds)
    {
        var services = new ServiceCollection();
        var configuration = CreateOllamaConfiguration();
        configuration.Ollama.Model = model;
        configuration.Ollama.ExpectedDigest = digest;
        configuration.Ollama.MinimumRuntimeVersion =
            Version.Parse(minimumVersion);
        configuration.Ollama.MaximumRuntimeVersionExclusive =
            Version.Parse(maximumVersion);
        configuration.Ollama.RequestTimeoutSeconds = timeoutSeconds;

        Assert.Throws<InvalidOperationException>(
            () => services.AddProjectForgeExecution(
                "artifacts",
                providerConfiguration: configuration));
    }

    private static ProjectForgeProviderConfiguration
        CreateOllamaConfiguration() =>
        new()
        {
            Mode = ProjectForgeProviderMode.Ollama,
            Ollama = new OllamaHostConfiguration
            {
                Endpoint = new Uri("http://127.0.0.1:11434"),
                Model = "gemma3:4b",
                ExpectedDigest = $"sha256:{new string('a', 64)}",
                MinimumRuntimeVersion = new Version(0, 10),
                MaximumRuntimeVersionExclusive = new Version(0, 11),
                RequestTimeoutSeconds = 45
            }
        };
}
