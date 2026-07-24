namespace ProjectForge.Application.Workflows;

/// <summary>
/// Captures durable, restart-safe evidence for one provider provisioning
/// attempt.
/// </summary>
public sealed record WorkflowProvisioningEvidence
{
    public required string ProviderId { get; init; }

    public required string ProviderVersion { get; init; }

    public required int Attempt { get; init; }

    public required WorkflowProvisioningStatus Status { get; init; }

    public IReadOnlyCollection<WorkflowProvisioningRequirementEvidence>
        Requirements { get; init; }
        = Array.Empty<WorkflowProvisioningRequirementEvidence>();

    public required DateTimeOffset StartedAtUtc { get; init; }

    public DateTimeOffset? CompletedAtUtc { get; init; }

    public string? FailureCategory { get; init; }

    public string? FailureMessage { get; init; }
}
