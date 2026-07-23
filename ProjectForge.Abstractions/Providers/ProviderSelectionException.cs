namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Represents a provider-selection failure with candidate evidence.
/// </summary>
public sealed class ProviderSelectionException : InvalidOperationException
{
    /// <summary>
    /// Initializes a new provider-selection failure.
    /// </summary>
    public ProviderSelectionException(
        string message,
        IReadOnlyCollection<ProviderEvaluation> evaluations)
        : base(message)
    {
        ArgumentNullException.ThrowIfNull(evaluations);

        Evaluations = evaluations;
    }

    /// <summary>
    /// Gets the evaluations that led to the failure.
    /// </summary>
    public IReadOnlyCollection<ProviderEvaluation> Evaluations { get; }
}
