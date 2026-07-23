using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Selects a provider and returns the evidence used to make the decision.
/// </summary>
public interface IExplainableResourceScheduler
{
    /// <summary>
    /// Evaluates registered providers and selects the best eligible candidate.
    /// </summary>
    /// <param name="request">
    /// The capability execution request that requires a provider.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel provider selection.
    /// </param>
    /// <returns>
    /// The selected provider and the complete candidate evaluation.
    /// </returns>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no provider satisfies every mandatory constraint.
    /// </exception>
    Task<ProviderSelectionResult> SelectProviderWithEvidenceAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default);
}
