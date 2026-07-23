namespace ProjectForge.Abstractions.Provisioning;

/// <summary>
/// Ensures all registered runtime dependencies are ready for provider use.
/// </summary>
public interface IRuntimeProvisioningCoordinator
{
    /// <summary>
    /// Checks, provisions, and verifies all registered dependencies.
    /// Concurrent requests for the same dependency share in-flight work.
    /// </summary>
    Task EnsureReadyAsync(
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default);
}
