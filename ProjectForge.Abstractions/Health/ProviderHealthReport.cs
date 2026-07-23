namespace ProjectForge.Abstractions.Health;

/// <summary>
/// Represents the result of an operational health check performed
/// against a capability provider.
/// </summary>
public sealed class ProviderHealthReport
{
    /// <summary>
    /// Gets the name of the provider that was checked.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets whether the provider is currently healthy and available
    /// for capability execution.
    /// </summary>
    public bool IsHealthy { get; init; }

    /// <summary>
    /// Gets an optional human-readable description of the provider's
    /// current health status.
    /// </summary>
    public string? StatusMessage { get; init; }

    /// <summary>
    /// Gets the UTC date and time when the health check completed.
    /// </summary>
    public DateTimeOffset CheckedAtUtc { get; init; }
        = DateTimeOffset.UtcNow;

    /// <summary>
    /// Gets the time taken to complete the health check.
    /// </summary>
    public TimeSpan ResponseTime { get; init; }

    /// <summary>
    /// Gets optional provider-specific health information.
    /// </summary>
    /// <remarks>
    /// Examples include service version, endpoint status, installed model,
    /// sandbox availability, rate-limit state, or authentication status.
    /// </remarks>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
