using System.Globalization;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using ProjectForge.Abstractions.Provisioning.Consent;
using ProjectForge.Abstractions.Provisioning.Manifests;

namespace ProjectForge.Infrastructure.Provisioning.Consent;

/// <summary>
/// Stores explicit provisioning consent receipts in an isolated SQLite schema.
/// </summary>
public sealed partial class SqliteProvisioningConsentStore :
    IProvisioningConsentStore,
    IDisposable
{
    private readonly string _connectionString;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SqliteProvisioningConsentStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);
        var fullPath = Path.GetFullPath(databasePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false,
            DefaultTimeout = 30
        }.ToString();
    }

    public async Task<ProvisioningConsentReceipt> RecordAsync(
        ProvisioningConsentReceipt receipt,
        CancellationToken cancellationToken = default)
    {
        ValidateReceipt(receipt);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        await using var insert = connection.CreateCommand();
        insert.Transaction = transaction;
        insert.CommandText =
            """
            INSERT INTO provisioning_consent_receipts (
                receipt_id, operator_id, requirement_id, requirement_version,
                artifact_sha256, license_id, license_sha256, consent_policy,
                consented_at_utc, revoked_at_utc, revoked_by_operator_id)
            VALUES ($receipt, $operator, $requirement, $version, $artifact, $license,
                $licenseHash, $policy, $consented, $revoked, $revokedBy)
            ON CONFLICT(receipt_id) DO NOTHING;
            """;
        AddReceiptParameters(insert, receipt);
        await insert.ExecuteNonQueryAsync(cancellationToken);

        var stored = await GetAsync(connection, transaction, receipt.ReceiptId, cancellationToken);
        if (stored != receipt with
        {
            Binding = receipt.Binding with
            {
                ArtifactSha256 = NormalizeHash(receipt.Binding.ArtifactSha256),
                LicenseSha256 = NormalizeHash(receipt.Binding.LicenseSha256)
            }
        })
        {
            throw new InvalidOperationException(
                "The receipt identifier is already bound to different consent evidence.");
        }

        await transaction.CommitAsync(cancellationToken);
        return stored!;
    }

    public async Task<bool> HasActiveConsentAsync(
        string operatorId,
        ProvisioningConsentBinding binding,
        CancellationToken cancellationToken = default)
    {
        ValidateIdentity(operatorId, nameof(operatorId));
        ValidateBinding(binding);
        await using var connection = await OpenAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT EXISTS (
                SELECT 1 FROM provisioning_consent_receipts
                WHERE operator_id = $operator
                  AND requirement_id = $requirement
                  AND requirement_version = $version
                  AND artifact_sha256 = $artifact
                  AND license_id = $license
                  AND license_sha256 = $licenseHash
                  AND consent_policy = $policy
                  AND revoked_at_utc IS NULL);
            """;
        AddBindingParameters(command, operatorId, binding);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task<ProvisioningConsentReceipt?> RevokeAsync(
        Guid receiptId,
        string revokedByOperatorId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (receiptId == Guid.Empty) throw new ArgumentException("Receipt ID is required.", nameof(receiptId));
        ValidateIdentity(revokedByOperatorId, nameof(revokedByOperatorId));
        if (revokedAtUtc.Offset != TimeSpan.Zero) throw new ArgumentException("Revocation time must be UTC.", nameof(revokedAtUtc));

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            UPDATE provisioning_consent_receipts
            SET revoked_at_utc = $revoked, revoked_by_operator_id = $revokedBy
            WHERE receipt_id = $receipt AND revoked_at_utc IS NULL;
            """;
        command.Parameters.AddWithValue("$receipt", receiptId.ToString("D"));
        command.Parameters.AddWithValue("$revoked", Format(revokedAtUtc));
        command.Parameters.AddWithValue("$revokedBy", revokedByOperatorId);
        await command.ExecuteNonQueryAsync(cancellationToken);
        var stored = await GetAsync(connection, transaction, receiptId, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return stored;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await EnsureInitializedAsync(connection, cancellationToken);
        return connection;
    }

    private async Task EnsureInitializedAsync(SqliteConnection connection, CancellationToken cancellationToken)
    {
        if (_initialized) return;
        await _initialization.WaitAsync(cancellationToken);
        try
        {
            if (_initialized) return;
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                PRAGMA journal_mode = WAL;
                CREATE TABLE IF NOT EXISTS provisioning_consent_receipts (
                    receipt_id TEXT PRIMARY KEY,
                    operator_id TEXT NOT NULL,
                    requirement_id TEXT NOT NULL,
                    requirement_version TEXT NOT NULL,
                    artifact_sha256 TEXT NOT NULL,
                    license_id TEXT NOT NULL,
                    license_sha256 TEXT NOT NULL,
                    consent_policy INTEGER NOT NULL,
                    consented_at_utc TEXT NOT NULL,
                    revoked_at_utc TEXT NULL,
                    revoked_by_operator_id TEXT NULL,
                    CHECK (length(artifact_sha256) = 64),
                    CHECK (length(license_sha256) = 64),
                    CHECK ((revoked_at_utc IS NULL) = (revoked_by_operator_id IS NULL))
                );
                CREATE INDEX IF NOT EXISTS ix_consent_exact_active
                ON provisioning_consent_receipts (
                    operator_id, requirement_id, requirement_version, artifact_sha256,
                    license_id, license_sha256, consent_policy, revoked_at_utc);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initialization.Release();
        }
    }

    private static async Task<ProvisioningConsentReceipt?> GetAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid receiptId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT * FROM provisioning_consent_receipts WHERE receipt_id = $receipt;";
        command.Parameters.AddWithValue("$receipt", receiptId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        return new ProvisioningConsentReceipt(
            Guid.Parse(reader.GetString(reader.GetOrdinal("receipt_id"))),
            reader.GetString(reader.GetOrdinal("operator_id")),
            new ProvisioningConsentBinding(
                reader.GetString(reader.GetOrdinal("requirement_id")),
                reader.GetString(reader.GetOrdinal("requirement_version")),
                reader.GetString(reader.GetOrdinal("artifact_sha256")),
                reader.GetString(reader.GetOrdinal("license_id")),
                reader.GetString(reader.GetOrdinal("license_sha256")),
                (ArtifactConsentPolicy)reader.GetInt32(reader.GetOrdinal("consent_policy"))),
            DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("consented_at_utc")), CultureInfo.InvariantCulture),
            reader.IsDBNull(reader.GetOrdinal("revoked_at_utc"))
                ? null
                : DateTimeOffset.Parse(reader.GetString(reader.GetOrdinal("revoked_at_utc")), CultureInfo.InvariantCulture),
            reader.IsDBNull(reader.GetOrdinal("revoked_by_operator_id"))
                ? null
                : reader.GetString(reader.GetOrdinal("revoked_by_operator_id")));
    }

    private static void AddReceiptParameters(SqliteCommand command, ProvisioningConsentReceipt receipt)
    {
        command.Parameters.AddWithValue("$receipt", receipt.ReceiptId.ToString("D"));
        AddBindingParameters(command, receipt.OperatorId, receipt.Binding);
        command.Parameters.AddWithValue("$consented", Format(receipt.ConsentedAtUtc));
        command.Parameters.AddWithValue("$revoked", (object?)receipt.RevokedAtUtc is null ? DBNull.Value : Format(receipt.RevokedAtUtc.Value));
        command.Parameters.AddWithValue("$revokedBy", (object?)receipt.RevokedByOperatorId ?? DBNull.Value);
    }

    private static void AddBindingParameters(SqliteCommand command, string operatorId, ProvisioningConsentBinding binding)
    {
        command.Parameters.AddWithValue("$operator", operatorId);
        command.Parameters.AddWithValue("$requirement", binding.RequirementId);
        command.Parameters.AddWithValue("$version", binding.RequirementVersion);
        command.Parameters.AddWithValue("$artifact", NormalizeHash(binding.ArtifactSha256));
        command.Parameters.AddWithValue("$license", binding.LicenseId);
        command.Parameters.AddWithValue("$licenseHash", NormalizeHash(binding.LicenseSha256));
        command.Parameters.AddWithValue("$policy", (int)binding.ConsentPolicy);
    }

    private static void ValidateReceipt(ProvisioningConsentReceipt receipt)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        if (receipt.ReceiptId == Guid.Empty)
        {
            throw new ArgumentException(
                "Receipt ID is required.",
                nameof(receipt));
        }
        ValidateIdentity(receipt.OperatorId, nameof(receipt));
        ValidateBinding(receipt.Binding);
        if (receipt.ConsentedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException(
                "Consent time must be UTC.",
                nameof(receipt));
        }

        if (receipt.RevokedAtUtc is not null || receipt.RevokedByOperatorId is not null)
        {
            throw new ArgumentException("New receipts cannot be recorded as revoked.", nameof(receipt));
        }
    }

    private static void ValidateBinding(ProvisioningConsentBinding binding)
    {
        ArgumentNullException.ThrowIfNull(binding);
        ValidateIdentity(binding.RequirementId, nameof(binding.RequirementId));
        ValidateIdentity(binding.RequirementVersion, nameof(binding.RequirementVersion));
        ValidateHash(binding.ArtifactSha256, nameof(binding.ArtifactSha256));
        ValidateIdentity(binding.LicenseId, nameof(binding.LicenseId));
        ValidateHash(binding.LicenseSha256, nameof(binding.LicenseSha256));
        if (binding.ConsentPolicy is not ArtifactConsentPolicy.RequiredBeforeDownload
            and not ArtifactConsentPolicy.RequiredBeforeExecution)
            throw new ArgumentException("Only an explicit consent policy can produce a receipt.", nameof(binding));
    }

    private static void ValidateIdentity(string value, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value) || value != value.Trim() || value.Length > 256)
            throw new ArgumentException("Identity values must be non-empty, unambiguous, and at most 256 characters.", parameterName);
    }

    private static void ValidateHash(string value, string parameterName)
    {
        if (string.IsNullOrEmpty(value) || !Sha256Regex().IsMatch(value))
            throw new ArgumentException("SHA-256 values must contain exactly 64 hexadecimal characters.", parameterName);
    }

    private static string NormalizeHash(string value) => value.ToLowerInvariant();
    private static string Format(DateTimeOffset value) => value.ToString("O", CultureInfo.InvariantCulture);

    [GeneratedRegex(@"\A[0-9a-fA-F]{64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex Sha256Regex();

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _initialization.Dispose();
        _disposed = true;
    }
}
