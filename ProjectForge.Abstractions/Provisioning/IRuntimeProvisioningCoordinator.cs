namespace ProjectForge.Abstractions.Provisioning;

/// <summary>
/// Ensures all registered runtime dependencies are ready for provider use.
/// </summary>
public interface IRuntimeProvisioningCoordinator
{
    /// <summary>
    /// Gets the latest immutable state observed for each requirement that has
    /// been checked during this process.
    /// </summary>
    IReadOnlyCollection<ProvisioningSnapshot> Snapshots { get; }

    /// <summary>
    /// Checks, provisions, and verifies all registered dependencies.
    /// Concurrent requests for the same dependency share in-flight work.
    /// </summary>
    Task EnsureReadyAsync(
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks, provisions, and verifies one registered dependency by its
    /// stable requirement identifier.
    /// </summary>
    Task EnsureRequirementReadyAsync(
        string requirementId,
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Attempts to get the latest state observed for a requirement.
    /// </summary>
    bool TryGetSnapshot(
        string requirementId,
        out ProvisioningSnapshot snapshot);
}
