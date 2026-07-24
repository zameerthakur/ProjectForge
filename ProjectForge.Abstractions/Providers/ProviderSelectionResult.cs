namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Represents an explainable provider-selection decision.
/// </summary>
public sealed class ProviderSelectionResult
{
    /// <summary>
    /// Gets the provider selected for execution.
    /// </summary>
    public required ISchedulableCapabilityProvider SelectedProvider { get; init; }

    /// <summary>
    /// Gets the selected provider's estimated execution cost.
    /// </summary>
    public required decimal EstimatedCost { get; init; }

    /// <summary>
    /// Gets the evaluations considered when making the decision.
    /// </summary>
    public IReadOnlyCollection<ProviderEvaluation> Evaluations { get; init; }
        = Array.Empty<ProviderEvaluation>();
}
