using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Core.Providers;

/// <summary>
/// Enforces mandatory provider constraints before applying deterministic
/// locality and cost preferences.
/// </summary>
public sealed class PolicyResourceScheduler :
    IResourceScheduler,
    IExplainableResourceScheduler
{
    private readonly IProviderRegistry _providerRegistry;
    private readonly IProviderHealthChecker _healthChecker;

    /// <summary>
    /// Initializes a policy-based resource scheduler.
    /// </summary>
    public PolicyResourceScheduler(
        IProviderRegistry providerRegistry,
        IProviderHealthChecker healthChecker)
    {
        ArgumentNullException.ThrowIfNull(providerRegistry);
        ArgumentNullException.ThrowIfNull(healthChecker);

        _providerRegistry = providerRegistry;
        _healthChecker = healthChecker;
    }

    /// <inheritdoc />
    public async Task<ICapabilityProvider> SelectProviderAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        var result = await SelectProviderWithEvidenceAsync(
            request,
            cancellationToken);

        return result.SelectedProvider;
    }

    /// <inheritdoc />
    public async Task<ProviderSelectionResult> SelectProviderWithEvidenceAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        var evaluations = new List<ProviderEvaluation>();

        foreach (var provider in _providerRegistry.Providers)
        {
            cancellationToken.ThrowIfCancellationRequested();
            evaluations.Add(
                await EvaluateAsync(provider, request, cancellationToken));
        }

        var selected = evaluations
            .Where(evaluation => evaluation.IsEligible)
            .OrderBy(
                evaluation => LocalityRank(
                    (ISchedulableCapabilityProvider)evaluation.Provider,
                    request.Requirement.PreferLocalExecution))
            .ThenBy(evaluation => evaluation.EstimatedCost)
            .ThenBy(evaluation => evaluation.Provider.Name, StringComparer.Ordinal)
            .FirstOrDefault();

        if (selected is null)
        {
            throw new ProviderSelectionException(
                $"No eligible provider is available for capability " +
                $"'{request.Requirement.Capability}'.",
                evaluations);
        }

        return new ProviderSelectionResult
        {
            SelectedProvider =
                (ISchedulableCapabilityProvider)selected.Provider,
            EstimatedCost = selected.EstimatedCost!.Value,
            Evaluations = evaluations
        };
    }

    private async Task<ProviderEvaluation> EvaluateAsync(
        ICapabilityProvider provider,
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken)
    {
        var rejections = new List<ProviderRejection>();

        if (!provider.SupportedCapabilities.Contains(
                request.Requirement.Capability))
        {
            Reject(
                rejections,
                ProviderRejectionCode.CapabilityNotSupported,
                $"Provider '{provider.Name}' does not support capability " +
                $"'{request.Requirement.Capability}'.");
        }

        if (provider is not ISchedulableCapabilityProvider schedulable)
        {
            Reject(
                rejections,
                ProviderRejectionCode.SchedulingMetadataUnavailable,
                $"Provider '{provider.Name}' does not expose scheduling metadata.");

            return Evaluation(provider, null, rejections);
        }

        EvaluateStaticConstraints(
            schedulable,
            request.Requirement,
            rejections);

        if (rejections.Count > 0)
        {
            return Evaluation(provider, null, rejections);
        }

        var health = await _healthChecker.CheckHealthAsync(
            provider,
            cancellationToken);

        if (!health.IsHealthy)
        {
            Reject(
                rejections,
                ProviderRejectionCode.ProviderUnhealthy,
                health.StatusMessage ?? $"Provider '{provider.Name}' is unhealthy.");

            return Evaluation(provider, null, rejections);
        }

        if (!await provider.CanExecuteAsync(request, cancellationToken))
        {
            Reject(
                rejections,
                ProviderRejectionCode.ProviderUnavailable,
                $"Provider '{provider.Name}' cannot currently execute the request.");

            return Evaluation(provider, null, rejections);
        }

        var estimatedCost = await schedulable.EstimateCostAsync(
            request,
            cancellationToken);

        if (request.Requirement.MaximumEstimatedCost is decimal maximumCost &&
            estimatedCost > maximumCost)
        {
            Reject(
                rejections,
                ProviderRejectionCode.MaximumCostExceeded,
                $"Provider '{provider.Name}' estimated cost {estimatedCost} " +
                $"exceeds the permitted maximum {maximumCost}.");
        }

        return Evaluation(provider, estimatedCost, rejections);
    }

    private static void EvaluateStaticConstraints(
        ISchedulableCapabilityProvider provider,
        CapabilityRequirement requirement,
        ICollection<ProviderRejection> rejections)
    {
        var descriptor = provider.Descriptor;

        if (!requirement.AllowCloudExecution &&
            descriptor.ExecutionLocation == ProviderExecutionLocation.Cloud)
        {
            Reject(
                rejections,
                ProviderRejectionCode.CloudExecutionNotAllowed,
                $"Provider '{provider.Name}' requires cloud execution.");
        }

        if (requirement.RequiresRepositoryAccess &&
            !descriptor.SupportsRepositoryAccess)
        {
            Reject(
                rejections,
                ProviderRejectionCode.RepositoryAccessNotSupported,
                $"Provider '{provider.Name}' cannot access a repository.");
        }

        if (requirement.RequiresFileWriteAccess &&
            !descriptor.SupportsFileWriteAccess)
        {
            Reject(
                rejections,
                ProviderRejectionCode.FileWriteAccessNotSupported,
                $"Provider '{provider.Name}' cannot modify files.");
        }

        if (requirement.RequiresToolExecution &&
            !descriptor.SupportsToolExecution)
        {
            Reject(
                rejections,
                ProviderRejectionCode.ToolExecutionNotSupported,
                $"Provider '{provider.Name}' cannot execute tools.");
        }
    }

    private static ProviderEvaluation Evaluation(
        ICapabilityProvider provider,
        decimal? estimatedCost,
        IReadOnlyCollection<ProviderRejection> rejections)
    {
        return new ProviderEvaluation
        {
            Provider = provider,
            EstimatedCost = estimatedCost,
            Rejections = rejections
        };
    }

    private static int LocalityRank(
        ISchedulableCapabilityProvider provider,
        bool preferLocal)
    {
        if (!preferLocal)
        {
            return 0;
        }

        return provider.Descriptor.ExecutionLocation switch
        {
            ProviderExecutionLocation.LocalProcess => 0,
            ProviderExecutionLocation.LocalModel => 1,
            ProviderExecutionLocation.Cloud => 2,
            _ => throw new InvalidOperationException(
                $"Unknown execution location " +
                $"'{provider.Descriptor.ExecutionLocation}'.")
        };
    }

    private static void Reject(
        ICollection<ProviderRejection> rejections,
        ProviderRejectionCode code,
        string message)
    {
        rejections.Add(
            new ProviderRejection
            {
                Code = code,
                Message = message
            });
    }
}
