using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Defines a capability provider that exposes the information required for
/// policy-based scheduling.
/// </summary>
/// <remarks>
/// Keeping scheduling metadata in an additive interface allows simple or legacy
/// providers to retain the base execution contract. Policy-based schedulers can
/// explicitly reject providers that do not expose enough decision evidence.
/// </remarks>
public interface ISchedulableCapabilityProvider : ICapabilityProvider
{
    /// <summary>
    /// Gets the provider characteristics used for eligibility and ranking.
    /// </summary>
    ProviderDescriptor Descriptor { get; }

    /// <summary>
    /// Estimates the cost of executing the specified request.
    /// </summary>
    /// <param name="request">
    /// The capability execution request to estimate.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel cost estimation.
    /// </param>
    /// <returns>
    /// The estimated execution cost in the provider's configured billing
    /// currency. Local providers will normally return zero.
    /// </returns>
    Task<decimal> EstimateCostAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default);
}
