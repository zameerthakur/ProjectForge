using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Core.Providers;

namespace ProjectForge.Core.Tests.Providers;

public sealed class ProviderHealthCheckerTests
{
    [Fact]
    public async Task ReturnsUnhealthyReportWhenProviderTimesOut()
    {
        var provider = Provider(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        var checker = new ProviderHealthChecker(
            TimeSpan.FromMilliseconds(50));

        var report = await checker.CheckHealthAsync(provider);

        Assert.False(report.IsHealthy);
        Assert.Equal(provider.Name, report.ProviderName);
        Assert.Contains("timed out", report.StatusMessage);
    }

    [Fact]
    public async Task PreservesCallerCancellation()
    {
        var provider = Provider(
            async cancellationToken =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                throw new InvalidOperationException("Unreachable.");
            });
        var checker = new ProviderHealthChecker(TimeSpan.FromSeconds(10));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checker.CheckHealthAsync(provider, cancellation.Token));
    }

    [Fact]
    public async Task DoesNotMisreportProviderCancellationAsTimeout()
    {
        var provider = Provider(
            _ => Task.FromCanceled<ProviderHealthReport>(
                new CancellationToken(canceled: true)));
        var checker = new ProviderHealthChecker(TimeSpan.FromSeconds(10));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => checker.CheckHealthAsync(provider));
    }

    [Fact]
    public void RejectsNonPositiveTimeout()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new ProviderHealthChecker(TimeSpan.Zero));
    }

    private static FakeCapabilityProvider Provider(
        Func<CancellationToken, Task<ProviderHealthReport>> healthCheck)
    {
        return new FakeCapabilityProvider
        {
            Name = "provider",
            Descriptor = new()
            {
                Name = "provider",
                ExecutionLocation = ProviderExecutionLocation.LocalProcess
            },
            HealthCheck = healthCheck
        };
    }
}
