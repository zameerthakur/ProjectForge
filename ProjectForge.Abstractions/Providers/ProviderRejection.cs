namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Describes one reason a provider was rejected during selection.
/// </summary>
public sealed class ProviderRejection
{
    /// <summary>
    /// Gets the machine-readable rejection code.
    /// </summary>
    public required ProviderRejectionCode Code { get; init; }

    /// <summary>
    /// Gets the human-readable explanation.
    /// </summary>
    public required string Message { get; init; }
}
