using System.Text.RegularExpressions;
using ProjectForge.Abstractions.Provisioning.Manifests;

namespace ProjectForge.Core.Provisioning.Manifests;

/// <summary>
/// Validates pinned artifact metadata before a provisioner is allowed to use it.
/// </summary>
public static partial class TrustedArtifactManifestValidator
{
    private static readonly HashSet<string> FloatingVersions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "latest",
            "current",
            "stable",
            "nightly",
            "preview",
            "edge",
            "snapshot"
        };

    public static ArtifactManifestValidationResult Validate(
        TrustedArtifactManifest? manifest)
    {
        var errors = new List<ArtifactManifestValidationError>();
        if (manifest is null)
        {
            Add(errors, "manifest.required", "A manifest is required.");
            return Result(errors);
        }

        ValidateRequirement(manifest, errors);
        ValidatePlatform(manifest, errors);
        ValidateSource(manifest, errors);

        if (string.IsNullOrWhiteSpace(manifest.Sha256) ||
            !Sha256Pattern().IsMatch(manifest.Sha256))
        {
            Add(
                errors,
                "sha256.invalid",
                "SHA-256 must contain exactly 64 hexadecimal characters.");
        }

        if (!Enum.IsDefined(manifest.ArchiveKind))
        {
            Add(
                errors,
                "archive.invalid",
                "Archive kind must be one of the supported package formats.");
        }

        ValidateEntryPoint(manifest.EntryPoint, errors);

        if (manifest.SizeInBytes <= 0)
        {
            Add(
                errors,
                "size.invalid",
                "Artifact size must be a positive exact byte count.");
        }

        if (string.IsNullOrWhiteSpace(manifest.LicenseIdentity))
        {
            Add(
                errors,
                "license.required",
                "A license identity is required.");
        }

        if (manifest.ConsentPolicy == ArtifactConsentPolicy.Unspecified ||
            !Enum.IsDefined(manifest.ConsentPolicy))
        {
            Add(
                errors,
                "consent.ambiguous",
                "Consent policy must explicitly state whether and when consent is required.");
        }

        return Result(errors);
    }

    private static void ValidateRequirement(
        TrustedArtifactManifest manifest,
        List<ArtifactManifestValidationError> errors)
    {
        if (manifest.Requirement is null)
        {
            Add(errors, "requirement.required", "A provisioning requirement is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.Requirement.Id))
        {
            Add(errors, "requirement.id.required", "Requirement ID is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Requirement.DisplayName))
        {
            Add(
                errors,
                "requirement.name.required",
                "Requirement display name is required.");
        }

        var version = manifest.Requirement.Version?.Trim();
        if (string.IsNullOrEmpty(version))
        {
            Add(errors, "version.required", "An exact version is required.");
        }
        else if (FloatingVersions.Contains(version) ||
                 version.Contains('*') ||
                 version.Contains('^') ||
                 version.Contains('~') ||
                 version.Contains('>') ||
                 version.Contains('<') ||
                 version.Contains("||", StringComparison.Ordinal))
        {
            Add(
                errors,
                "version.floating",
                "Version must be exact and must not use a floating label or range.");
        }
    }

    private static void ValidatePlatform(
        TrustedArtifactManifest manifest,
        List<ArtifactManifestValidationError> errors)
    {
        if (manifest.Platform is null)
        {
            Add(errors, "platform.required", "An exact target platform is required.");
            return;
        }

        if (string.IsNullOrWhiteSpace(manifest.Platform.OperatingSystem))
        {
            Add(errors, "platform.os.required", "Target operating system is required.");
        }

        if (string.IsNullOrWhiteSpace(manifest.Platform.Architecture))
        {
            Add(
                errors,
                "platform.architecture.required",
                "Target architecture is required.");
        }
    }

    private static void ValidateSource(
        TrustedArtifactManifest manifest,
        List<ArtifactManifestValidationError> errors)
    {
        var source = manifest.ArtifactUri;
        if (source is null || !source.IsAbsoluteUri)
        {
            Add(errors, "source.absolute.required", "Artifact source must be an absolute URI.");
            return;
        }

        if (!string.Equals(source.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            Add(errors, "source.https.required", "Artifact source must use HTTPS.");
        }

        if (!string.IsNullOrEmpty(source.UserInfo))
        {
            Add(errors, "source.credentials.forbidden", "Artifact source must not contain credentials.");
        }

        if (!string.IsNullOrEmpty(source.Fragment))
        {
            Add(errors, "source.fragment.forbidden", "Artifact source must not contain a fragment.");
        }

        if (!string.IsNullOrEmpty(source.Query))
        {
            Add(
                errors,
                "source.query.forbidden",
                "Artifact source must not contain query parameters or embedded access tokens.");
        }
    }

    private static void ValidateEntryPoint(
        string? entryPoint,
        List<ArtifactManifestValidationError> errors)
    {
        if (string.IsNullOrWhiteSpace(entryPoint))
        {
            Add(errors, "entrypoint.required", "A relative entrypoint is required.");
            return;
        }

        var normalized = entryPoint.Replace('\\', '/');
        var segments = normalized.Split('/');
        if (Path.IsPathRooted(entryPoint) ||
            normalized[0] == '/' ||
            normalized.Contains(':') ||
            segments.Any(segment =>
                segment.Length == 0 ||
                segment is "." or ".."))
        {
            Add(
                errors,
                "entrypoint.unsafe",
                "Entrypoint must be a normalized relative path without traversal.");
        }
    }

    private static void Add(
        List<ArtifactManifestValidationError> errors,
        string code,
        string message) =>
        errors.Add(new ArtifactManifestValidationError(code, message));

    private static ArtifactManifestValidationResult Result(
        List<ArtifactManifestValidationError> errors) =>
        new()
        {
            Errors = errors.AsReadOnly()
        };

    [GeneratedRegex("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Pattern();
}
