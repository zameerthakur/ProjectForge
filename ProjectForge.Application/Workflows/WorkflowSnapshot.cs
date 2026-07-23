namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents a workflow and its related durable operator state.
/// </summary>
public sealed class WorkflowSnapshot
{
    /// <summary>
    /// Gets the workflow record.
    /// </summary>
    public required WorkflowRecord Workflow { get; init; }

    /// <summary>
    /// Gets the workflow approval, when one was requested.
    /// </summary>
    public ApprovalRecord? Approval { get; init; }

    /// <summary>
    /// Gets the ordered workflow audit history.
    /// </summary>
    public IReadOnlyCollection<WorkflowAuditEvent> AuditEvents { get; init; }
        = Array.Empty<WorkflowAuditEvent>();
}
