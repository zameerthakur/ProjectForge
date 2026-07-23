using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Core.Providers;

/// <summary>
/// Provides one policy-facing boundary for capability-provider health checks.
/// </summary>
public sealed class ProviderHealthChecker : IProviderHealthChecker
{
    /// <inheritdoc />
    public Task<ProviderHealthReport> CheckHealthAsync(
        ICapabilityProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);

        return provider.CheckHealthAsync(cancellationToken);
    }
}
