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

    /// <summary>
    /// Gets durable evidence for the provider-selection decision.
    /// </summary>
    public WorkflowProviderSelectionEvidence? ProviderSelection { get; init; }

    /// <summary>
    /// Gets evidence for the latest provider provisioning attempt.
    /// </summary>
    public WorkflowProvisioningEvidence? Provisioning { get; init; }

    /// <summary>
    /// Gets the normalized provider execution outcome.
    /// </summary>
    public WorkflowExecutionEvidence? Execution { get; init; }

    /// <summary>
    /// Gets the paths of artifacts published for successful execution.
    /// </summary>
    public WorkflowArtifactPaths? Artifacts { get; init; }

    /// <summary>
    /// Gets metadata describing an execution interrupted across process
    /// lifetime boundaries.
    /// </summary>
    public WorkflowRecoveryMetadata? Recovery { get; init; }
}
