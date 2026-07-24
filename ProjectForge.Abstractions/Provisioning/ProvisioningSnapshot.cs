namespace ProjectForge.Abstractions.Provisioning;

/// <summary>
/// Captures the latest immutable provisioning state for one requirement.
/// </summary>
public sealed record ProvisioningSnapshot
{
    public required ProvisioningRequirement Requirement { get; init; }

    public required ProvisioningStatus Status { get; init; }

    public double? Percentage { get; init; }

    public string? Message { get; init; }
}
