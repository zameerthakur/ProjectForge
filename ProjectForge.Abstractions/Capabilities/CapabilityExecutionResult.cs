namespace ProjectForge.Abstractions.Capabilities;

/// <summary>
/// Represents the outcome of executing an engineering capability.
/// </summary>
public sealed class CapabilityExecutionResult
{
    /// <summary>
    /// Gets the identifier of the execution request.
    /// </summary>
    public required Guid RequestId { get; init; }

    /// <summary>
    /// Gets the name of the provider that executed the request.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets whether execution completed successfully.
    /// </summary>
    public bool IsSuccessful { get; init; }

    /// <summary>
    /// Gets an optional summary of the work performed.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Gets the detailed provider output.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Gets the error message when execution fails.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the UTC date and time when execution started.
    /// </summary>
    public DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>
    /// Gets the UTC date and time when execution completed.
    /// </summary>
    public DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>
    /// Gets the execution duration.
    /// </summary>
    public TimeSpan Duration => CompletedAtUtc - StartedAtUtc;

    /// <summary>
    /// Gets the estimated execution cost.
    /// </summary>
    /// <remarks>
    /// Local providers will normally return zero.
    /// </remarks>
    public decimal EstimatedCost { get; init; }

    /// <summary>
    /// Gets optional provider-specific metadata.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}
