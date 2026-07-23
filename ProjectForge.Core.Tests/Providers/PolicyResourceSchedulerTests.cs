using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Core.Providers;

namespace ProjectForge.Core.Tests.Providers;

public sealed class PolicyResourceSchedulerTests
{
    [Fact]
    public async Task SelectsLocalProviderWhenLocalExecutionIsPreferred()
    {
        var cloud = Provider(
            "cloud",
            ProviderExecutionLocation.Cloud,
            estimatedCost: 0.01m);
        var local = Provider(
            "local",
            ProviderExecutionLocation.LocalModel,
            estimatedCost: 1.00m);

        var result = await Scheduler(cloud, local)
            .SelectProviderWithEvidenceAsync(Request());

        Assert.Same(local, result.SelectedProvider);
        Assert.Equal(2, result.Evaluations.Count);
    }

    [Fact]
    public async Task SelectsLowestCostWhenLocalExecutionIsNotPreferred()
    {
        var local = Provider(
            "local",
            ProviderExecutionLocation.LocalModel,
            estimatedCost: 1.00m);
        var cloud = Provider(
            "cloud",
            ProviderExecutionLocation.Cloud,
            estimatedCost: 0.01m);

        var result = await Scheduler(local, cloud)
            .SelectProviderWithEvidenceAsync(
                Request(preferLocalExecution: false));

        Assert.Same(cloud, result.SelectedProvider);
        Assert.Equal(0.01m, result.EstimatedCost);
    }

    [Fact]
    public async Task RejectsCloudProviderWhenCloudExecutionIsProhibited()
    {
        var cloud = Provider(
            "cloud",
            ProviderExecutionLocation.Cloud);
        var local = Provider(
            "local",
            ProviderExecutionLocation.LocalProcess);

        var result = await Scheduler(cloud, local)
            .SelectProviderWithEvidenceAsync(
                Request(allowCloudExecution: false));

        Assert.Same(local, result.SelectedProvider);
        var cloudEvaluation = Assert.Single(
            result.Evaluations,
            evaluation => ReferenceEquals(evaluation.Provider, cloud));
        Assert.Contains(
            cloudEvaluation.Rejections,
            rejection =>
                rejection.Code ==
                ProviderRejectionCode.CloudExecutionNotAllowed);
    }

    [Theory]
    [InlineData(true, false, false, ProviderRejectionCode.RepositoryAccessNotSupported)]
    [InlineData(false, true, false, ProviderRejectionCode.FileWriteAccessNotSupported)]
    [InlineData(false, false, true, ProviderRejectionCode.ToolExecutionNotSupported)]
    public async Task RejectsProviderMissingRequiredExecutionPermission(
        bool requiresRepository,
        bool requiresFileWrite,
        bool requiresTools,
        ProviderRejectionCode expectedCode)
    {
        var provider = Provider(
            "restricted",
            ProviderExecutionLocation.LocalProcess);

        var exception = await Assert.ThrowsAsync<ProviderSelectionException>(
            () => Scheduler(provider).SelectProviderWithEvidenceAsync(
                Request(
                    requiresRepository: requiresRepository,
                    requiresFileWrite: requiresFileWrite,
                    requiresTools: requiresTools)));

        var evaluation = Assert.Single(exception.Evaluations);
        Assert.Contains(
            evaluation.Rejections,
            rejection => rejection.Code == expectedCode);
    }

    [Fact]
    public async Task RejectsProviderAboveMaximumEstimatedCost()
    {
        var expensive = Provider(
            "expensive",
            ProviderExecutionLocation.Cloud,
            estimatedCost: 5m);

        var exception = await Assert.ThrowsAsync<ProviderSelectionException>(
            () => Scheduler(expensive).SelectProviderWithEvidenceAsync(
                Request(maximumEstimatedCost: 1m)));

        var evaluation = Assert.Single(exception.Evaluations);
        Assert.Equal(5m, evaluation.EstimatedCost);
        Assert.Contains(
            evaluation.Rejections,
            rejection =>
                rejection.Code == ProviderRejectionCode.MaximumCostExceeded);
    }

    [Fact]
    public async Task RejectsUnhealthyProviderBeforeCostEstimation()
    {
        var unhealthy = Provider(
            "unhealthy",
            ProviderExecutionLocation.LocalProcess,
            isHealthy: false);

        var exception = await Assert.ThrowsAsync<ProviderSelectionException>(
            () => Scheduler(unhealthy).SelectProviderWithEvidenceAsync(Request()));

        var evaluation = Assert.Single(exception.Evaluations);
        Assert.Null(evaluation.EstimatedCost);
        Assert.Contains(
            evaluation.Rejections,
            rejection => rejection.Code == ProviderRejectionCode.ProviderUnhealthy);
    }

    [Fact]
    public async Task UsesProviderNameAsDeterministicFinalTieBreaker()
    {
        var zulu = Provider("zulu", ProviderExecutionLocation.LocalProcess);
        var alpha = Provider("alpha", ProviderExecutionLocation.LocalProcess);

        var result = await Scheduler(zulu, alpha)
            .SelectProviderWithEvidenceAsync(Request());

        Assert.Same(alpha, result.SelectedProvider);
    }

    [Fact]
    public async Task ObservesCancellationBeforeEvaluatingProviders()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Scheduler(
                    Provider("local", ProviderExecutionLocation.LocalProcess))
                .SelectProviderWithEvidenceAsync(
                    Request(),
                    cancellation.Token));
    }

    private static PolicyResourceScheduler Scheduler(
        params ICapabilityProvider[] providers)
    {
        return new PolicyResourceScheduler(
            new ProviderRegistry(providers),
            new ProviderHealthChecker());
    }

    private static FakeCapabilityProvider Provider(
        string name,
        ProviderExecutionLocation location,
        decimal estimatedCost = 0m,
        bool isHealthy = true)
    {
        return new FakeCapabilityProvider
        {
            Name = name,
            Descriptor = new ProviderDescriptor
            {
                Name = name,
                ExecutionLocation = location
            },
            EstimatedCost = estimatedCost,
            IsHealthy = isHealthy
        };
    }

    private static CapabilityExecutionRequest Request(
        bool preferLocalExecution = true,
        bool allowCloudExecution = true,
        bool requiresRepository = false,
        bool requiresFileWrite = false,
        bool requiresTools = false,
        decimal? maximumEstimatedCost = null)
    {
        return new CapabilityExecutionRequest
        {
            WorkflowId = "workflow-1",
            TaskId = "task-1",
            TaskName = "Test provider selection",
            Instruction = "Select a provider.",
            Requirement = new CapabilityRequirement
            {
                Capability = EngineeringCapability.Coding,
                PreferLocalExecution = preferLocalExecution,
                AllowCloudExecution = allowCloudExecution,
                RequiresRepositoryAccess = requiresRepository,
                RequiresFileWriteAccess = requiresFileWrite,
                RequiresToolExecution = requiresTools,
                MaximumEstimatedCost = maximumEstimatedCost
            }
        };
    }
}
