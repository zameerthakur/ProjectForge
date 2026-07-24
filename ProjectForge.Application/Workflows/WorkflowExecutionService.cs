using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Application.Artifacts;

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
    private readonly IExecutionArtifactWriter _artifactWriter;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _executionTimeout;

    /// <summary>
    /// Initializes a workflow execution service.
    /// </summary>
    public WorkflowExecutionService(
        IWorkflowStore store,
        IExplainableResourceScheduler scheduler,
        IExecutionArtifactWriter artifactWriter,
        TimeProvider? timeProvider = null,
        TimeSpan? executionTimeout = null)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(scheduler);
        ArgumentNullException.ThrowIfNull(artifactWriter);

        _store = store;
        _scheduler = scheduler;
        _artifactWriter = artifactWriter;
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
        var startedAtUtc = _timeProvider.GetUtcNow();
        var running = await _store.TryStartExecutionAsync(
            workflowId,
            snapshot.Workflow.Version,
            MapSelection(selection),
            startedAtUtc,
            cancellationToken);

        if (!running.WasApplied)
        {
            return new WorkflowExecutionResult
            {
                WasExecutionStarted = false,
                Current = running.Current
            };
        }

        CapabilityExecutionResult? execution;
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
        catch (OperationCanceledException)
            when (executionTimeout.IsCancellationRequested)
        {
            var evidence = FailureEvidence(
                snapshot.Workflow.Request,
                provider,
                selection,
                WorkflowExecutionOutcome.TimedOut,
                startedAtUtc,
                $"Execution timed out after " +
                $"{_executionTimeout.TotalSeconds:g} seconds.");
            var failed = await CompleteAsync(
                running.Current,
                evidence,
                null,
                "workflow.execution-timed-out",
                $"Provider '{provider.Name}' exceeded the execution timeout.",
                evidence.ErrorMessage);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection
            };
        }
        catch (OperationCanceledException exception)
        {
            var errorMessage = string.IsNullOrWhiteSpace(exception.Message)
                ? "Provider execution was canceled."
                : exception.Message;
            var evidence = FailureEvidence(
                snapshot.Workflow.Request,
                provider,
                selection,
                WorkflowExecutionOutcome.Canceled,
                startedAtUtc,
                errorMessage);
            var failed = await CompleteAsync(
                running.Current,
                evidence,
                null,
                "workflow.execution-canceled",
                $"Provider '{provider.Name}' canceled during execution.",
                evidence.ErrorMessage);

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
            var evidence = FailureEvidence(
                snapshot.Workflow.Request,
                provider,
                selection,
                WorkflowExecutionOutcome.TimedOut,
                startedAtUtc,
                $"Execution timed out after " +
                $"{_executionTimeout.TotalSeconds:g} seconds.");
            var failed = await CompleteAsync(
                running.Current,
                evidence,
                null,
                "workflow.execution-timed-out",
                $"Provider '{provider.Name}' exceeded the execution timeout.",
                evidence.ErrorMessage);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection
            };
        }
        catch (Exception exception)
        {
            var evidence = FailureEvidence(
                snapshot.Workflow.Request,
                provider,
                selection,
                WorkflowExecutionOutcome.ProviderError,
                startedAtUtc,
                exception.Message);
            var failed = await CompleteAsync(
                running.Current,
                evidence,
                null,
                "workflow.execution-failed",
                $"Provider '{provider.Name}' threw during execution.",
                evidence.ErrorMessage);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection
            };
        }

        if (execution is null)
        {
            const string errorMessage =
                "The provider returned no execution result.";
            var evidence = FailureEvidence(
                snapshot.Workflow.Request,
                provider,
                selection,
                WorkflowExecutionOutcome.InvalidResult,
                startedAtUtc,
                errorMessage);
            var failed = await CompleteAsync(
                running.Current,
                evidence,
                null,
                "workflow.execution-invalid",
                $"Provider '{provider.Name}' returned an invalid result.",
                errorMessage);

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
            var evidence = FailureEvidence(
                snapshot.Workflow.Request,
                provider,
                selection,
                WorkflowExecutionOutcome.InvalidResult,
                startedAtUtc,
                correlationFailure);
            var failed = await CompleteAsync(
                running.Current,
                evidence,
                null,
                "workflow.execution-invalid",
                $"Provider '{provider.Name}' returned an invalid result.",
                evidence.ErrorMessage);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection,
                Execution = execution
            };
        }

        var executionEvidence = MapExecution(execution);
        if (!execution.IsSuccessful)
        {
            var terminal = await CompleteAsync(
                running.Current,
                executionEvidence,
                null,
                "workflow.execution-failed",
                $"Provider '{provider.Name}' reported execution failure.",
                execution.ErrorMessage ??
                    "The provider returned an unsuccessful result.");

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = terminal,
                Selection = selection,
                Execution = execution
            };
        }

        WorkflowArtifactPaths artifactPaths;

        try
        {
            var artifacts = await _artifactWriter.WriteAsync(
                workflowId,
                execution,
                CancellationToken.None);
            if (artifacts is null ||
                string.IsNullOrWhiteSpace(artifacts.MarkdownPath) ||
                string.IsNullOrWhiteSpace(artifacts.JsonPath) ||
                !Path.IsPathFullyQualified(artifacts.MarkdownPath) ||
                !Path.IsPathFullyQualified(artifacts.JsonPath))
            {
                throw new InvalidOperationException(
                    "The artifact writer returned invalid artifact paths.");
            }

            artifactPaths = new WorkflowArtifactPaths
            {
                MarkdownPath = artifacts.MarkdownPath,
                JsonPath = artifacts.JsonPath
            };
        }
        catch (Exception exception)
        {
            var errorMessage = string.IsNullOrWhiteSpace(exception.Message)
                ? "Execution artifacts could not be published."
                : $"Execution artifacts could not be published: " +
                  exception.Message;
            var evidence = executionEvidence with
            {
                Outcome = WorkflowExecutionOutcome.ArtifactError,
                ErrorMessage = errorMessage
            };
            var failed = await CompleteAsync(
                running.Current,
                evidence,
                null,
                "workflow.execution-artifact-failed",
                $"Artifacts for provider '{provider.Name}' could not be " +
                "published.",
                errorMessage);

            return new WorkflowExecutionResult
            {
                WasExecutionStarted = true,
                Current = failed,
                Selection = selection,
                Execution = execution
            };
        }

        var succeeded = await CompleteAsync(
            running.Current,
            executionEvidence,
            artifactPaths,
            "workflow.execution-succeeded",
            $"Provider '{provider.Name}' completed execution and published " +
            "artifacts.",
            null);

        return new WorkflowExecutionResult
        {
            WasExecutionStarted = true,
            Current = succeeded,
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

        if (execution.CompletedAtUtc < execution.StartedAtUtc)
        {
            return "The provider result completed before it started.";
        }

        if (execution.EstimatedCost < decimal.Zero)
        {
            return "The provider result contains a negative execution cost.";
        }

        if (execution.Metadata is null ||
            execution.Metadata.Any(
                item => string.IsNullOrWhiteSpace(item.Key) ||
                        item.Value is null))
        {
            return "The provider result contains invalid execution metadata.";
        }

        return null;
    }

    private static WorkflowProviderSelectionEvidence MapSelection(
        ProviderSelectionResult selection) =>
        new()
        {
            SelectedProviderName = selection.SelectedProvider.Name,
            EstimatedCost = selection.EstimatedCost,
            Candidates = Array.AsReadOnly(
                selection.Evaluations
                    .Select(MapCandidate)
                    .ToArray())
        };

    private static WorkflowProviderCandidateEvidence MapCandidate(
        ProviderEvaluation candidate) =>
        new()
        {
            ProviderName = candidate.Provider.Name,
            EstimatedCost = candidate.EstimatedCost,
            Rejections = Array.AsReadOnly(
                candidate.Rejections
                    .Select(
                        rejection =>
                            new WorkflowProviderRejectionEvidence
                            {
                                Code = rejection.Code,
                                Message = rejection.Message
                            })
                    .ToArray())
        };

    private static WorkflowExecutionEvidence MapExecution(
        CapabilityExecutionResult execution) =>
        new()
        {
            RequestId = execution.RequestId,
            ProviderName = execution.ProviderName,
            Outcome = execution.IsSuccessful
                ? WorkflowExecutionOutcome.Succeeded
                : WorkflowExecutionOutcome.Failed,
            Summary = execution.Summary,
            Output = execution.Output,
            ErrorMessage = execution.IsSuccessful
                ? execution.ErrorMessage
                : execution.ErrorMessage ??
                  "The provider returned an unsuccessful result.",
            StartedAtUtc = execution.StartedAtUtc,
            CompletedAtUtc = execution.CompletedAtUtc,
            EstimatedCost = execution.EstimatedCost,
            Metadata = new Dictionary<string, string>(execution.Metadata)
        };

    private WorkflowExecutionEvidence FailureEvidence(
        CapabilityExecutionRequest request,
        ICapabilityProvider provider,
        ProviderSelectionResult selection,
        WorkflowExecutionOutcome outcome,
        DateTimeOffset startedAtUtc,
        string? errorMessage) =>
        new()
        {
            RequestId = request.RequestId,
            ProviderName = provider.Name,
            Outcome = outcome,
            ErrorMessage = string.IsNullOrWhiteSpace(errorMessage)
                ? "Provider execution failed."
                : errorMessage,
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = _timeProvider.GetUtcNow(),
            EstimatedCost = selection.EstimatedCost
        };

    private static WorkflowExecutionResult NotExecuted(
        WorkflowSnapshot snapshot) =>
        new()
        {
            WasExecutionStarted = false,
            Current = snapshot
        };

    private async Task<WorkflowSnapshot> CompleteAsync(
        WorkflowSnapshot snapshot,
        WorkflowExecutionEvidence execution,
        WorkflowArtifactPaths? artifacts,
        string eventType,
        string message,
        string? failureMessage)
    {
        var status = execution.Outcome == WorkflowExecutionOutcome.Succeeded
            ? WorkflowStatus.Succeeded
            : WorkflowStatus.Failed;
        var completed = await _store.TryCompleteExecutionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            status,
            execution,
            artifacts,
            eventType,
            message,
            _timeProvider.GetUtcNow(),
            failureMessage,
            CancellationToken.None);

        return completed.Current;
    }
}
