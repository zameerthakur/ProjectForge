using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Core.Providers;
using ProjectForge.Host.Providers;

namespace ProjectForge.Host.Tests.Providers;

public sealed class ProviderReadinessSnapshotTests
{
    [Fact]
    public async Task CreateAsyncReturnsStableOperatorSafeReadiness()
    {
        var registry = new ProviderRegistry(
            [
                new TestProvider("zulu", EngineeringCapability.Testing),
                new TestProvider(
                    "alpha",
                    EngineeringCapability.Coding,
                    EngineeringCapability.Discovery)
            ]);
        var checker = new RecordingHealthChecker();

        var result = await ProviderReadinessSnapshot.CreateAsync(
            registry,
            checker,
            CancellationToken.None);

        Assert.Collection(
            result,
            provider =>
            {
                Assert.Equal("alpha", provider.Name);
                Assert.Equal(
                    [
                        EngineeringCapability.Discovery,
                        EngineeringCapability.Coding
                    ],
                    provider.Capabilities);
                Assert.True(provider.IsReady);
                Assert.Null(provider.Descriptor);
                Assert.DoesNotContain(
                    "secret",
                    provider.Status ?? string.Empty,
                    StringComparison.OrdinalIgnoreCase);
            },
            provider => Assert.Equal("zulu", provider.Name));
        Assert.Equal(2, checker.CheckedProviders.Count);
    }

    [Fact]
    public async Task CreateAsyncPreservesCallerCancellation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var registry = new ProviderRegistry(
            [new TestProvider("provider", EngineeringCapability.Coding)]);

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => ProviderReadinessSnapshot.CreateAsync(
                registry,
                new RecordingHealthChecker(),
                cancellation.Token));
    }

    private sealed class RecordingHealthChecker : IProviderHealthChecker
    {
        public List<string> CheckedProviders { get; } = [];

        public Task<ProviderHealthReport> CheckHealthAsync(
            ICapabilityProvider provider,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CheckedProviders.Add(provider.Name);
            return Task.FromResult(
                new ProviderHealthReport
                {
                    ProviderName = provider.Name,
                    IsHealthy = true,
                    StatusMessage = "Ready.",
                    Metadata = new Dictionary<string, string>
                    {
                        ["secret"] = "must-not-be-exposed"
                    }
                });
        }
    }

    private sealed class TestProvider(
        string name,
        params EngineeringCapability[] capabilities) : ICapabilityProvider
    {
        public string Name { get; } = name;

        public IReadOnlyCollection<EngineeringCapability>
            SupportedCapabilities { get; } = capabilities;

        public Task<bool> CanExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<ProviderHealthReport> CheckHealthAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
