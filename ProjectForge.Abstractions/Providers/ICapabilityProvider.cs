using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Health;

namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Defines a provider capable of executing one or more
/// engineering capabilities.
/// </summary>
/// <remarks>
/// Provider implementations may integrate with local tools,
/// local AI runtimes, cloud services, coding agents, or other
/// external engineering systems.
/// </remarks>
public interface ICapabilityProvider
{
    /// <summary>
    /// Gets the unique human-readable name of the provider.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Gets the engineering capabilities supported by the provider.
    /// </summary>
    IReadOnlyCollection<EngineeringCapability> SupportedCapabilities { get; }

    /// <summary>
    /// Determines whether the provider can execute the specified request
    /// under its current configuration and runtime conditions.
    /// </summary>
    /// <param name="request">
    /// The capability execution request to evaluate.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the evaluation.
    /// </param>
    /// <returns>
    /// <see langword="true"/> when the provider can execute the request;
    /// otherwise, <see langword="false"/>.
    /// </returns>
    Task<bool> CanExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Executes the specified capability request.
    /// </summary>
    /// <param name="request">
    /// The capability execution request.
    /// </param>
    /// <param name="cancellationToken">
    /// A token used to cancel the execution.
    /// </param>
    /// <returns>
    /// The result of the capability execution.
    /// </returns>
    Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks the current operational health of the provider.
    /// </summary>
    /// <param name="cancellationToken">
    /// A token used to cancel the health check.
    /// </param>
    /// <returns>
    /// A health report describing the provider's current operational status.
    /// </returns>
    Task<ProviderHealthReport> CheckHealthAsync(
        CancellationToken cancellationToken = default);
}
