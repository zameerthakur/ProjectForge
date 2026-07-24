using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Infrastructure.Providers;

namespace ProjectForge.Infrastructure.Tests.Providers;

public sealed class LocalMockCapabilityProviderTests
{
    private static readonly Guid RequestId =
        Guid.Parse("6fb93580-e240-4a02-bec8-e58ce1b8f61b");

    private static readonly DateTimeOffset Timestamp =
        new(2026, 7, 23, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task ReportsHealthyLocalExecutionWithZeroCost()
    {
        var provider = Provider();
        var request = Request();

        var canExecute = await provider.CanExecuteAsync(request);
        var estimatedCost = await provider.EstimateCostAsync(request);
        var health = await provider.CheckHealthAsync();

        Assert.True(canExecute);
        Assert.Equal(0m, estimatedCost);
        Assert.Equal("local-mock", provider.Name);
        Assert.Equal(provider.Name, provider.Descriptor.Name);
        Assert.Equal(
            ProviderExecutionLocation.LocalProcess,
            provider.Descriptor.ExecutionLocation);
        Assert.False(provider.Descriptor.SupportsRepositoryAccess);
        Assert.False(provider.Descriptor.SupportsFileWriteAccess);
        Assert.False(provider.Descriptor.SupportsToolExecution);
        Assert.True(health.IsHealthy);
        Assert.Equal(provider.Name, health.ProviderName);
        Assert.Equal(Timestamp, health.CheckedAtUtc);
        Assert.Equal(TimeSpan.Zero, health.ResponseTime);
        Assert.Equal(
            "deterministic-mock",
            health.Metadata["provider.kind"]);
    }

    [Fact]
    public async Task ProducesDeterministicCorrelatedExecutionEvidence()
    {
        var provider = Provider();
        var request = Request(
            new Dictionary<string, string>
            {
                ["zeta"] = "last",
                ["alpha"] = "first"
            });

        var first = await provider.ExecuteAsync(request);
        var second = await provider.ExecuteAsync(request);

        Assert.True(first.IsSuccessful);
        Assert.Equal(RequestId, first.RequestId);
        Assert.Equal(provider.Name, first.ProviderName);
        Assert.Equal(0m, first.EstimatedCost);
        Assert.Equal(Timestamp, first.StartedAtUtc);
        Assert.Equal(Timestamp, first.CompletedAtUtc);
        Assert.Equal(TimeSpan.Zero, first.Duration);
        Assert.Equal(first.Summary, second.Summary);
        Assert.Equal(first.Output, second.Output);
        Assert.Equal(first.Metadata, second.Metadata);
        Assert.Contains("task 'Provider test'", first.Summary);
        Assert.Contains("Capability: Testing", first.Output);
        Assert.True(
            first.Output!.IndexOf("- alpha: first", StringComparison.Ordinal) <
            first.Output.IndexOf("- zeta: last", StringComparison.Ordinal));
        Assert.Equal("workflow-1", first.Metadata["workflow.id"]);
        Assert.Equal("task-1", first.Metadata["task.id"]);
        Assert.Equal("2", first.Metadata["input.count"]);
        Assert.Null(first.ErrorMessage);
    }

    [Fact]
    public void SupportsEveryDefinedEngineeringCapability()
    {
        var provider = Provider();

        Assert.Equal(
            Enum.GetValues<EngineeringCapability>(),
            provider.SupportedCapabilities);
    }

    [Fact]
    public async Task RejectsMalformedRequestsBeforeExecution()
    {
        var provider = Provider();
        var request = Request(instruction: " ");

        var exception = await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ExecuteAsync(request));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task HonorsCancellationDuringBoundedExecution()
    {
        var provider = new LocalMockCapabilityProvider(
            executionDelay: TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();

        var execution = provider.ExecuteAsync(Request(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => execution);
    }

    [Fact]
    public async Task HonorsPreCanceledHealthAndCostChecks()
    {
        var provider = Provider();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.CheckHealthAsync(cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => provider.EstimateCostAsync(Request(), cancellation.Token));
    }

    [Fact]
    public void RejectsAnUnboundedExecutionDelay()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new LocalMockCapabilityProvider(
                executionDelay: TimeSpan.FromSeconds(31)));
    }

    private static LocalMockCapabilityProvider Provider() =>
        new(new FixedTimeProvider(Timestamp));

    private static CapabilityExecutionRequest Request(
        IReadOnlyDictionary<string, string>? inputs = null,
        string instruction = "Produce deterministic evidence.") =>
        new()
        {
            RequestId = RequestId,
            WorkflowId = "workflow-1",
            TaskId = "task-1",
            TaskName = "Provider test",
            Instruction = instruction,
            Requirement = new CapabilityRequirement
            {
                Capability = EngineeringCapability.Testing,
                AllowCloudExecution = false
            },
            Inputs = inputs ?? new Dictionary<string, string>(),
            CreatedAtUtc = Timestamp
        };

    private sealed class FixedTimeProvider(DateTimeOffset timestamp)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => timestamp;
    }
}
