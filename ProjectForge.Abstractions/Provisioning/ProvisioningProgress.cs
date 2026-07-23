namespace ProjectForge.Abstractions.Provisioning;

public sealed record ProvisioningProgress
{
    public required ProvisioningRequirement Requirement { get; init; }

    public required ProvisioningStatus Status { get; init; }

    public double? Percentage { get; init; }

    public string? Message { get; init; }
}
