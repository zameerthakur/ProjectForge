namespace ProjectForge.Application.Workflows;

/// <summary>
/// Identifies the durable lifecycle state of a ProjectForge workflow.
/// </summary>
public enum WorkflowStatus
{
    /// <summary>
    /// The workflow is waiting for a human decision.
    /// </summary>
    PendingApproval = 1,

    /// <summary>
    /// Approval was granted and execution may be queued.
    /// </summary>
    Approved = 2,

    /// <summary>
    /// The workflow is queued for provider execution.
    /// </summary>
    Queued = 3,

    /// <summary>
    /// A provider is executing the workflow capability.
    /// </summary>
    Running = 4,

    /// <summary>
    /// Provider execution completed successfully.
    /// </summary>
    Succeeded = 5,

    /// <summary>
    /// Provider execution failed.
    /// </summary>
    Failed = 6,

    /// <summary>
    /// A human rejected the workflow.
    /// </summary>
    Rejected = 7,

    /// <summary>
    /// Execution was interrupted and requires operator reconciliation before
    /// any further provider work can be attempted.
    /// </summary>
    ReconciliationRequired = 8
}
