namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Provides access to the capability providers available to ProjectForge.
/// </summary>
/// <remarks>
/// The registry is responsible only for provider discovery.
/// It does not select, evaluate, execute, or monitor providers.
/// </remarks>
public interface IProviderRegistry
{
    /// <summary>
    /// Gets all capability providers currently registered with ProjectForge.
    /// </summary>
    IReadOnlyCollection<ICapabilityProvider> Providers { get; }
}
