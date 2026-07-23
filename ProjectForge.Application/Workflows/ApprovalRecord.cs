namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents the human approval associated with a workflow.
/// </summary>
public sealed class ApprovalRecord
{
    /// <summary>
    /// Gets the approval identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the workflow awaiting the decision.
    /// </summary>
    public required Guid WorkflowId { get; init; }

    /// <summary>
    /// Gets the prompt presented to the approver.
    /// </summary>
    public required string Prompt { get; init; }

    /// <summary>
    /// Gets the decision, or <see langword="null"/> while it is pending.
    /// </summary>
    public ApprovalDecision? Decision { get; init; }

    /// <summary>
    /// Gets the optional identity of the approver.
    /// </summary>
    public string? DecidedBy { get; init; }

    /// <summary>
    /// Gets the UTC time at which the approval was requested.
    /// </summary>
    public required DateTimeOffset RequestedAtUtc { get; init; }

    /// <summary>
    /// Gets the UTC time at which the decision was recorded.
    /// </summary>
    public DateTimeOffset? DecidedAtUtc { get; init; }
}
