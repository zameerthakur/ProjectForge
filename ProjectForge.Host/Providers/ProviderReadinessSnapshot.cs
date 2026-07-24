using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Host.Providers;

/// <summary>
/// Creates a point-in-time, operator-safe view of registered providers.
/// </summary>
public static class ProviderReadinessSnapshot
{
    /// <summary>
    /// Checks all registered providers and returns their readiness in stable
    /// provider-name order.
    /// </summary>
    public static async Task<IReadOnlyCollection<ProviderReadinessResponse>>
        CreateAsync(
            IProviderRegistry registry,
            IProviderHealthChecker healthChecker,
            CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(healthChecker);
        cancellationToken.ThrowIfCancellationRequested();

        var providers = registry.Providers
            .OrderBy(provider => provider.Name, StringComparer.Ordinal)
            .ToArray();
        var checks = providers.Select(
            provider => CreateResponseAsync(
                provider,
                healthChecker,
                cancellationToken));

        return await Task.WhenAll(checks);
    }

    private static async Task<ProviderReadinessResponse> CreateResponseAsync(
        ICapabilityProvider provider,
        IProviderHealthChecker healthChecker,
        CancellationToken cancellationToken)
    {
        var health = await healthChecker.CheckHealthAsync(
            provider,
            cancellationToken);
        var descriptor = provider is ISchedulableCapabilityProvider schedulable
            ? new ProviderDescriptorResponse(
                schedulable.Descriptor.ExecutionLocation,
                schedulable.Descriptor.SupportsRepositoryAccess,
                schedulable.Descriptor.SupportsFileWriteAccess,
                schedulable.Descriptor.SupportsToolExecution)
            : null;

        return new ProviderReadinessResponse(
            provider.Name,
            provider.SupportedCapabilities
                .OrderBy(capability => capability)
                .ToArray(),
            descriptor,
            health.IsHealthy,
            health.StatusMessage,
            health.CheckedAtUtc,
            health.ResponseTime.TotalMilliseconds);
    }
}
