using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Application.Workflows;

/// <summary>
/// Coordinates durable workflow transitions around provider execution.
/// </summary>
public sealed class WorkflowExecutionService : IWorkflowExecutionService
{
    private static readonly TimeSpan DefaultExecutionTimeout =
        TimeSpan.FromMinutes(5);

    private readonly IWorkflowStore _store;
    private readonly IExplainableResourceScheduler _scheduler;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _executionTimeout;

    /// <summary>
    /// Initializes a workflow execution service.
    /// </summary>
    public WorkflowExecutionService(
        IWorkflowStore store,
        IExplainableResourceScheduler scheduler,
        TimeProvider? timeProvider = null,
        TimeSpan? executionTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);

        _store = store;
        _scheduler = scheduler;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _executionTimeout = executionTimeout ?? DefaultExecutionTimeout;
        if (_executionTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executionTimeout),
                _executionTimeout,
                "The provider execution timeout must be positive.");
        }
    }

    /// <inheritdoc />
    public async Task<WorkflowExecutionResult> ResumeAsync(
        Guid workflowId,
        long expectedVersion,
        CancellationToken cancellationToken = default)
    {
        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "A workflow identifier is required.",
                nameof(workflowId));
        }

        if (expectedVersion < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(expectedVersion),
                expectedVersion,
                "The expected workflow version must be positive.");
        }

        var snapshot = await _store.GetAsync(workflowId, cancellationToken)
            ?? throw new KeyNotFoundException(
                $"Workflow '{workflowId}' does not exist.");

        if (snapshot.Workflow.Version != expectedVersion ||
            !CanResume(snapshot.Workflow.Status))
        {
            return NotExecuted(snapshot);
        }

        if (snapshot.Workflow.Status == WorkflowStatus.Approved)
        {
            var queued = await _store.TryTransitionAsync(
                workflowId,
                snapshot.Workflow.Version,
                WorkflowStatus.Approved,
                WorkflowStatus.Queued,
                "workflow.execution-queued",
                "Approved workflow queued for provider selection.",
                _timeProvider.GetUtcNow(),
                cancellationToken: cancellationToken);

            snapshot = queued.Current;
            if (!queued.WasApplied &&
                snapshot.Workflow.Status != WorkflowStatus.Queued)
            {
                return NotExecuted(snapshot);
            }
        }

        var selection = await _scheduler.SelectProviderWithEvidenceAsync(
            snapshot.Workflow.Request,
            cancellationToken);
        var provider = selection.SelectedProvider;
        var running = await _store.TryTransitionAsync(
            workflowId,
            snapshot.Workflow.Version,
            WorkflowStatus.Queued,
            WorkflowStatus.Running,
            "workflow.execution-started",
            $"Provider '{provider.Name}' selected at estimated cost " +
            $"{selection.EstimatedCost}.",
            _timeProvider.GetUtcNow(),
            cancellationToken: cancellationToken);

        if (!running.WasApplied)
        {
            return new WorkflowExecutionResult
            {
                WasExecutionStarted = false,
                Current = running.Current,
                Selection = selection
            };
        }

        CapabilityExecutionResult execution;
        using var executionTimeout = new CancellationTokenSource(
            _executionTimeout);

        try
        {
            execution = await provider
                .ExecuteAsync(
                    snapshot.Workflow.Request,
                    executionTimeout.Token)
                .WaitAsync(_executionTimeout, CancellationToken.None);
        }
        catch (OperationCanceledException exception)
        {
            var failed = await CompleteAsync(
                running.Current,
                WorkflowStatus.Failed,
                "workflow.execution-failed",
                $"Provider '{provider.Name}' canceled during execution.",
                exception.Message,
                CancellationToken.None);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection
            };
        }
        catch (TimeoutException)
        {
            executionTimeout.Cancel();
            var failed = await CompleteAsync(
                running.Current,
                WorkflowStatus.Failed,
                "workflow.execution-timed-out",
                $"Provider '{provider.Name}' exceeded the execution timeout.",
                $"Execution timed out after " +
                $"{_executionTimeout.TotalSeconds:g} seconds.",
                CancellationToken.None);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection
            };
        }
        catch (Exception exception)
        {
            var failed = await CompleteAsync(
                running.Current,
                WorkflowStatus.Failed,
                "workflow.execution-failed",
                $"Provider '{provider.Name}' threw during execution.",
                exception.Message,
                CancellationToken.None);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection
            };
        }

        var correlationFailure = ValidateExecution(
            snapshot.Workflow.Request,
            provider,
            execution);
        if (correlationFailure is not null)
        {
            var failed = await CompleteAsync(
                running.Current,
                WorkflowStatus.Failed,
                "workflow.execution-invalid",
                $"Provider '{provider.Name}' returned an invalid result.",
                correlationFailure,
                CancellationToken.None);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection,
                Execution = execution
            };
        }

        var terminal = execution.IsSuccessful
            ? await CompleteAsync(
                running.Current,
                WorkflowStatus.Succeeded,
                "workflow.execution-succeeded",
                $"Provider '{provider.Name}' completed execution.",
                null,
                CancellationToken.None)
            : await CompleteAsync(
                running.Current,
                WorkflowStatus.Failed,
                "workflow.execution-failed",
                $"Provider '{provider.Name}' reported execution failure.",
                execution.ErrorMessage ??
                    "The provider returned an unsuccessful result.",
                CancellationToken.None);

        return new WorkflowExecutionResult
        {
            WasExecutionStarted = true,
            Current = terminal,
            Selection = selection,
            Execution = execution
        };
    }

    private static bool CanResume(WorkflowStatus status) =>
        status is WorkflowStatus.Approved or WorkflowStatus.Queued;

    private static string? ValidateExecution(
        CapabilityExecutionRequest request,
        ICapabilityProvider provider,
        CapabilityExecutionResult execution)
    {
        if (execution.RequestId != request.RequestId)
        {
            return "The provider result does not match the execution request.";
        }

        if (!string.Equals(
                execution.ProviderName,
                provider.Name,
                StringComparison.Ordinal))
        {
            return "The provider result identifies a different provider.";
        }

        return null;
    }

    private static WorkflowExecutionResult NotExecuted(
        WorkflowSnapshot snapshot) =>
        new()
        {
            WasExecutionStarted = false,
            Current = snapshot
        };

    private async Task<WorkflowSnapshot> CompleteAsync(
        WorkflowSnapshot snapshot,
        WorkflowStatus status,
        string eventType,
        string message,
        string? failureMessage,
        CancellationToken cancellationToken)
    {
        var completed = await _store.TryTransitionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            WorkflowStatus.Running,
            status,
            eventType,
            message,
            _timeProvider.GetUtcNow(),
            failureMessage,
            cancellationToken);

        return completed.Current;
    }
}
