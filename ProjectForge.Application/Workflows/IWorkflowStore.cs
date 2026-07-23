namespace ProjectForge.Application.Workflows;

/// <summary>
/// Persists ProjectForge-owned workflow, approval, and audit state.
/// </summary>
/// <remarks>
/// Each mutating operation must persist the state change and its audit event in
/// one transaction. The expected version makes repeated or concurrent approval
/// and execution requests safe to reject without duplicating work.
/// </remarks>
public interface IWorkflowStore
{
    /// <summary>
    /// Creates a workflow with its initial approval and audit state.
    /// </summary>
    Task CreateAsync(
        WorkflowSnapshot workflow,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets a workflow and its related operator state.
    /// </summary>
    Task<WorkflowSnapshot?> GetAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Lists workflows ordered from most recently updated.
    /// </summary>
    Task<IReadOnlyCollection<WorkflowSnapshot>> ListAsync(
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records a human decision when the workflow version and pending state
    /// still match.
    /// </summary>
    Task<WorkflowMutationResult> TryRecordDecisionAsync(
        Guid workflowId,
        long expectedVersion,
        ApprovalDecision decision,
        string? decidedBy,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves a workflow between execution states when the expected version and
    /// current status still match.
    /// </summary>
    Task<WorkflowMutationResult> TryTransitionAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowStatus expectedStatus,
        WorkflowStatus nextStatus,
        string auditEventType,
        string auditMessage,
        DateTimeOffset occurredAtUtc,
        string? failureMessage = null,
        CancellationToken cancellationToken = default);
}
