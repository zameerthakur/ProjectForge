using System.Collections.Concurrent;
using ProjectForge.Abstractions.Provisioning;

namespace ProjectForge.Core.Provisioning;

/// <summary>
/// Provisions missing dependencies concurrently and once per process.
/// </summary>
public sealed class RuntimeProvisioningCoordinator :
    IRuntimeProvisioningCoordinator
{
    private readonly Dictionary<string, IRuntimeProvisioner> _provisioners;
    private readonly ConcurrentDictionary<string, Lazy<Task>> _operations =
        new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, ProvisioningSnapshot> _snapshots =
        new(StringComparer.Ordinal);
    private readonly CancellationToken _lifetimeCancellationToken;

    public RuntimeProvisioningCoordinator(
        IEnumerable<IRuntimeProvisioner> provisioners,
        CancellationToken lifetimeCancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(provisioners);
        _lifetimeCancellationToken = lifetimeCancellationToken;
        var registeredProvisioners = provisioners.ToArray();

        var duplicate = registeredProvisioners
            .GroupBy(item => item.Requirement.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1);

        if (duplicate is not null)
        {
            throw new ArgumentException(
                $"More than one provisioner is registered for '{duplicate.Key}'.",
                nameof(provisioners));
        }

        _provisioners = registeredProvisioners.ToDictionary(
            item => item.Requirement.Id,
            StringComparer.Ordinal);
    }

    public IReadOnlyCollection<ProvisioningSnapshot> Snapshots =>
        _snapshots.Values
            .OrderBy(snapshot => snapshot.Requirement.Id, StringComparer.Ordinal)
            .ToArray();

    public Task EnsureReadyAsync(
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        return Task.WhenAll(
            _provisioners.Values.Select(
                item => EnsureReadyAsync(item, progress, cancellationToken)));
    }

    public Task EnsureRequirementReadyAsync(
        string requirementId,
        IProgress<ProvisioningProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementId);

        if (!_provisioners.TryGetValue(requirementId, out var provisioner))
        {
            throw new KeyNotFoundException(
                $"No runtime provisioner is registered for '{requirementId}'.");
        }

        return EnsureReadyAsync(provisioner, progress, cancellationToken);
    }

    public bool TryGetSnapshot(
        string requirementId,
        out ProvisioningSnapshot snapshot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(requirementId);
        return _snapshots.TryGetValue(requirementId, out snapshot!);
    }

    private async Task EnsureReadyAsync(
        IRuntimeProvisioner provisioner,
        IProgress<ProvisioningProgress>? progress,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

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

    private async Task EnsureProvisionedAsync(
        IRuntimeProvisioner provisioner,
        IProgress<ProvisioningProgress>? progress)
    {
        try
        {
            Report(progress, provisioner, ProvisioningStatus.Checking);
            if (await provisioner.IsReadyAsync(_lifetimeCancellationToken))
            {
                Report(progress, provisioner, ProvisioningStatus.Ready);
                return;
            }

            Report(progress, provisioner, ProvisioningStatus.Downloading);
            await provisioner.ProvisionAsync(
                new SnapshotProgress(this, progress),
                _lifetimeCancellationToken);

            Report(progress, provisioner, ProvisioningStatus.Verifying);
            if (!await provisioner.IsReadyAsync(_lifetimeCancellationToken))
            {
                throw new InvalidOperationException(
                    $"Provisioning '{provisioner.Requirement.DisplayName}' " +
                    "completed but verification failed.");
            }

            Report(progress, provisioner, ProvisioningStatus.Ready);
        }
        catch (OperationCanceledException)
            when (_lifetimeCancellationToken.IsCancellationRequested)
        {
            Report(
                progress,
                provisioner,
                ProvisioningStatus.Canceled,
                "Provisioning was canceled because the coordinator is stopping.");
            throw;
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

    private void Report(
        IProgress<ProvisioningProgress>? progress,
        IRuntimeProvisioner provisioner,
        ProvisioningStatus status,
        string? message = null)
    {
        var update = new ProvisioningProgress
        {
            Requirement = provisioner.Requirement,
            Status = status,
            Message = message
        };
        Capture(update, progress);
    }

    private void Capture(
        ProvisioningProgress update,
        IProgress<ProvisioningProgress>? progress)
    {
        var snapshotMessage = update.Status == ProvisioningStatus.Failed
            ? "Provisioning failed."
            : update.Message;
        _snapshots[update.Requirement.Id] = new ProvisioningSnapshot
        {
            Requirement = update.Requirement,
            Status = update.Status,
            Percentage = update.Percentage,
            Message = snapshotMessage
        };
        progress?.Report(update);
    }

    private sealed class SnapshotProgress(
        RuntimeProvisioningCoordinator coordinator,
        IProgress<ProvisioningProgress>? progress)
        : IProgress<ProvisioningProgress>
    {
        public void Report(ProvisioningProgress value)
        {
            ArgumentNullException.ThrowIfNull(value);
            coordinator.Capture(value, progress);
        }
    }
}
