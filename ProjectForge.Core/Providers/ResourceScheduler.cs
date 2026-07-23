using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Core.Providers;

/// <summary>
/// Selects the first healthy capability provider that can execute
/// the requested engineering capability.
/// </summary>
/// <remarks>
/// This implementation intentionally uses a simple first-match strategy
/// for the technical spike. Provider scoring, cost comparison, workload
/// balancing, and advanced policy evaluation can be added after the
/// end-to-end orchestration flow has been validated.
/// </remarks>
public sealed class ResourceScheduler : IResourceScheduler
{
    private readonly IProviderRegistry _providerRegistry;

    /// <summary>
    /// Initializes a new instance of the <see cref="ResourceScheduler"/> class.
    /// </summary>
    /// <param name="providerRegistry">
    /// The registry containing the available capability providers.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="providerRegistry"/> is
    /// <see langword="null"/>.
    /// </exception>
    public ResourceScheduler(IProviderRegistry providerRegistry)
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);

        _providerRegistry = providerRegistry;
    }

    /// <inheritdoc />
    public async Task<ICapabilityProvider> SelectProviderAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        foreach (var provider in _providerRegistry.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!provider.SupportedCapabilities.Contains(
                    request.Requirement.Capability))
            {
                continue;
            }

            var healthReport = await provider.CheckHealthAsync(
                cancellationToken);

            if (!healthReport.IsHealthy)
            {
                continue;
            }

            var canExecute = await provider.CanExecuteAsync(
                request,
                cancellationToken);

            if (canExecute)
            {
                return provider;
            }
        }

        throw new InvalidOperationException(
            $"No healthy provider is available for capability " +
            $"'{request.Requirement.Capability}'.");
    }
}
