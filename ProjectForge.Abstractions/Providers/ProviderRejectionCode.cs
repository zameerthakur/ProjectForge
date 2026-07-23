namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Identifies why a provider was not eligible for a capability request.
/// </summary>
public enum ProviderRejectionCode
{
    /// <summary>
    /// The provider does not expose policy-based scheduling metadata.
    /// </summary>
    SchedulingMetadataUnavailable = 1,

    /// <summary>
    /// The provider does not support the requested engineering capability.
    /// </summary>
    CapabilityNotSupported = 2,

    /// <summary>
    /// The request prohibits cloud execution.
    /// </summary>
    CloudExecutionNotAllowed = 3,

    /// <summary>
    /// The provider cannot access a required source-code repository.
    /// </summary>
    RepositoryAccessNotSupported = 4,

    /// <summary>
    /// The provider cannot modify files when the request requires it.
    /// </summary>
    FileWriteAccessNotSupported = 5,

    /// <summary>
    /// The provider cannot execute required tools or operating-system commands.
    /// </summary>
    ToolExecutionNotSupported = 6,

    /// <summary>
    /// The provider is not currently healthy.
    /// </summary>
    ProviderUnhealthy = 7,

    /// <summary>
    /// The provider cannot execute the request in its current state.
    /// </summary>
    ProviderUnavailable = 8,

    /// <summary>
    /// The provider's estimated cost exceeds the request's cost ceiling.
    /// </summary>
    MaximumCostExceeded = 9
}
