using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents durable, provider-independent evidence for a scheduling decision.
/// </summary>
public sealed record WorkflowProviderSelectionEvidence
{
    /// <summary>
    /// Gets the name of the provider selected for execution.
    /// </summary>
    public required string SelectedProviderName { get; init; }

    /// <summary>
    /// Gets the selected provider's estimated execution cost.
    /// </summary>
    public required decimal EstimatedCost { get; init; }

    /// <summary>
    /// Gets the candidate evaluations considered by the scheduler.
    /// </summary>
    public IReadOnlyCollection<WorkflowProviderCandidateEvidence> Candidates
    {
        get;
        init;
    } = Array.Empty<WorkflowProviderCandidateEvidence>();
}

/// <summary>
/// Represents the durable evaluation of one provider candidate.
/// </summary>
public sealed record WorkflowProviderCandidateEvidence
{
    /// <summary>
    /// Gets the provider name.
    /// </summary>
    public required string ProviderName { get; init; }

    /// <summary>
    /// Gets the estimated execution cost when one was obtained.
    /// </summary>
    public decimal? EstimatedCost { get; init; }

    /// <summary>
    /// Gets the reasons the provider was rejected.
    /// </summary>
    public IReadOnlyCollection<WorkflowProviderRejectionEvidence> Rejections
    {
        get;
        init;
    } = Array.Empty<WorkflowProviderRejectionEvidence>();

    /// <summary>
    /// Gets whether the provider satisfied every mandatory constraint.
    /// </summary>
    public bool IsEligible => Rejections.Count == 0;
}

/// <summary>
/// Represents one durable provider-rejection reason.
/// </summary>
public sealed record WorkflowProviderRejectionEvidence
{
    /// <summary>
    /// Gets the machine-readable rejection code.
    /// </summary>
    public required ProviderRejectionCode Code { get; init; }

    /// <summary>
    /// Gets the human-readable rejection explanation.
    /// </summary>
    public required string Message { get; init; }
}
