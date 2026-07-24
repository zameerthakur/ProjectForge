namespace ProjectForge.Abstractions.Provisioning;

/// <summary>
/// Detects and provisions one versioned runtime dependency.
/// </summary>
/// <remarks>
/// Implementations own provider-specific acquisition and installation details.
/// They must use pinned trusted sources, verify artifact integrity, clean up
/// partial work, and recover safely after interruption. Implementations must
/// not make system-wide changes or accept licenses without explicit consent.
/// </remarks>
public interface IRuntimeProvisioner
{
    /// <summary>
    /// Gets the exact dependency managed by this provisioner.
    /// </summary>
    ProvisioningRequirement Requirement { get; }

    /// <summary>
    /// Determines whether the dependency is installed, healthy, and passes all
    /// required version and integrity checks.
    /// </summary>
    Task<bool> IsReadyAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Provisions the dependency and reports implementation-specific lifecycle
    /// progress.
    /// </summary>
    /// <remarks>
    /// The operation must honor cancellation and leave recoverable state.
    /// Implementations should apply bounded retries to transient failures and
    /// reuse valid artifacts from their application-owned per-user cache.
    /// </remarks>
    Task ProvisionAsync(
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
