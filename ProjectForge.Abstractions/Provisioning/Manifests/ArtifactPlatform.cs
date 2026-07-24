namespace ProjectForge.Abstractions.Provisioning.Manifests;

/// <summary>
/// Identifies one exact operating-system and processor-architecture target.
/// </summary>
public sealed record ArtifactPlatform
{
    public required string OperatingSystem { get; init; }

    public required string Architecture { get; init; }
}
