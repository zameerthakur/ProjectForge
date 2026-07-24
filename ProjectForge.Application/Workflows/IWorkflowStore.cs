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

    /// <summary>
    /// Claims a queued workflow for provider provisioning and atomically
    /// records provider selection and the in-progress attempt.
    /// </summary>
    Task<WorkflowMutationResult> TryBeginProvisioningAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowProviderSelectionEvidence providerSelection,
        WorkflowProvisioningEvidence provisioning,
        CancellationToken cancellationToken = default) =>
        Task.FromException<WorkflowMutationResult>(
            new NotSupportedException(
                "This workflow store does not support durable provisioning."));

    /// <summary>
    /// Completes the current provisioning attempt. Successful attempts return
    /// to the queue so execution can be claimed; failed attempts terminate the
    /// workflow.
    /// </summary>
    Task<WorkflowMutationResult> TryCompleteProvisioningAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowProvisioningEvidence provisioning,
        CancellationToken cancellationToken = default) =>
        Task.FromException<WorkflowMutationResult>(
            new NotSupportedException(
                "This workflow store does not support durable provisioning."));

    /// <summary>
    /// Claims a queued workflow for execution and atomically records the
    /// provider-selection evidence.
    /// </summary>
    Task<WorkflowMutationResult> TryStartExecutionAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowProviderSelectionEvidence providerSelection,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Completes a running workflow and atomically records its normalized
    /// execution outcome, artifact paths, and audit event.
    /// </summary>
    Task<WorkflowMutationResult> TryCompleteExecutionAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowStatus terminalStatus,
        WorkflowExecutionEvidence execution,
        WorkflowArtifactPaths? artifacts,
        string auditEventType,
        string auditMessage,
        DateTimeOffset occurredAtUtc,
        string? failureMessage = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Moves executions left running by a prior process lifetime into an
    /// explicit reconciliation state without replaying provider work.
    /// </summary>
    /// <returns>The number of workflows marked for reconciliation.</returns>
    Task<int> ReconcileInterruptedExecutionsAsync(
        DateTimeOffset detectedAtUtc,
        string reason,
        CancellationToken cancellationToken = default);
}
