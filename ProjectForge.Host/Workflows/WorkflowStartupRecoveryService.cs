using ProjectForge.Application.Workflows;

namespace ProjectForge.Host.Workflows;

/// <summary>
/// Reconciles workflow executions that were left running by a prior host
/// lifetime.
/// </summary>
internal sealed class WorkflowStartupRecoveryService : IHostedService
{
    internal const string RecoveryReason =
        "The prior host stopped while provider execution was running. " +
        "Operator reconciliation is required before further execution.";

    private readonly IWorkflowStore _store;
    private readonly TimeProvider _timeProvider;

    /// <summary>
    /// Initializes the startup recovery service.
    /// </summary>
    public WorkflowStartupRecoveryService(
        IWorkflowStore store,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(timeProvider);

        _store = store;
        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        await _store.ReconcileInterruptedExecutionsAsync(
            _timeProvider.GetUtcNow(),
            RecoveryReason,
            cancellationToken);
    }

    /// <inheritdoc />
    public Task StopAsync(CancellationToken cancellationToken) =>
        Task.CompletedTask;
}
