namespace ProjectForge.Core.Provisioning.Manifests;

/// <summary>
/// Describes one deterministic trusted-manifest validation failure.
/// </summary>
public sealed record ArtifactManifestValidationError(
    string Code,
    string Message);
