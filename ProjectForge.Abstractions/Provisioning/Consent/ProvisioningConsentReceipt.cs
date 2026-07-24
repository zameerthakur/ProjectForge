namespace ProjectForge.Abstractions.Provisioning.Consent;

/// <summary>
/// Durable evidence of an operator's explicit provisioning consent.
/// License text and credentials are deliberately excluded.
/// </summary>
public sealed record ProvisioningConsentReceipt(
    Guid ReceiptId,
    string OperatorId,
    ProvisioningConsentBinding Binding,
    DateTimeOffset ConsentedAtUtc,
    DateTimeOffset? RevokedAtUtc = null,
    string? RevokedByOperatorId = null)
{
    public bool IsActive => RevokedAtUtc is null;
}
