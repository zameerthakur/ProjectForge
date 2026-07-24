using ProjectForge.Abstractions.Provisioning.Manifests;

namespace ProjectForge.Abstractions.Provisioning.Consent;

/// <summary>
/// Identifies the exact artifact and license terms to which an operator may consent.
/// </summary>
public sealed record ProvisioningConsentBinding(
    string RequirementId,
    string RequirementVersion,
    string ArtifactSha256,
    string LicenseId,
    string LicenseSha256,
    ArtifactConsentPolicy ConsentPolicy);
