namespace ProjectForge.Core.Provisioning.Manifests;

/// <summary>
/// Contains all validation failures found in a trusted artifact manifest.
/// </summary>
public sealed record ArtifactManifestValidationResult
{
    public required IReadOnlyList<ArtifactManifestValidationError> Errors
    {
        get;
        init;
    }

    public bool IsValid => Errors.Count == 0;
}
