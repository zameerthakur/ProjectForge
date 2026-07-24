namespace ProjectForge.Application.Workflows;

/// <summary>
/// Identifies one pinned dependency considered by a provisioning attempt.
/// </summary>
public sealed record WorkflowProvisioningRequirementEvidence
{
    public required string RequirementId { get; init; }

    public required string Version { get; init; }

    public required string Category { get; init; }
}
