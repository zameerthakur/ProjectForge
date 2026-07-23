using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Application.Workflows;

/// <summary>
/// Coordinates the application-owned approval lifecycle for workflows.
/// </summary>
public interface IWorkflowCoordinator
{
    /// <summary>
    /// Creates and durably pauses a workflow for human approval.
    /// </summary>
    Task<WorkflowSnapshot> CreatePendingApprovalAsync(
        CapabilityExecutionRequest request,
        string approvalPrompt,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Records an approval or rejection using optimistic concurrency.
    /// </summary>
    Task<WorkflowMutationResult> RecordDecisionAsync(
        Guid workflowId,
        long expectedVersion,
        ApprovalDecision decision,
        string? decidedBy,
        CancellationToken cancellationToken = default);
}
