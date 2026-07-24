namespace ProjectForge.Abstractions.Provisioning.Manifests;

/// <summary>
/// Defines when explicit user consent is required for a runtime artifact.
/// </summary>
public enum ArtifactConsentPolicy
{
    Unspecified,
    NotRequired,
    RequiredBeforeDownload,
    RequiredBeforeExecution
}
