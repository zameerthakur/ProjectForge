using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Application.Artifacts;
using ProjectForge.Application.Workflows;

namespace ProjectForge.Application.Tests.Workflows;

public sealed class WorkflowExecutionServiceTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 14, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task ExecutesApprovedWorkflowThroughOptimisticTransitions()
    {
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Approved, 2));
        var provider = new StubProvider();
        var scheduler = new StubScheduler(provider);
        var service = Service(store, scheduler);

        var result = await service.ResumeAsync(WorkflowId, 2);

        Assert.True(result.WasExecutionStarted);
        Assert.Equal(WorkflowStatus.Succeeded, result.Current.Workflow.Status);
        Assert.Equal(5, result.Current.Workflow.Version);
        Assert.Same(provider.Result, result.Execution);
        Assert.Equal(
            WorkflowExecutionOutcome.Succeeded,
            result.Current.Execution?.Outcome);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("artifacts", "result.md")),
            result.Current.Artifacts?.MarkdownPath);
        Assert.Equal(
            Path.GetFullPath(Path.Combine("artifacts", "result.json")),
            result.Current.Artifacts?.JsonPath);
        Assert.Equal(
            "stub-provider",
            result.Current.ProviderSelection?.SelectedProviderName);
        var candidate = Assert.Single(
            result.Current.ProviderSelection?.Candidates ??
            Array.Empty<WorkflowProviderCandidateEvidence>());
        Assert.Equal("stub-provider", candidate.ProviderName);
        Assert.Equal(1.25m, candidate.EstimatedCost);
        Assert.Equal(1, provider.ExecutionCount);
        Assert.Equal(1, scheduler.SelectionCount);
        Assert.Collection(
            store.Transitions,
            transition =>
            {
                Assert.Equal(WorkflowStatus.Approved, transition.ExpectedStatus);
                Assert.Equal(WorkflowStatus.Queued, transition.NextStatus);
            },
            transition =>
            {
                Assert.Equal(WorkflowStatus.Queued, transition.ExpectedStatus);
                Assert.Equal(WorkflowStatus.Running, transition.NextStatus);
            },
            transition =>
            {
                Assert.Equal(WorkflowStatus.Running, transition.ExpectedStatus);
                Assert.Equal(WorkflowStatus.Succeeded, transition.NextStatus);
            });
    }

    [Theory]
    [InlineData(WorkflowStatus.PendingApproval)]
    [InlineData(WorkflowStatus.Running)]
    [InlineData(WorkflowStatus.Succeeded)]
    [InlineData(WorkflowStatus.Failed)]
    [InlineData(WorkflowStatus.Rejected)]
    public async Task DoesNotExecuteWorkflowThatCannotBeResumed(
        WorkflowStatus status)
    {
        var store = new InMemoryWorkflowStore(Snapshot(status, 2));
        var provider = new StubProvider();
        var scheduler = new StubScheduler(provider);

        var result = await Service(store, scheduler).ResumeAsync(WorkflowId, 2);

        Assert.False(result.WasExecutionStarted);
        Assert.Equal(status, result.Current.Workflow.Status);
        Assert.Equal(0, scheduler.SelectionCount);
        Assert.Equal(0, provider.ExecutionCount);
        Assert.Empty(store.Transitions);
    }

    [Fact]
    public async Task DoesNotExecuteWhenExpectedVersionIsStale()
    {
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Approved, 3));
        var provider = new StubProvider();
        var scheduler = new StubScheduler(provider);

        var result = await Service(store, scheduler).ResumeAsync(WorkflowId, 2);

        Assert.False(result.WasExecutionStarted);
        Assert.Equal(3, result.Current.Workflow.Version);
        Assert.Equal(0, scheduler.SelectionCount);
        Assert.Equal(0, provider.ExecutionCount);
    }

    [Fact]
    public async Task DoesNotRetryPersistedRunningWorkflowAfterRestart()
    {
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Running, 4));
        var provider = new StubProvider();
        var scheduler = new StubScheduler(provider);

        var result = await Service(store, scheduler).ResumeAsync(WorkflowId, 4);

        Assert.False(result.WasExecutionStarted);
        Assert.Equal(WorkflowStatus.Running, result.Current.Workflow.Status);
        Assert.Equal(4, result.Current.Workflow.Version);
        Assert.Equal(0, scheduler.SelectionCount);
        Assert.Equal(0, provider.ExecutionCount);
    }

    [Fact]
    public async Task RepeatedConcurrentResumeExecutesProviderOnce()
    {
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));
        var provider = new StubProvider();
        var scheduler = new StubScheduler(
            provider,
            synchronizeTwoSelections: true);
        var service = Service(store, scheduler);

        var results = await Task.WhenAll(
            service.ResumeAsync(WorkflowId, 3),
            service.ResumeAsync(WorkflowId, 3));

        Assert.Equal(1, provider.ExecutionCount);
        Assert.Equal(2, scheduler.SelectionCount);
        Assert.Single(results, result => result.WasExecutionStarted);
        var loser = Assert.Single(
            results,
            result => !result.WasExecutionStarted);
        Assert.Null(loser.Selection);
        Assert.Equal(WorkflowStatus.Succeeded, store.Current.Workflow.Status);
    }

    [Fact]
    public async Task ResumesWorkflowAlreadyQueuedBeforeRestart()
    {
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));
        var provider = new StubProvider();
        var scheduler = new StubScheduler(provider);

        var result = await Service(store, scheduler).ResumeAsync(WorkflowId, 3);

        Assert.True(result.WasExecutionStarted);
        Assert.Equal(WorkflowStatus.Succeeded, result.Current.Workflow.Status);
        Assert.Equal(5, result.Current.Workflow.Version);
        Assert.DoesNotContain(
            store.Transitions,
            transition => transition.NextStatus == WorkflowStatus.Queued);
    }

    [Fact]
    public async Task LeavesWorkflowQueuedWhenProviderSelectionFails()
    {
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Approved, 2));
        var scheduler = new StubScheduler(
            new StubProvider(),
            new ProviderSelectionException(
                "No eligible provider.",
                Array.Empty<ProviderEvaluation>()));
        var service = Service(store, scheduler);

        var exception = await Assert.ThrowsAsync<ProviderSelectionException>(
            () => service.ResumeAsync(WorkflowId, 2));

        Assert.Equal("No eligible provider.", exception.Message);
        Assert.Equal(WorkflowStatus.Queued, store.Current.Workflow.Status);
        Assert.Equal(3, store.Current.Workflow.Version);
    }

    [Fact]
    public async Task RecordsUnsuccessfulProviderResultAsFailed()
    {
        var artifactWriter = new StubArtifactWriter();
        var provider = new StubProvider
        {
            Result = Result(isSuccessful: false, errorMessage: "Execution failed.")
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Approved, 2));

        var result = await Service(
                store,
                new StubScheduler(provider),
                artifactWriter: artifactWriter)
            .ResumeAsync(WorkflowId, 2);

        Assert.True(result.WasExecutionStarted);
        Assert.Same(provider.Result, result.Execution);
        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Equal("Execution failed.", result.Current.Workflow.FailureMessage);
        Assert.Equal(
            WorkflowExecutionOutcome.Failed,
            result.Current.Execution?.Outcome);
        Assert.Null(result.Current.Artifacts);
        Assert.Equal(0, artifactWriter.WriteCount);
    }

    [Fact]
    public async Task RecordsProviderExceptionAsFailed()
    {
        var provider = new StubProvider
        {
            Exception = new InvalidOperationException("Provider crashed.")
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Approved, 2));

        var result = await Service(store, new StubScheduler(provider))
            .ResumeAsync(WorkflowId, 2);

        Assert.True(result.WasExecutionStarted);
        Assert.Null(result.Execution);
        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Equal("Provider crashed.", result.Current.Workflow.FailureMessage);
        Assert.Equal(
            WorkflowExecutionOutcome.ProviderError,
            result.Current.Execution?.Outcome);
        Assert.Null(result.Current.Artifacts);
    }

    [Fact]
    public async Task ProviderCancellationAfterClaimIsRecordedAsFailed()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new StubProvider
        {
            Exception = new OperationCanceledException(cancellation.Token),
            OnExecute = cancellation.Cancel
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));

        var result = await Service(store, new StubScheduler(provider))
            .ResumeAsync(WorkflowId, 3, cancellation.Token);

        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Equal(5, store.Current.Workflow.Version);
        Assert.Equal(1, provider.ExecutionCount);
        Assert.Equal(
            WorkflowExecutionOutcome.Canceled,
            result.Current.Execution?.Outcome);
        Assert.Null(result.Current.Artifacts);
    }

    [Fact]
    public async Task PersistsSuccessfulCompletionAfterCallerCancels()
    {
        using var cancellation = new CancellationTokenSource();
        var provider = new StubProvider
        {
            OnExecute = cancellation.Cancel
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));

        var result = await Service(store, new StubScheduler(provider))
            .ResumeAsync(WorkflowId, 3, cancellation.Token);

        Assert.True(cancellation.IsCancellationRequested);
        Assert.True(result.WasExecutionStarted);
        Assert.Equal(WorkflowStatus.Succeeded, result.Current.Workflow.Status);
    }

    [Fact]
    public async Task RejectsResultForDifferentRequest()
    {
        var artifactWriter = new StubArtifactWriter();
        var provider = new StubProvider
        {
            Result = Result(requestId: Guid.NewGuid())
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));

        var result = await Service(
                store,
                new StubScheduler(provider),
                artifactWriter: artifactWriter)
            .ResumeAsync(WorkflowId, 3);

        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Contains(
            "does not match",
            result.Current.Workflow.FailureMessage);
        Assert.Equal(
            WorkflowExecutionOutcome.InvalidResult,
            result.Current.Execution?.Outcome);
        Assert.Null(result.Current.Artifacts);
        Assert.Equal(0, artifactWriter.WriteCount);
    }

    [Fact]
    public async Task RecordsMissingProviderResultAsInvalid()
    {
        var provider = new StubProvider
        {
            Result = null!
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));

        var result = await Service(store, new StubScheduler(provider))
            .ResumeAsync(WorkflowId, 3);

        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Equal(
            WorkflowExecutionOutcome.InvalidResult,
            result.Current.Execution?.Outcome);
        Assert.Contains(
            "no execution result",
            result.Current.Execution?.ErrorMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Current.Artifacts);
    }

    [Fact]
    public async Task RecordsExecutionTimeoutAsFailed()
    {
        var provider = new StubProvider
        {
            ExecutionDelay = Timeout.InfiniteTimeSpan
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));
        var service = Service(
            store,
            new StubScheduler(provider),
            TimeSpan.FromMilliseconds(50));

        var result = await service.ResumeAsync(WorkflowId, 3);

        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Equal(5, result.Current.Workflow.Version);
        Assert.Equal(
            WorkflowExecutionOutcome.TimedOut,
            result.Current.Execution?.Outcome);
        Assert.Null(result.Current.Artifacts);
    }

    [Fact]
    public async Task RecordsArtifactPublicationFailureWithoutArtifactPaths()
    {
        var artifactWriter = new StubArtifactWriter
        {
            Exception = new IOException("Disk unavailable.")
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));

        var result = await Service(
                store,
                new StubScheduler(new StubProvider()),
                artifactWriter: artifactWriter)
            .ResumeAsync(WorkflowId, 3);

        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Equal(
            WorkflowExecutionOutcome.ArtifactError,
            result.Current.Execution?.Outcome);
        Assert.Contains(
            "artifacts could not be published",
            result.Current.Workflow.FailureMessage,
            StringComparison.OrdinalIgnoreCase);
        Assert.Null(result.Current.Artifacts);
        Assert.Equal(1, artifactWriter.WriteCount);
    }

    [Fact]
    public async Task RecordsInvalidArtifactWriterResultAsArtifactFailure()
    {
        var artifactWriter = new StubArtifactWriter
        {
            Result = new ArtifactWriteResult
            {
                MarkdownPath = "relative.md",
                JsonPath = "relative.json"
            }
        };
        var store = new InMemoryWorkflowStore(Snapshot(WorkflowStatus.Queued, 3));

        var result = await Service(
                store,
                new StubScheduler(new StubProvider()),
                artifactWriter: artifactWriter)
            .ResumeAsync(WorkflowId, 3);

        Assert.Equal(WorkflowStatus.Failed, result.Current.Workflow.Status);
        Assert.Equal(
            WorkflowExecutionOutcome.ArtifactError,
            result.Current.Execution?.Outcome);
        Assert.Null(result.Current.Artifacts);
    }

    private static readonly Guid WorkflowId =
        Guid.Parse("a4fbf54d-97f5-4eed-88d5-4bf528b45ddd");

    private static readonly Guid RequestId =
        Guid.Parse("8890337c-1eb6-4b51-88a3-d37c45bec0a1");

    private static WorkflowExecutionService Service(
        IWorkflowStore store,
        IExplainableResourceScheduler scheduler,
        TimeSpan? executionTimeout = null,
        IExecutionArtifactWriter? artifactWriter = null) =>
        new(
            store,
            scheduler,
            artifactWriter ?? new StubArtifactWriter(),
            new FixedTimeProvider(Now),
            executionTimeout);

    private static WorkflowSnapshot Snapshot(
        WorkflowStatus status,
        long version) =>
        new()
        {
            Workflow = new WorkflowRecord
            {
                Id = WorkflowId,
                Request = Request(),
                Status = status,
                Version = version,
                CreatedAtUtc = Now.AddMinutes(-5),
                UpdatedAtUtc = Now.AddMinutes(-1)
            }
        };

    private static CapabilityExecutionRequest Request() =>
        new()
        {
            RequestId = RequestId,
            WorkflowId = WorkflowId.ToString("D"),
            TaskId = "task-1",
            TaskName = "Execute workflow",
            Instruction = "Run the approved capability.",
            Requirement = new CapabilityRequirement
            {
                Capability = EngineeringCapability.Coding
            }
        };

    private static CapabilityExecutionResult Result(
        bool isSuccessful = true,
        string? errorMessage = null,
        Guid? requestId = null) =>
        new()
        {
            RequestId = requestId ?? RequestId,
            ProviderName = "stub-provider",
            IsSuccessful = isSuccessful,
            ErrorMessage = errorMessage,
            StartedAtUtc = Now,
            CompletedAtUtc = Now.AddSeconds(1),
            EstimatedCost = 1.25m
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class StubScheduler(
        StubProvider provider,
        Exception? exception = null,
        bool synchronizeTwoSelections = false) : IExplainableResourceScheduler
    {
        private int _selectionCount;
        private readonly TaskCompletionSource _twoSelections =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public int SelectionCount => _selectionCount;

        public async Task<ProviderSelectionResult> SelectProviderWithEvidenceAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var selectionCount = Interlocked.Increment(ref _selectionCount);

            if (synchronizeTwoSelections)
            {
                if (selectionCount == 2)
                {
                    _twoSelections.TrySetResult();
                }

                await _twoSelections.Task.WaitAsync(cancellationToken);
            }

            if (exception is not null)
            {
                throw exception;
            }

            return new ProviderSelectionResult
            {
                SelectedProvider = provider,
                EstimatedCost = 1.25m,
                Evaluations =
                    new[]
                    {
                        new ProviderEvaluation
                        {
                            Provider = provider,
                            EstimatedCost = 1.25m
                        }
                    }
            };
        }
    }

    private sealed class StubArtifactWriter : IExecutionArtifactWriter
    {
        private int _writeCount;

        public Exception? Exception { get; init; }

        public ArtifactWriteResult Result { get; init; } =
            new()
            {
                MarkdownPath =
                    Path.GetFullPath(Path.Combine("artifacts", "result.md")),
                JsonPath =
                    Path.GetFullPath(Path.Combine("artifacts", "result.json"))
            };

        public int WriteCount => _writeCount;

        public Task<ArtifactWriteResult> WriteAsync(
            Guid workflowId,
            CapabilityExecutionResult result,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _writeCount);

            if (Exception is not null)
            {
                return Task.FromException<ArtifactWriteResult>(Exception);
            }

            return Task.FromResult(Result);
        }
    }

    private sealed class StubProvider : ISchedulableCapabilityProvider
    {
        private int _executionCount;

        public string Name => "stub-provider";

        public IReadOnlyCollection<EngineeringCapability>
            SupportedCapabilities
        { get; } =
                new[] { EngineeringCapability.Coding };

        public ProviderDescriptor Descriptor { get; } =
            new()
            {
                Name = "stub-provider",
                ExecutionLocation = ProviderExecutionLocation.LocalProcess
            };

        public CapabilityExecutionResult Result { get; set; } =
            WorkflowExecutionServiceTests.Result();

        public Exception? Exception { get; init; }

        public Action? OnExecute { get; init; }

        public TimeSpan ExecutionDelay { get; init; }

        public int ExecutionCount => _executionCount;

        public Task<bool> CanExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public async Task<CapabilityExecutionResult> ExecuteAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _executionCount);
            OnExecute?.Invoke();

            if (ExecutionDelay != default)
            {
                await Task.Delay(ExecutionDelay, cancellationToken);
            }

            if (Exception is not null)
            {
                throw Exception;
            }

            return Result;
        }

        public Task<ProviderHealthReport> CheckHealthAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult(
                new ProviderHealthReport
                {
                    ProviderName = Name,
                    IsHealthy = true
                });

        public Task<decimal> EstimateCostAsync(
            CapabilityExecutionRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(1.25m);
    }

    private sealed class InMemoryWorkflowStore(WorkflowSnapshot snapshot)
        : IWorkflowStore
    {
        private readonly object _sync = new();

        public WorkflowSnapshot Current { get; private set; } = snapshot;

        public List<TransitionRecord> Transitions { get; } = new();

        public Task CreateAsync(
            WorkflowSnapshot workflow,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowSnapshot?> GetAsync(
            Guid workflowId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                return Task.FromResult<WorkflowSnapshot?>(
                    workflowId == Current.Workflow.Id ? Current : null);
            }
        }

        public Task<IReadOnlyCollection<WorkflowSnapshot>> ListAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowMutationResult> TryRecordDecisionAsync(
            Guid workflowId,
            long expectedVersion,
            ApprovalDecision decision,
            string? decidedBy,
            DateTimeOffset decidedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowMutationResult> TryStartExecutionAsync(
            Guid workflowId,
            long expectedVersion,
            WorkflowProviderSelectionEvidence providerSelection,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (!Matches(
                        workflowId,
                        expectedVersion,
                        WorkflowStatus.Queued))
                {
                    return NotApplied();
                }

                return Applied(
                    WorkflowStatus.Running,
                    startedAtUtc,
                    providerSelection: providerSelection);
            }
        }

        public Task<WorkflowMutationResult> TryCompleteExecutionAsync(
            Guid workflowId,
            long expectedVersion,
            WorkflowStatus terminalStatus,
            WorkflowExecutionEvidence execution,
            WorkflowArtifactPaths? artifacts,
            string auditEventType,
            string auditMessage,
            DateTimeOffset occurredAtUtc,
            string? failureMessage = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (!Matches(
                        workflowId,
                        expectedVersion,
                        WorkflowStatus.Running))
                {
                    return NotApplied();
                }

                return Applied(
                    terminalStatus,
                    occurredAtUtc,
                    failureMessage,
                    execution: execution,
                    artifacts: artifacts,
                    eventType: auditEventType,
                    message: auditMessage);
            }
        }

        public Task<int> ReconcileInterruptedExecutionsAsync(
            DateTimeOffset detectedAtUtc,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowMutationResult> TryTransitionAsync(
            Guid workflowId,
            long expectedVersion,
            WorkflowStatus expectedStatus,
            WorkflowStatus nextStatus,
            string auditEventType,
            string auditMessage,
            DateTimeOffset occurredAtUtc,
            string? failureMessage = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            lock (_sync)
            {
                if (!Matches(workflowId, expectedVersion, expectedStatus))
                {
                    return NotApplied();
                }

                return Applied(
                    nextStatus,
                    occurredAtUtc,
                    failureMessage,
                    eventType: auditEventType,
                    message: auditMessage);
            }
        }

        private bool Matches(
            Guid workflowId,
            long expectedVersion,
            WorkflowStatus expectedStatus) =>
            workflowId == Current.Workflow.Id &&
            expectedVersion == Current.Workflow.Version &&
            expectedStatus == Current.Workflow.Status;

        private Task<WorkflowMutationResult> Applied(
            WorkflowStatus nextStatus,
            DateTimeOffset occurredAtUtc,
            string? failureMessage = null,
            WorkflowProviderSelectionEvidence? providerSelection = null,
            WorkflowExecutionEvidence? execution = null,
            WorkflowArtifactPaths? artifacts = null,
            string eventType = "workflow.execution-started",
            string message = "Execution state changed.")
        {
            Transitions.Add(
                new TransitionRecord(
                    Current.Workflow.Status,
                    nextStatus,
                    message));
            var auditEvents = Current.AuditEvents.Append(
                new WorkflowAuditEvent
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = Current.Workflow.Id,
                    EventType = eventType,
                    Message = message,
                    OccurredAtUtc = occurredAtUtc
                }).ToArray();
            Current = new WorkflowSnapshot
            {
                Workflow = new WorkflowRecord
                {
                    Id = Current.Workflow.Id,
                    Request = Current.Workflow.Request,
                    Status = nextStatus,
                    Version = Current.Workflow.Version + 1,
                    CreatedAtUtc = Current.Workflow.CreatedAtUtc,
                    UpdatedAtUtc = occurredAtUtc,
                    FailureMessage = failureMessage
                },
                Approval = Current.Approval,
                AuditEvents = auditEvents,
                ProviderSelection =
                    providerSelection ?? Current.ProviderSelection,
                Execution = execution ?? Current.Execution,
                Artifacts = artifacts,
                Recovery = Current.Recovery
            };

            return Task.FromResult(
                new WorkflowMutationResult
                {
                    WasApplied = true,
                    Current = Current
                });
        }

        private Task<WorkflowMutationResult> NotApplied() =>
            Task.FromResult(
                new WorkflowMutationResult
                {
                    WasApplied = false,
                    Current = Current
                });
    }

    private sealed record TransitionRecord(
        WorkflowStatus ExpectedStatus,
        WorkflowStatus NextStatus,
        string Message);
}
