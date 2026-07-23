using System.Collections.Concurrent;
using ProjectForge.Abstractions.Provisioning;

namespace ProjectForge.Core.Provisioning;

/// <summary>
/// Provisions missing dependencies concurrently and once per process.
/// </summary>
public sealed class RuntimeProvisioningCoordinator :
    IRuntimeProvisioningCoordinator
{
    private readonly IReadOnlyCollection<IRuntimeProvisioner> _provisioners;
    private readonly ConcurrentDictionary<string, Lazy<Task>> _operations =
        new(StringComparer.Ordinal);

    public RuntimeProvisioningCoordinator(
        IEnumerable<IRuntimeProvisioner> provisioners)
    {
        ArgumentNullException.ThrowIfNull(provisioners);
        _provisioners = provisioners.ToArray();

        var duplicate = _provisioners
            .GroupBy(item => item.Requirement.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"More than one provisioner is registered for '{duplicate.Key}'.",
                nameof(provisioners));
        }
    }

    public Task EnsureReadyAsync(
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.WhenAll(
            _provisioners.Select(
                item => EnsureReadyAsync(item, progress, cancellationToken)));
    }

    private async Task EnsureReadyAsync(
        IRuntimeProvisioner provisioner,
        IProgress<ProvisioningProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Report(progress, provisioner, ProvisioningStatus.Checking);

        var operation = _operations.GetOrAdd(
            provisioner.Requirement.Id,
            _ => new Lazy<Task>(
                () => EnsureProvisionedAsync(provisioner, progress),
                LazyThreadSafetyMode.ExecutionAndPublication));

        var task = operation.Value;
        _ = task.ContinueWith(
            _ =>
            {
                _operations.TryRemove(
                    new KeyValuePair<string, Lazy<Task>>(
                        provisioner.Requirement.Id,
                        operation));
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        await task.WaitAsync(cancellationToken);
    }

    private static async Task EnsureProvisionedAsync(
        IRuntimeProvisioner provisioner,
        IProgress<ProvisioningProgress>? progress)
    {
        try
        {
            if (await provisioner.IsReadyAsync())
            {
                Report(progress, provisioner, ProvisioningStatus.Ready);
                return;
            }

            Report(progress, provisioner, ProvisioningStatus.Downloading);
            await provisioner.ProvisionAsync(progress);

            Report(progress, provisioner, ProvisioningStatus.Verifying);
            if (!await provisioner.IsReadyAsync())
            {
                throw new InvalidOperationException(
                    $"Provisioning '{provisioner.Requirement.DisplayName}' " +
                    "completed but verification failed.");
            }

            Report(progress, provisioner, ProvisioningStatus.Ready);
        }
        catch (Exception exception)
        {
            Report(
                progress,
                provisioner,
                ProvisioningStatus.Failed,
                exception.Message);
            throw;
        }
    }

    private static void Report(
        IProgress<ProvisioningProgress>? progress,
        IRuntimeProvisioner provisioner,
        ProvisioningStatus status,
        string? message = null)
    {
        progress?.Report(
            new ProvisioningProgress
            {
                Requirement = provisioner.Requirement,
                Status = status,
                Message = message
            });
    }
}
