using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Core.Tests.Providers;

internal sealed class FakeCapabilityProvider : ISchedulableCapabilityProvider
{
    public required string Name { get; init; }

    public required ProviderDescriptor Descriptor { get; init; }

    public IReadOnlyCollection<EngineeringCapability> SupportedCapabilities
        { get; set; } = new[] { EngineeringCapability.Coding };

    public bool IsHealthy { get; init; } = true;

    public bool CanExecute { get; set; } = true;

    public decimal EstimatedCost { get; init; }

    public Func<CancellationToken, Task<ProviderHealthReport>>? HealthCheck
        { get; set; }

    public Task<bool> CanExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CanExecute);
    }

    public Task<decimal> EstimateCostAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(EstimatedCost);
    }

    public Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        throw new NotSupportedException("Selection tests do not execute providers.");
    }

    public Task<ProviderHealthReport> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (HealthCheck is not null)
        {
            return HealthCheck(cancellationToken);
        }

        return Task.FromResult(
            new ProviderHealthReport
            {
                ProviderName = Name,
                IsHealthy = IsHealthy,
                StatusMessage = IsHealthy ? "Healthy." : "Unavailable."
            });
    }
}
