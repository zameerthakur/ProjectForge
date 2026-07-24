namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents the durable outcome of one provider execution attempt.
/// </summary>
public sealed record WorkflowExecutionEvidence
{
    /// <summary>
    /// Gets the identifier of the provider execution request.
    /// </summary>
    public required Guid RequestId { get; init; }

    /// <summary>
    /// Gets the name of the provider that handled the request.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the normalized execution outcome.
    /// </summary>
    public required WorkflowExecutionOutcome Outcome { get; init; }

    /// <summary>
    /// Gets an optional summary of the work performed.
    /// </summary>
    public string? Summary { get; init; }

    /// <summary>
    /// Gets the detailed provider output.
    /// </summary>
    public string? Output { get; init; }

    /// <summary>
    /// Gets the failure description for an unsuccessful outcome.
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// Gets the UTC time at which provider execution started.
    /// </summary>
    public required DateTimeOffset StartedAtUtc { get; init; }

    /// <summary>
    /// Gets the UTC time at which provider execution completed.
    /// </summary>
    public required DateTimeOffset CompletedAtUtc { get; init; }

    /// <summary>
    /// Gets the estimated execution cost.
    /// </summary>
    public required decimal EstimatedCost { get; init; }

    /// <summary>
    /// Gets provider-specific metadata safe for durable operator inspection.
    /// </summary>
    public IReadOnlyDictionary<string, string> Metadata { get; init; }
        = new Dictionary<string, string>();
}

/// <summary>
/// Identifies the normalized result of a provider execution attempt.
/// </summary>
public enum WorkflowExecutionOutcome
{
    /// <summary>
    /// The provider completed the request successfully.
    /// </summary>
    Succeeded = 1,

    /// <summary>
    /// The provider returned an unsuccessful result.
    /// </summary>
    Failed = 2,

    /// <summary>
    /// Execution was canceled before a result was produced.
    /// </summary>
    Canceled = 3,

    /// <summary>
    /// Execution exceeded its configured time limit.
    /// </summary>
    TimedOut = 4,

    /// <summary>
    /// The provider returned a result that failed correlation or validation.
    /// </summary>
    InvalidResult = 5,

    /// <summary>
    /// The provider threw before returning a result.
    /// </summary>
    ProviderError = 6,

    /// <summary>
    /// Provider execution succeeded but its durable artifacts could not be
    /// published.
    /// </summary>
    ArtifactError = 7
}
