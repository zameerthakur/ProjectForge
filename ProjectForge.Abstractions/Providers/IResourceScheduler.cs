using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Selects the most appropriate capability provider for an execution request.
/// </summary>
/// <remarks>
/// The resource scheduler is responsible only for provider selection.
/// It does not execute the request, manage workflow state, or perform retries.
/// </remarks>
public interface IResourceScheduler
{
    /// <summary>
    /// Selects the most appropriate provider for the specified request.
    /// </summary>
    /// <param name="request">
    /// The capability execution request that requires a provider.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel provider selection.
    /// </param>
    /// <returns>
    /// The selected capability provider.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no suitable provider is available.
    /// </exception>
    Task<ICapabilityProvider> SelectProviderAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default);
}
