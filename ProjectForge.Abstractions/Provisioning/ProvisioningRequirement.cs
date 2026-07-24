namespace ProjectForge.Abstractions.Provisioning;

/// <summary>
/// Describes one versioned dependency required by a provider.
/// </summary>
public sealed record ProvisioningRequirement
{
    public required string Id { get; init; }

    public required string DisplayName { get; init; }

    public required string Version { get; init; }

    public required ProvisioningArtifactKind Kind { get; init; }
}
