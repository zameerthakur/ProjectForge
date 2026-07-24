namespace ProjectForge.Abstractions.Provisioning.Manifests;

/// <summary>
/// Identifies the packaging applied to a trusted runtime artifact.
/// </summary>
public enum ArtifactArchiveKind
{
    None,
    Zip,
    Tar,
    TarGzip
}
