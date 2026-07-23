namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents an immutable, human-readable workflow audit entry.
/// </summary>
public sealed class WorkflowAuditEvent
{
    /// <summary>
    /// Gets the audit event identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the workflow to which the event belongs.
    /// </summary>
    public required Guid WorkflowId { get; init; }

    /// <summary>
    /// Gets the stable machine-readable event type.
    /// </summary>
    public required string EventType { get; init; }

    /// <summary>
    /// Gets the human-readable event description.
    /// </summary>
    public required string Message { get; init; }

    /// <summary>
    /// Gets the UTC time at which the event occurred.
    /// </summary>
    public required DateTimeOffset OccurredAtUtc { get; init; }
}
