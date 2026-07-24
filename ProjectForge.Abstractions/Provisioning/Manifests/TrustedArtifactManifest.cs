namespace ProjectForge.Abstractions.Provisioning.Manifests;

/// <summary>
/// Provides immutable, provider-neutral metadata for one pinned runtime artifact.
/// </summary>
public sealed record TrustedArtifactManifest
{
    public required ProvisioningRequirement Requirement { get; init; }

    public required ArtifactPlatform Platform { get; init; }

    public required Uri ArtifactUri { get; init; }

    public required string Sha256 { get; init; }

    public required ArtifactArchiveKind ArchiveKind { get; init; }

    public required string EntryPoint { get; init; }

    public required long SizeInBytes { get; init; }

    public required string LicenseIdentity { get; init; }

    public required ArtifactConsentPolicy ConsentPolicy { get; init; }
}
