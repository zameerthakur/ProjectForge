using ProjectForge.Abstractions.Health;

namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Determines the current operational health of capability providers.
/// </summary>
/// <remarks>
/// The health checker verifies provider availability and returns a
/// standardized health report. It does not select providers, execute
/// capability requests, or manage workflow state.
/// </remarks>
public interface IProviderHealthChecker
{
    /// <summary>
    /// Checks the current operational health of the specified provider.
    /// </summary>
    /// <param name="provider">
    /// The capability provider to check.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the health check.
    /// </param>
    /// <returns>
    /// A health report describing the provider's current status.
    /// </returns>
    Task<ProviderHealthReport> CheckHealthAsync(
        ICapabilityProvider provider,
        CancellationToken cancellationToken = default);
}
