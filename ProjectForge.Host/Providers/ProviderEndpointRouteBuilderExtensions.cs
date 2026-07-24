using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Host.Providers;

/// <summary>
/// Maps operator-facing provider endpoints.
/// </summary>
public static class ProviderEndpointRouteBuilderExtensions
{
    /// <summary>
    /// Maps the provider readiness endpoint.
    /// </summary>
    public static IEndpointRouteBuilder MapProviderEndpoints(
        this IEndpointRouteBuilder endpoints)
    {
        ArgumentNullException.ThrowIfNull(endpoints);

        endpoints.MapGet(
            "/providers",
            async (
                IProviderRegistry registry,
                IProviderHealthChecker healthChecker,
                CancellationToken cancellationToken) =>
            {
                var providers = await ProviderReadinessSnapshot.CreateAsync(
                    registry,
                    healthChecker,
                    cancellationToken);
                return Results.Ok(providers);
            });

        return endpoints;
    }
}
