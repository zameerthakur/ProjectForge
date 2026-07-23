using System.Diagnostics;
using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Core.Providers;

/// <summary>
/// Provides one policy-facing boundary for capability-provider health checks.
/// </summary>
public sealed class ProviderHealthChecker : IProviderHealthChecker
{
    private static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(10);
    private readonly TimeSpan _timeout;

    public ProviderHealthChecker()
        : this(DefaultTimeout)
    {
    }

    public ProviderHealthChecker(TimeSpan timeout)
    {
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(timeout),
                timeout,
                "The provider health-check timeout must be positive.");
        }

        _timeout = timeout;
    }

    /// <inheritdoc />
    public async Task<ProviderHealthReport> CheckHealthAsync(
        ICapabilityProvider provider,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provider);
        cancellationToken.ThrowIfCancellationRequested();

        var stopwatch = Stopwatch.StartNew();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken);
        timeout.CancelAfter(_timeout);

        try
        {
            return await provider
                .CheckHealthAsync(timeout.Token)
                .WaitAsync(_timeout, cancellationToken);
        }
        catch (OperationCanceledException)
            when (!cancellationToken.IsCancellationRequested)
        {
            return TimedOut(provider, stopwatch.Elapsed);
        }
        catch (TimeoutException)
        {
            timeout.Cancel();
            return TimedOut(provider, stopwatch.Elapsed);
        }
    }

    private ProviderHealthReport TimedOut(
        ICapabilityProvider provider,
        TimeSpan responseTime)
    {
        return new ProviderHealthReport
        {
            ProviderName = provider.Name,
            IsHealthy = false,
            StatusMessage =
                $"Health check timed out after {_timeout.TotalSeconds:g} seconds.",
            ResponseTime = responseTime
        };
    }
}
