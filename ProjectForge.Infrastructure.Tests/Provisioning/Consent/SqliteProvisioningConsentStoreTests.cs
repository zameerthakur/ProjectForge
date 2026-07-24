using ProjectForge.Abstractions.Provisioning.Consent;
using ProjectForge.Abstractions.Provisioning.Manifests;
using ProjectForge.Infrastructure.Provisioning.Consent;

namespace ProjectForge.Infrastructure.Tests.Provisioning.Consent;

public sealed class SqliteProvisioningConsentStoreTests : IDisposable
{
    private const string ArtifactHash = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa";
    private const string LicenseHash = "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb";
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"projectforge-consent-{Guid.NewGuid():N}");
    private string DatabasePath => Path.Combine(_directory, "consent.db");

    [Fact]
    public async Task RecordSurvivesRestartAndRequiresAnExactMatch()
    {
        var receipt = CreateReceipt();
        var first = new SqliteProvisioningConsentStore(DatabasePath);
        await first.RecordAsync(receipt);

        var restarted = new SqliteProvisioningConsentStore(DatabasePath);
        Assert.True(await restarted.HasActiveConsentAsync(receipt.OperatorId, receipt.Binding));

        var mismatches = new[]
        {
            receipt.Binding with { RequirementId = "runtime.other" },
            receipt.Binding with { RequirementVersion = "2.0.0" },
            receipt.Binding with { ArtifactSha256 = new string('c', 64) },
            receipt.Binding with { LicenseId = "synthetic-license-v2" },
            receipt.Binding with { LicenseSha256 = new string('d', 64) },
            receipt.Binding with { ConsentPolicy = ArtifactConsentPolicy.RequiredBeforeExecution }
        };

        foreach (var mismatch in mismatches)
        {
            Assert.False(await restarted.HasActiveConsentAsync(receipt.OperatorId, mismatch));
        }

        Assert.False(await restarted.HasActiveConsentAsync("operator-2", receipt.Binding));
    }

    [Fact]
    public async Task RepeatedRecordIsIdempotentButIdentifierReuseIsRejected()
    {
        var store = new SqliteProvisioningConsentStore(DatabasePath);
        var receipt = CreateReceipt();

        var first = await store.RecordAsync(receipt);
        var second = await store.RecordAsync(receipt);

        Assert.Equal(first, second);
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => store.RecordAsync(receipt with { OperatorId = "operator-2" }));
    }

    [Fact]
    public async Task ConcurrentRecordsConvergeOnOneReceipt()
    {
        var receipt = CreateReceipt();
        var stores = Enumerable.Range(0, 12)
            .Select(_ => new SqliteProvisioningConsentStore(DatabasePath))
            .ToArray();

        var results = await Task.WhenAll(stores.Select(store => store.RecordAsync(receipt)));

        Assert.All(results, result => Assert.Equal(receipt, result));
        Assert.True(await stores[0].HasActiveConsentAsync(receipt.OperatorId, receipt.Binding));
    }

    [Fact]
    public async Task RevocationIsExplicitIdempotentAndSurvivesRestart()
    {
        var receipt = CreateReceipt();
        var store = new SqliteProvisioningConsentStore(DatabasePath);
        await store.RecordAsync(receipt);
        var revokedAt = new DateTimeOffset(2026, 7, 24, 2, 0, 0, TimeSpan.Zero);

        var revoked = await store.RevokeAsync(receipt.ReceiptId, "security-operator", revokedAt);
        var repeated = await store.RevokeAsync(
            receipt.ReceiptId,
            "different-operator",
            revokedAt.AddMinutes(1));

        Assert.NotNull(revoked);
        Assert.False(revoked.IsActive);
        Assert.Equal("security-operator", revoked.RevokedByOperatorId);
        Assert.Equal(revoked, repeated);

        var restarted = new SqliteProvisioningConsentStore(DatabasePath);
        Assert.False(await restarted.HasActiveConsentAsync(receipt.OperatorId, receipt.Binding));
        Assert.Null(await restarted.RevokeAsync(Guid.NewGuid(), "security-operator", revokedAt));
    }

    [Fact]
    public async Task MalformedOrAmbiguousEvidenceIsRejected()
    {
        var store = new SqliteProvisioningConsentStore(DatabasePath);
        var receipt = CreateReceipt();

        var malformed = new[]
        {
            receipt with { ReceiptId = Guid.Empty },
            receipt with { OperatorId = " operator-1" },
            receipt with { OperatorId = " " },
            receipt with { Binding = receipt.Binding with { RequirementId = "" } },
            receipt with { Binding = receipt.Binding with { RequirementVersion = "1.0.0 " } },
            receipt with { Binding = receipt.Binding with { ArtifactSha256 = "not-a-hash" } },
            receipt with { Binding = receipt.Binding with { LicenseId = " " } },
            receipt with { Binding = receipt.Binding with { LicenseSha256 = new string('z', 64) } },
            receipt with { Binding = receipt.Binding with { ConsentPolicy = ArtifactConsentPolicy.NotRequired } },
            receipt with { ConsentedAtUtc = receipt.ConsentedAtUtc.ToOffset(TimeSpan.FromHours(1)) },
            receipt with
            {
                RevokedAtUtc = receipt.ConsentedAtUtc.AddMinutes(1),
                RevokedByOperatorId = "operator-1"
            }
        };

        foreach (var invalid in malformed)
        {
            await Assert.ThrowsAsync<ArgumentException>(() => store.RecordAsync(invalid));
        }
    }

    [Fact]
    public async Task HashesAreCanonicalizedWithoutStoringLicenseContent()
    {
        var store = new SqliteProvisioningConsentStore(DatabasePath);
        var receipt = CreateReceipt() with
        {
            Binding = CreateReceipt().Binding with
            {
                ArtifactSha256 = ArtifactHash.ToUpperInvariant(),
                LicenseSha256 = LicenseHash.ToUpperInvariant()
            }
        };

        var stored = await store.RecordAsync(receipt);

        Assert.Equal(ArtifactHash, stored.Binding.ArtifactSha256);
        Assert.Equal(LicenseHash, stored.Binding.LicenseSha256);
        Assert.DoesNotContain(
            "license text",
            string.Join("|", stored.Binding.RequirementId, stored.Binding.LicenseId),
            StringComparison.OrdinalIgnoreCase);
    }

    private static ProvisioningConsentReceipt CreateReceipt() =>
        new(
            Guid.Parse("2146efee-96c4-45c7-9ab8-0da94e214677"),
            "operator-1",
            new ProvisioningConsentBinding(
                "runtime.synthetic",
                "1.0.0",
                ArtifactHash,
                "synthetic-license-v1",
                LicenseHash,
                ArtifactConsentPolicy.RequiredBeforeDownload),
            new DateTimeOffset(2026, 7, 24, 1, 0, 0, TimeSpan.Zero));

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}
