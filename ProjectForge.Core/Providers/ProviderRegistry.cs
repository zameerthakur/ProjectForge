using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Core.Providers;

/// <summary>
/// Provides access to all capability providers registered with ProjectForge.
/// </summary>
/// <remarks>
/// The registry is responsible only for exposing registered providers.
/// It does not evaluate provider health, select providers, or execute requests.
/// </remarks>
public sealed class ProviderRegistry : IProviderRegistry
{
    private readonly IReadOnlyCollection<ICapabilityProvider> _providers;

    /// <summary>
    /// Initializes a new instance of the <see cref="ProviderRegistry"/> class.
    /// </summary>
    /// <param name="providers">
    /// The capability providers registered with dependency injection.
    /// </param>
    /// <exception cref="ArgumentNullException">
    /// Thrown when <paramref name="providers"/> is <see langword="null"/>.
    /// </exception>
    public ProviderRegistry(IEnumerable<ICapabilityProvider> providers)
    {
        ArgumentNullException.ThrowIfNull(providers);

        _providers = providers.ToArray();
    }

    /// <inheritdoc />
    public IReadOnlyCollection<ICapabilityProvider> Providers => _providers;
}
