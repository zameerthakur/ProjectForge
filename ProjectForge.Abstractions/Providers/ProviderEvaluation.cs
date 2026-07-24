namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Records how one provider was evaluated for a capability request.
/// </summary>
public sealed class ProviderEvaluation
{
    /// <summary>
    /// Gets the evaluated provider.
    /// </summary>
    public required ICapabilityProvider Provider { get; init; }

    /// <summary>
    /// Gets the provider's estimated execution cost when estimation was needed.
    /// </summary>
    public decimal? EstimatedCost { get; init; }

    /// <summary>
    /// Gets the reasons the provider was rejected.
    /// </summary>
    public IReadOnlyCollection<ProviderRejection> Rejections { get; init; }
        = Array.Empty<ProviderRejection>();

    /// <summary>
    /// Gets whether the provider satisfied every mandatory constraint.
    /// </summary>
    public bool IsEligible => Rejections.Count == 0;
}
