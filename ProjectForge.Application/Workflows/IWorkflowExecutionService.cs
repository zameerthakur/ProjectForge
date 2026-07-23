namespace ProjectForge.Application.Workflows;

/// <summary>
/// Resumes approved workflows and coordinates provider execution.
/// </summary>
public interface IWorkflowExecutionService
{
    /// <summary>
    /// Attempts to resume a workflow at the expected durable version.
    /// </summary>
    /// <remarks>
    /// Only the caller that atomically moves a queued workflow to running
    /// executes the selected provider. Stale, rejected, running, and terminal
    /// workflows are returned without provider execution. A persisted running
    /// workflow is deliberately not retried because this contract has no
    /// provider idempotency key or durable execution lease with which to prove
    /// that repeating the external side effect is safe.
    /// </remarks>
    Task<WorkflowExecutionResult> ResumeAsync(
        Guid workflowId,
        long expectedVersion,
        CancellationToken cancellationToken = default);
}
