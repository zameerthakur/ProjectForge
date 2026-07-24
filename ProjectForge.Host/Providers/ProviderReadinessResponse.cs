using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Host.Providers;

/// <summary>
/// Describes a provider's public scheduling characteristics and current
/// readiness without exposing provider configuration or health metadata.
/// </summary>
public sealed record ProviderReadinessResponse(
    string Name,
    IReadOnlyCollection<EngineeringCapability> Capabilities,
    ProviderDescriptorResponse? Descriptor,
    bool IsReady,
    string? Status,
    DateTimeOffset CheckedAtUtc,
    double ResponseTimeMilliseconds);

/// <summary>
/// Contains the non-sensitive provider characteristics used by scheduling.
/// </summary>
public sealed record ProviderDescriptorResponse(
    ProviderExecutionLocation ExecutionLocation,
    bool SupportsRepositoryAccess,
    bool SupportsFileWriteAccess,
    bool SupportsToolExecution);
