using Microsoft.Extensions.DependencyInjection.Extensions;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Application.Artifacts;
using ProjectForge.Application.Workflows;
using ProjectForge.Core.Providers;
using ProjectForge.Host.Configuration;
using ProjectForge.Infrastructure.Artifacts;
using ProjectForge.Infrastructure.Providers;

namespace ProjectForge.Host.Composition;

/// <summary>
/// Registers the provider-selection and workflow-execution services used by
/// the ProjectForge host.
/// </summary>
public static class ServiceCollectionExtensions
{
    private static readonly TimeSpan DefaultExecutionTimeout =
        TimeSpan.FromMinutes(5);

    private static readonly TimeSpan MaximumExecutionTimeout =
        TimeSpan.FromMinutes(30);

    /// <summary>
    /// Adds the deterministic local provider and the services required to
    /// select it and execute approved workflows.
    /// </summary>
    /// <param name="services">
    /// The host service collection.
    /// </param>
    /// <param name="artifactRoot">
    /// The application-owned directory in which successful execution
    /// artifacts are written.
    /// </param>
    /// <param name="executionTimeout">
    /// The maximum duration of one provider execution. When omitted, the
    /// default is five minutes.
    /// </param>
    /// <returns>The service collection for further composition.</returns>
    public static IServiceCollection AddProjectForgeExecution(
        this IServiceCollection services,
        string artifactRoot,
        TimeSpan? executionTimeout = null,
        ProjectForgeProviderConfiguration? providerConfiguration = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);

        var timeout = executionTimeout ?? DefaultExecutionTimeout;
        if (timeout <= TimeSpan.Zero ||
            timeout > MaximumExecutionTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executionTimeout),
                timeout,
                $"The execution timeout must be between zero and " +
                $"{MaximumExecutionTimeout.TotalMinutes:g} minutes.");
        }

        var artifactWriter = new FileSystemExecutionArtifactWriter(
            artifactRoot);
        providerConfiguration ??= new ProjectForgeProviderConfiguration();
        providerConfiguration.Validate();

        services.TryAddSingleton(TimeProvider.System);
        RegisterCapabilityProvider(services, providerConfiguration);

        services.AddSingleton<ProviderRegistry>();
        services.AddSingleton<IProviderRegistry>(
            provider => provider.GetRequiredService<ProviderRegistry>());

        services.AddSingleton<ProviderHealthChecker>();
        services.AddSingleton<IProviderHealthChecker>(
            provider => provider.GetRequiredService<ProviderHealthChecker>());

        services.AddSingleton<PolicyResourceScheduler>();
        services.AddSingleton<IExplainableResourceScheduler>(
            provider => provider.GetRequiredService<PolicyResourceScheduler>());
        services.AddSingleton<IResourceScheduler>(
            provider => provider.GetRequiredService<PolicyResourceScheduler>());

        services.AddSingleton(artifactWriter);
        services.AddSingleton<IExecutionArtifactWriter>(
            provider => provider.GetRequiredService<
                FileSystemExecutionArtifactWriter>());

        services.AddSingleton<WorkflowExecutionService>(
            provider => new WorkflowExecutionService(
                provider.GetRequiredService<IWorkflowStore>(),
                provider.GetRequiredService<IExplainableResourceScheduler>(),
                provider.GetRequiredService<IExecutionArtifactWriter>(),
                provider.GetRequiredService<TimeProvider>(),
                timeout));
        services.AddSingleton<IWorkflowExecutionService>(
            provider => provider.GetRequiredService<
                WorkflowExecutionService>());

        return services;
    }

    private static void RegisterCapabilityProvider(
        IServiceCollection services,
        ProjectForgeProviderConfiguration configuration)
    {
        if (configuration.Mode == ProjectForgeProviderMode.LocalMock)
        {
            services.AddSingleton<LocalMockCapabilityProvider>();
            services.AddSingleton<ICapabilityProvider>(
                provider => provider.GetRequiredService<
                    LocalMockCapabilityProvider>());
            return;
        }

        var options = configuration.CreateOllamaOptions();
        services.AddSingleton(options);
        services.AddSingleton(
            new HttpClient
            {
                BaseAddress = options.Endpoint,
                Timeout = configuration.Ollama.RequestTimeout
            });
        services.AddSingleton<OllamaCapabilityProvider>();
        services.AddSingleton<ICapabilityProvider>(
            provider => provider.GetRequiredService<
                OllamaCapabilityProvider>());
    }
}
