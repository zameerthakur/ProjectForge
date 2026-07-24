using ProjectForge.Abstractions.Provisioning;
using ProjectForge.Abstractions.Provisioning.Manifests;
using ProjectForge.Core.Provisioning.Manifests;

namespace ProjectForge.Core.Tests.Provisioning.Manifests;

public sealed class TrustedArtifactManifestValidatorTests
{
    [Fact]
    public void AcceptsCompletePinnedManifest()
    {
        var result = TrustedArtifactManifestValidator.Validate(ValidManifest());

        Assert.True(result.IsValid);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void ReportsRequiredManifest()
    {
        AssertError(null, "manifest.required");
    }

    [Fact]
    public void RejectsMissingRequirement()
    {
        AssertError(
            ValidManifest() with
            {
                Requirement = null!
            },
            "requirement.required");
    }

    [Theory]
    [InlineData("", "Example runtime", "requirement.id.required")]
    [InlineData("runtime.example", "", "requirement.name.required")]
    public void RejectsIncompleteRequirement(
        string id,
        string displayName,
        string expectedCode)
    {
        AssertError(
            ValidManifest() with
            {
                Requirement = ValidRequirement() with
                {
                    Id = id,
                    DisplayName = displayName
                }
            },
            expectedCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("latest")]
    [InlineData("LATEST")]
    [InlineData("stable")]
    [InlineData("current")]
    [InlineData("nightly")]
    [InlineData("preview")]
    [InlineData("edge")]
    [InlineData("1.*")]
    [InlineData("^1.2.3")]
    [InlineData("~1.2.3")]
    [InlineData(">1.2.3")]
    [InlineData("<2.0.0")]
    [InlineData("1.0.0 || 2.0.0")]
    public void RejectsMissingOrFloatingVersion(string version)
    {
        var manifest = ValidManifest() with
        {
            Requirement = ValidRequirement() with
            {
                Version = version
            }
        };

        AssertError(
            manifest,
            string.IsNullOrWhiteSpace(version)
                ? "version.required"
                : "version.floating");
    }

    [Theory]
    [InlineData("http://downloads.example.test/runtime.zip", "source.https.required")]
    [InlineData("ftp://downloads.example.test/runtime.zip", "source.https.required")]
    [InlineData("https://user:secret@downloads.example.test/runtime.zip", "source.credentials.forbidden")]
    [InlineData("https://downloads.example.test/runtime.zip#checksum", "source.fragment.forbidden")]
    [InlineData("https://downloads.example.test/runtime.zip?token=secret", "source.query.forbidden")]
    public void RejectsUntrustedArtifactSource(string source, string expectedCode)
    {
        AssertError(
            ValidManifest() with
            {
                ArtifactUri = new Uri(source)
            },
            expectedCode);
    }

    [Fact]
    public void RejectsRelativeArtifactSource()
    {
        AssertError(
            ValidManifest() with
            {
                ArtifactUri = new Uri("runtime.zip", UriKind.Relative)
            },
            "source.absolute.required");
    }

    [Fact]
    public void RejectsMissingPlatform()
    {
        AssertError(
            ValidManifest() with
            {
                Platform = null!
            },
            "platform.required");
    }

    [Theory]
    [InlineData("", "x64", "platform.os.required")]
    [InlineData("windows", "", "platform.architecture.required")]
    public void RejectsIncompletePlatform(
        string operatingSystem,
        string architecture,
        string expectedCode)
    {
        AssertError(
            ValidManifest() with
            {
                Platform = new ArtifactPlatform
                {
                    OperatingSystem = operatingSystem,
                    Architecture = architecture
                }
            },
            expectedCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    [InlineData("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa")]
    public void RejectsInvalidSha256(string sha256)
    {
        AssertError(
            ValidManifest() with
            {
                Sha256 = sha256
            },
            "sha256.invalid");
    }

    [Fact]
    public void RejectsUnknownArchiveKind()
    {
        AssertError(
            ValidManifest() with
            {
                ArchiveKind = (ArtifactArchiveKind)999
            },
            "archive.invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("../runtime")]
    [InlineData("bin/../runtime")]
    [InlineData("./runtime")]
    [InlineData("/bin/runtime")]
    [InlineData("C:\\bin\\runtime.exe")]
    [InlineData("bin//runtime")]
    [InlineData("https://example.test/runtime")]
    public void RejectsMissingOrUnsafeEntrypoint(string entryPoint)
    {
        AssertError(
            ValidManifest() with
            {
                EntryPoint = entryPoint
            },
            string.IsNullOrWhiteSpace(entryPoint)
                ? "entrypoint.required"
                : "entrypoint.unsafe");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void RejectsMissingOrInvalidSize(long sizeInBytes)
    {
        AssertError(
            ValidManifest() with
            {
                SizeInBytes = sizeInBytes
            },
            "size.invalid");
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void RejectsMissingLicenseIdentity(string licenseIdentity)
    {
        AssertError(
            ValidManifest() with
            {
                LicenseIdentity = licenseIdentity
            },
            "license.required");
    }

    [Theory]
    [InlineData(ArtifactConsentPolicy.Unspecified)]
    [InlineData((ArtifactConsentPolicy)999)]
    public void RejectsAmbiguousConsentPolicy(ArtifactConsentPolicy consentPolicy)
    {
        AssertError(
            ValidManifest() with
            {
                ConsentPolicy = consentPolicy
            },
            "consent.ambiguous");
    }

    [Theory]
    [InlineData(ArtifactConsentPolicy.NotRequired)]
    [InlineData(ArtifactConsentPolicy.RequiredBeforeDownload)]
    [InlineData(ArtifactConsentPolicy.RequiredBeforeExecution)]
    public void AcceptsEveryExplicitConsentPolicy(
        ArtifactConsentPolicy consentPolicy)
    {
        var result = TrustedArtifactManifestValidator.Validate(
            ValidManifest() with
            {
                ConsentPolicy = consentPolicy
            });

        Assert.True(result.IsValid);
    }

    [Fact]
    public void ReportsAllIndependentFailuresInStableOrder()
    {
        var manifest = ValidManifest() with
        {
            Requirement = ValidRequirement() with
            {
                Version = "latest"
            },
            ArtifactUri = new Uri("http://user:secret@example.test/a.zip#fragment"),
            Sha256 = "invalid",
            EntryPoint = "../runtime",
            SizeInBytes = 0,
            LicenseIdentity = " ",
            ConsentPolicy = ArtifactConsentPolicy.Unspecified
        };

        var result = TrustedArtifactManifestValidator.Validate(manifest);

        Assert.Equal(
            [
                "version.floating",
                "source.https.required",
                "source.credentials.forbidden",
                "source.fragment.forbidden",
                "sha256.invalid",
                "entrypoint.unsafe",
                "size.invalid",
                "license.required",
                "consent.ambiguous"
            ],
            result.Errors.Select(error => error.Code));
    }

    [Fact]
    public void ValidationResultDoesNotExposeMutableErrorCollection()
    {
        var result = TrustedArtifactManifestValidator.Validate(
            ValidManifest() with
            {
                Sha256 = "invalid"
            });

        Assert.IsAssignableFrom<IReadOnlyList<ArtifactManifestValidationError>>(
            result.Errors);
        Assert.False(result.Errors is List<ArtifactManifestValidationError>);
    }

    private static void AssertError(
        TrustedArtifactManifest? manifest,
        string expectedCode)
    {
        var result = TrustedArtifactManifestValidator.Validate(manifest);

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.Code == expectedCode);
    }

    private static TrustedArtifactManifest ValidManifest() =>
        new()
        {
            Requirement = ValidRequirement(),
            Platform = new ArtifactPlatform
            {
                OperatingSystem = "windows",
                Architecture = "x64"
            },
            ArtifactUri = new Uri(
                "https://downloads.example.test/runtime/1.2.3/runtime.zip"),
            Sha256 =
                "0123456789abcdef0123456789abcdef" +
                "0123456789abcdef0123456789abcdef",
            ArchiveKind = ArtifactArchiveKind.Zip,
            EntryPoint = "bin/runtime.exe",
            SizeInBytes = 123_456,
            LicenseIdentity = "MIT",
            ConsentPolicy = ArtifactConsentPolicy.NotRequired
        };

    private static ProvisioningRequirement ValidRequirement() =>
        new()
        {
            Id = "runtime.example",
            DisplayName = "Example runtime",
            Version = "1.2.3",
            Kind = ProvisioningArtifactKind.Executable
        };
}
