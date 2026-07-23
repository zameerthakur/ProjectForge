namespace ProjectForge.Application.Workflows;

/// <summary>
/// Records why an interrupted workflow requires operator reconciliation.
/// </summary>
public sealed record WorkflowRecoveryMetadata
{
    /// <summary>
    /// Gets the state observed when interruption was detected.
    /// </summary>
    public required WorkflowStatus InterruptedStatus { get; init; }

    /// <summary>
    /// Gets when the interrupted state was last durably updated.
    /// </summary>
    public required DateTimeOffset InterruptedAtUtc { get; init; }

    /// <summary>
    /// Gets when recovery processing detected the interrupted state.
    /// </summary>
    public required DateTimeOffset DetectedAtUtc { get; init; }

    /// <summary>
    /// Gets the reason automatic replay was withheld.
    /// </summary>
    public required string Reason { get; init; }
}
