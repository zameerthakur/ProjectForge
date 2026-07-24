namespace ProjectForge.Abstractions.Provisioning.Consent;

public interface IProvisioningConsentStore
{
    Task<ProvisioningConsentReceipt> RecordAsync(
        ProvisioningConsentReceipt receipt,
        CancellationToken cancellationToken = default);

    Task<bool> HasActiveConsentAsync(
        string operatorId,
        ProvisioningConsentBinding binding,
        CancellationToken cancellationToken = default);

    Task<ProvisioningConsentReceipt?> RevokeAsync(
        Guid receiptId,
        string revokedByOperatorId,
        DateTimeOffset revokedAtUtc,
        CancellationToken cancellationToken = default);
}
