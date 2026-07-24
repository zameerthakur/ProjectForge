using Microsoft.Data.Sqlite;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;
using ProjectForge.Application.Workflows;
using ProjectForge.Infrastructure.Workflows;

namespace ProjectForge.Infrastructure.Tests.Workflows;

public sealed class SqliteWorkflowStoreTests : IDisposable
{
    private static readonly DateTimeOffset CreatedAt =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ProjectForge.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WorkflowSurvivesStoreRestart()
    {
        var snapshot = Snapshot();
        var databasePath = DatabasePath();
        using (var initialStore = new SqliteWorkflowStore(databasePath))
        {
            await initialStore.CreateAsync(snapshot);
        }

        using var restartedStore = new SqliteWorkflowStore(databasePath);
        var restored = await restartedStore.GetAsync(snapshot.Workflow.Id);

        Assert.NotNull(restored);
        Assert.Equal(snapshot.Workflow.Id, restored.Workflow.Id);
        Assert.Equal(
            snapshot.Workflow.Request.Instruction,
            restored.Workflow.Request.Instruction);
        Assert.Equal(WorkflowStatus.PendingApproval, restored.Workflow.Status);
        Assert.Equal(snapshot.Approval!.Id, restored.Approval!.Id);
        Assert.Equal(snapshot.Approval.Prompt, restored.Approval.Prompt);
        Assert.Equal(
            Assert.Single(snapshot.AuditEvents).EventType,
            Assert.Single(restored.AuditEvents).EventType);
    }

    [Theory]
    [InlineData(ApprovalDecision.Approved, WorkflowStatus.Approved)]
    [InlineData(ApprovalDecision.Rejected, WorkflowStatus.Rejected)]
    public async Task DecisionIsAppliedExactlyOnce(
        ApprovalDecision decision,
        WorkflowStatus expectedStatus)
    {
        var snapshot = Snapshot();
        using var store = new SqliteWorkflowStore(DatabasePath());
        await store.CreateAsync(snapshot);

        var first = await store.TryRecordDecisionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            decision,
            "maintainer",
            CreatedAt.AddMinutes(1));
        var repeated = await store.TryRecordDecisionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            decision,
            "maintainer",
            CreatedAt.AddMinutes(2));

        Assert.True(first.WasApplied);
        Assert.Equal(expectedStatus, first.Current.Workflow.Status);
        Assert.Equal(2, first.Current.Workflow.Version);
        Assert.Equal(decision, first.Current.Approval!.Decision);
        Assert.Equal(2, first.Current.AuditEvents.Count);

        Assert.False(repeated.WasApplied);
        Assert.Equal(2, repeated.Current.Workflow.Version);
        Assert.Equal(2, repeated.Current.AuditEvents.Count);
    }

    [Fact]
    public async Task ConcurrentDecisionAcrossStoreInstancesIsAppliedOnce()
    {
        var snapshot = Snapshot();
        var databasePath = DatabasePath();
        using var firstStore = new SqliteWorkflowStore(databasePath);
        using var secondStore = new SqliteWorkflowStore(databasePath);
        await firstStore.CreateAsync(snapshot);

        var attempts = await Task.WhenAll(
            firstStore.TryRecordDecisionAsync(
                snapshot.Workflow.Id,
                snapshot.Workflow.Version,
                ApprovalDecision.Approved,
                "first",
                CreatedAt.AddMinutes(1)),
            secondStore.TryRecordDecisionAsync(
                snapshot.Workflow.Id,
                snapshot.Workflow.Version,
                ApprovalDecision.Approved,
                "second",
                CreatedAt.AddMinutes(1)));

        Assert.Single(attempts, result => result.WasApplied);
        Assert.Single(attempts, result => !result.WasApplied);
        Assert.All(
            attempts,
            result => Assert.Equal(2, result.Current.Workflow.Version));
    }

    [Fact]
    public async Task TransitionRequiresExpectedStateAndVersion()
    {
        var snapshot = Snapshot();
        using var store = new SqliteWorkflowStore(DatabasePath());
        await store.CreateAsync(snapshot);

        var changed = await store.TryTransitionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            WorkflowStatus.PendingApproval,
            WorkflowStatus.Failed,
            "workflow.failed",
            "Workflow failed.",
            CreatedAt.AddMinutes(1),
            "Failure details.");
        var stale = await store.TryTransitionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            WorkflowStatus.PendingApproval,
            WorkflowStatus.Failed,
            "workflow.failed",
            "Workflow failed again.",
            CreatedAt.AddMinutes(2));

        Assert.True(changed.WasApplied);
        Assert.Equal(WorkflowStatus.Failed, changed.Current.Workflow.Status);
        Assert.Equal("Failure details.", changed.Current.Workflow.FailureMessage);
        Assert.False(stale.WasApplied);
        Assert.Equal(2, stale.Current.Workflow.Version);
        Assert.Equal(2, stale.Current.AuditEvents.Count);
    }

    [Fact]
    public async Task ListsMostRecentlyUpdatedWorkflowFirst()
    {
        var older = Snapshot(CreatedAt);
        var newer = Snapshot(CreatedAt.AddMinutes(1));
        using var store = new SqliteWorkflowStore(DatabasePath());
        await store.CreateAsync(older);
        await store.CreateAsync(newer);

        var workflows = await store.ListAsync();

        Assert.Equal(
            [newer.Workflow.Id, older.Workflow.Id],
            workflows.Select(item => item.Workflow.Id));
    }

    [Fact]
    public async Task ExecutionEvidenceAndArtifactsSurviveStoreRestart()
    {
        var snapshot = Snapshot();
        var databasePath = DatabasePath();
        var selection = ProviderSelection();
        var artifacts = ArtifactPaths();
        using (var initialStore = new SqliteWorkflowStore(databasePath))
        {
            await initialStore.CreateAsync(snapshot);
            var queued = await initialStore.TryTransitionAsync(
                snapshot.Workflow.Id,
                snapshot.Workflow.Version,
                WorkflowStatus.PendingApproval,
                WorkflowStatus.Queued,
                "workflow.execution-queued",
                "Workflow queued.",
                CreatedAt.AddMinutes(1));
            var running = await initialStore.TryStartExecutionAsync(
                snapshot.Workflow.Id,
                queued.Current.Workflow.Version,
                selection,
                CreatedAt.AddMinutes(2));
            var execution = SuccessfulExecution(
                snapshot.Workflow.Request.RequestId);

            var completed = await initialStore.TryCompleteExecutionAsync(
                snapshot.Workflow.Id,
                running.Current.Workflow.Version,
                WorkflowStatus.Succeeded,
                execution,
                artifacts,
                "workflow.execution-succeeded",
                "Provider completed execution.",
                CreatedAt.AddMinutes(4));

            Assert.True(completed.WasApplied);
        }

        using var restartedStore = new SqliteWorkflowStore(databasePath);
        var restored = await restartedStore.GetAsync(snapshot.Workflow.Id);

        Assert.NotNull(restored);
        Assert.Equal(WorkflowStatus.Succeeded, restored.Workflow.Status);
        Assert.Equal(
            selection.SelectedProviderName,
            restored.ProviderSelection!.SelectedProviderName);
        Assert.Equal(2, restored.ProviderSelection.Candidates.Count);
        Assert.Equal(
            WorkflowExecutionOutcome.Succeeded,
            restored.Execution!.Outcome);
        Assert.Equal(
            snapshot.Workflow.Request.RequestId,
            restored.Execution.RequestId);
        Assert.Equal(artifacts.MarkdownPath, restored.Artifacts!.MarkdownPath);
        Assert.Equal(artifacts.JsonPath, restored.Artifacts.JsonPath);
        Assert.Equal(4, restored.AuditEvents.Count);
    }

    [Fact]
    public async Task ConcurrentExecutionClaimIsAppliedOnceWithWinnerEvidence()
    {
        var snapshot = Snapshot();
        var databasePath = DatabasePath();
        using var firstStore = new SqliteWorkflowStore(databasePath);
        using var secondStore = new SqliteWorkflowStore(databasePath);
        await firstStore.CreateAsync(snapshot);
        var queued = await firstStore.TryTransitionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            WorkflowStatus.PendingApproval,
            WorkflowStatus.Queued,
            "workflow.execution-queued",
            "Workflow queued.",
            CreatedAt.AddMinutes(1));
        var version = queued.Current.Workflow.Version;

        var attempts = await Task.WhenAll(
            firstStore.TryStartExecutionAsync(
                snapshot.Workflow.Id,
                version,
                ProviderSelection("first-provider"),
                CreatedAt.AddMinutes(2)),
            secondStore.TryStartExecutionAsync(
                snapshot.Workflow.Id,
                version,
                ProviderSelection("second-provider"),
                CreatedAt.AddMinutes(2)));

        var winner = Assert.Single(attempts, result => result.WasApplied);
        var loser = Assert.Single(attempts, result => !result.WasApplied);
        Assert.Equal(WorkflowStatus.Running, loser.Current.Workflow.Status);
        Assert.Equal(
            winner.Current.ProviderSelection!.SelectedProviderName,
            loser.Current.ProviderSelection!.SelectedProviderName);
        Assert.Equal(3, loser.Current.AuditEvents.Count);
    }

    [Fact]
    public async Task InterruptedExecutionRequiresExplicitReconciliation()
    {
        var snapshot = Snapshot();
        var databasePath = DatabasePath();
        using (var initialStore = new SqliteWorkflowStore(databasePath))
        {
            await initialStore.CreateAsync(snapshot);
            var queued = await initialStore.TryTransitionAsync(
                snapshot.Workflow.Id,
                snapshot.Workflow.Version,
                WorkflowStatus.PendingApproval,
                WorkflowStatus.Queued,
                "workflow.execution-queued",
                "Workflow queued.",
                CreatedAt.AddMinutes(1));
            await initialStore.TryStartExecutionAsync(
                snapshot.Workflow.Id,
                queued.Current.Workflow.Version,
                ProviderSelection(),
                CreatedAt.AddMinutes(2));
        }

        using var restartedStore = new SqliteWorkflowStore(databasePath);
        var beforeRecovery =
            await restartedStore.GetAsync(snapshot.Workflow.Id);
        Assert.Equal(WorkflowStatus.Running, beforeRecovery!.Workflow.Status);

        const string reason =
            "The prior host stopped while provider execution was running.";
        var reconciled =
            await restartedStore.ReconcileInterruptedExecutionsAsync(
                CreatedAt.AddMinutes(5),
                reason);
        var repeated =
            await restartedStore.ReconcileInterruptedExecutionsAsync(
                CreatedAt.AddMinutes(6),
                reason);
        var recovered = await restartedStore.GetAsync(snapshot.Workflow.Id);

        Assert.Equal(1, reconciled);
        Assert.Equal(0, repeated);
        Assert.NotNull(recovered);
        Assert.Equal(
            WorkflowStatus.ReconciliationRequired,
            recovered.Workflow.Status);
        Assert.Equal(reason, recovered.Workflow.FailureMessage);
        Assert.Equal(
            WorkflowStatus.Running,
            recovered.Recovery!.InterruptedStatus);
        Assert.Equal(
            CreatedAt.AddMinutes(2),
            recovered.Recovery.InterruptedAtUtc);
        Assert.Equal(
            CreatedAt.AddMinutes(5),
            recovered.Recovery.DetectedAtUtc);
        Assert.Equal(reason, recovered.Recovery.Reason);
        Assert.Equal(
            "workflow.execution-interrupted",
            recovered.AuditEvents.Last().EventType);
    }

    [Fact]
    public async Task ReconciliationHonorsCancellationWithoutMutation()
    {
        var snapshot = Snapshot();
        using var store = new SqliteWorkflowStore(DatabasePath());
        await store.CreateAsync(snapshot);
        var queued = await store.TryTransitionAsync(
            snapshot.Workflow.Id,
            snapshot.Workflow.Version,
            WorkflowStatus.PendingApproval,
            WorkflowStatus.Queued,
            "workflow.execution-queued",
            "Workflow queued.",
            CreatedAt.AddMinutes(1));
        await store.TryStartExecutionAsync(
            snapshot.Workflow.Id,
            queued.Current.Workflow.Version,
            ProviderSelection(),
            CreatedAt.AddMinutes(2));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => store.ReconcileInterruptedExecutionsAsync(
                CreatedAt.AddMinutes(5),
                "Host restart detected.",
                cancellation.Token));
        var current = await store.GetAsync(snapshot.Workflow.Id);

        Assert.NotNull(current);
        Assert.Equal(WorkflowStatus.Running, current.Workflow.Status);
        Assert.Null(current.Recovery);
    }

    [Fact]
    public async Task ExistingDatabaseSchemaIsMigratedIdempotently()
    {
        var databasePath = DatabasePath();
        await CreateLegacySchemaAsync(databasePath);
        var snapshot = Snapshot();

        using (var migratedStore = new SqliteWorkflowStore(databasePath))
        {
            await migratedStore.CreateAsync(snapshot);
            var restored = await migratedStore.GetAsync(snapshot.Workflow.Id);

            Assert.NotNull(restored);
            Assert.Null(restored.ProviderSelection);
            Assert.Null(restored.Provisioning);
            Assert.Null(restored.Execution);
            Assert.Null(restored.Artifacts);
            Assert.Null(restored.Recovery);
        }

        using var reopenedStore = new SqliteWorkflowStore(databasePath);
        var reopened = await reopenedStore.GetAsync(snapshot.Workflow.Id);

        Assert.NotNull(reopened);
    }

    [Fact]
    public async Task ProvisioningAttemptSurvivesRestartAndCompletesExactlyOnce()
    {
        var snapshot = Snapshot();
        var databasePath = DatabasePath();
        WorkflowMutationResult provisioning;
        using (var initialStore = new SqliteWorkflowStore(databasePath))
        {
            await initialStore.CreateAsync(snapshot);
            var approved = await initialStore.TryRecordDecisionAsync(
                snapshot.Workflow.Id,
                1,
                ApprovalDecision.Approved,
                "maintainer",
                CreatedAt.AddMinutes(1));
            var queued = await initialStore.TryTransitionAsync(
                snapshot.Workflow.Id,
                approved.Current.Workflow.Version,
                WorkflowStatus.Approved,
                WorkflowStatus.Queued,
                "workflow.queued",
                "Workflow queued.",
                CreatedAt.AddMinutes(2));
            provisioning = await initialStore.TryBeginProvisioningAsync(
                snapshot.Workflow.Id,
                queued.Current.Workflow.Version,
                ProviderSelection(),
                Provisioning(WorkflowProvisioningStatus.InProgress));
        }

        using var restartedStore = new SqliteWorkflowStore(databasePath);
        var restored = await restartedStore.GetAsync(snapshot.Workflow.Id);
        Assert.NotNull(restored);
        Assert.Equal(WorkflowStatus.Provisioning, restored.Workflow.Status);
        Assert.Equal("ollama", restored.Provisioning!.ProviderId);
        Assert.Equal("0.30.8", restored.Provisioning.ProviderVersion);
        Assert.Equal(
            "qwen3",
            Assert.Single(restored.Provisioning.Requirements).RequirementId);

        var completedEvidence =
            Provisioning(WorkflowProvisioningStatus.Succeeded);
        var completed = await restartedStore.TryCompleteProvisioningAsync(
            snapshot.Workflow.Id,
            provisioning.Current.Workflow.Version,
            completedEvidence);
        var repeated = await restartedStore.TryCompleteProvisioningAsync(
            snapshot.Workflow.Id,
            provisioning.Current.Workflow.Version,
            completedEvidence);

        Assert.True(completed.WasApplied);
        Assert.Equal(WorkflowStatus.Queued, completed.Current.Workflow.Status);
        Assert.Equal(
            WorkflowProvisioningStatus.Succeeded,
            completed.Current.Provisioning!.Status);
        Assert.False(repeated.WasApplied);
        Assert.Equal(
            completed.Current.Workflow.Version,
            repeated.Current.Workflow.Version);
        Assert.Equal(
            1,
            completed.Current.AuditEvents.Count(
                item => item.EventType == "workflow.provisioning-succeeded"));
    }

    [Fact]
    public async Task FailedProvisioningCanBeRetriedWithoutClaimingExecution()
    {
        var snapshot = Snapshot();
        using var store = new SqliteWorkflowStore(DatabasePath());
        await store.CreateAsync(snapshot);
        var approved = await store.TryRecordDecisionAsync(
            snapshot.Workflow.Id,
            1,
            ApprovalDecision.Approved,
            "maintainer",
            CreatedAt.AddMinutes(1));
        var queued = await store.TryTransitionAsync(
            snapshot.Workflow.Id,
            approved.Current.Workflow.Version,
            WorkflowStatus.Approved,
            WorkflowStatus.Queued,
            "workflow.queued",
            "Workflow queued.",
            CreatedAt.AddMinutes(2));
        var started = await store.TryBeginProvisioningAsync(
            snapshot.Workflow.Id,
            queued.Current.Workflow.Version,
            ProviderSelection(),
            Provisioning(WorkflowProvisioningStatus.InProgress));
        var failed = await store.TryCompleteProvisioningAsync(
            snapshot.Workflow.Id,
            started.Current.Workflow.Version,
            Provisioning(WorkflowProvisioningStatus.Failed));
        var retryEvidence = Provisioning(
            WorkflowProvisioningStatus.InProgress,
            attempt: 2);
        var retried = await store.TryBeginProvisioningAsync(
            snapshot.Workflow.Id,
            failed.Current.Workflow.Version,
            ProviderSelection(),
            retryEvidence);

        Assert.Equal(
            WorkflowStatus.ProvisioningFailed,
            failed.Current.Workflow.Status);
        Assert.Null(failed.Current.Execution);
        Assert.True(retried.WasApplied);
        Assert.Equal(WorkflowStatus.Provisioning, retried.Current.Workflow.Status);
        Assert.Equal(2, retried.Current.Provisioning!.Attempt);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string DatabasePath() =>
        Path.Combine(_directory, "projectforge.db");

    private static WorkflowProvisioningEvidence Provisioning(
        WorkflowProvisioningStatus status,
        int attempt = 1) =>
        new()
        {
            ProviderId = "ollama",
            ProviderVersion = "0.30.8",
            Attempt = attempt,
            Status = status,
            Requirements =
            [
                new WorkflowProvisioningRequirementEvidence
                {
                    RequirementId = "qwen3",
                    Version = "8b-q4_K_M",
                    Category = "model"
                }
            ],
            StartedAtUtc = CreatedAt.AddMinutes(3),
            CompletedAtUtc =
                status == WorkflowProvisioningStatus.InProgress
                    ? null
                    : CreatedAt.AddMinutes(4),
            FailureCategory =
                status == WorkflowProvisioningStatus.Failed
                    ? "transient"
                    : null,
            FailureMessage =
                status == WorkflowProvisioningStatus.Failed
                    ? "Provisioning failed safely."
                    : null
        };

    private static WorkflowProviderSelectionEvidence ProviderSelection(
        string selectedProvider = "local-provider") =>
        new()
        {
            SelectedProviderName = selectedProvider,
            EstimatedCost = 0.25m,
            Candidates =
            [
                new WorkflowProviderCandidateEvidence
                {
                    ProviderName = selectedProvider,
                    EstimatedCost = 0.25m
                },
                new WorkflowProviderCandidateEvidence
                {
                    ProviderName = "ineligible-provider",
                    Rejections =
                    [
                        new WorkflowProviderRejectionEvidence
                        {
                            Code =
                                ProviderRejectionCode.CapabilityNotSupported,
                            Message = "Capability is not supported."
                        }
                    ]
                }
            ]
        };

    private static WorkflowExecutionEvidence SuccessfulExecution(
        Guid requestId) =>
        new()
        {
            RequestId = requestId,
            ProviderName = "local-provider",
            Outcome = WorkflowExecutionOutcome.Succeeded,
            Summary = "Execution completed.",
            Output = "Durable output.",
            StartedAtUtc = CreatedAt.AddMinutes(2),
            CompletedAtUtc = CreatedAt.AddMinutes(3),
            EstimatedCost = 0.25m,
            Metadata = new Dictionary<string, string>
            {
                ["mode"] = "deterministic"
            }
        };

    private static WorkflowArtifactPaths ArtifactPaths() =>
        new()
        {
            MarkdownPath = Path.GetFullPath("artifacts/result.md"),
            JsonPath = Path.GetFullPath("artifacts/result.json")
        };

    private static async Task CreateLegacySchemaAsync(string databasePath)
    {
        Directory.CreateDirectory(
            Path.GetDirectoryName(databasePath) ??
            throw new InvalidOperationException(
                "The database path must include a directory."));
        await using var connection = new SqliteConnection(
            new SqliteConnectionStringBuilder
            {
                DataSource = databasePath,
                Pooling = false
            }.ToString());
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            CREATE TABLE workflows (
                id TEXT PRIMARY KEY,
                request_json TEXT NOT NULL,
                status INTEGER NOT NULL,
                version INTEGER NOT NULL,
                created_at_utc TEXT NOT NULL,
                updated_at_utc TEXT NOT NULL,
                failure_message TEXT NULL
            );

            CREATE TABLE approvals (
                id TEXT PRIMARY KEY,
                workflow_id TEXT NOT NULL UNIQUE,
                prompt TEXT NOT NULL,
                decision INTEGER NULL,
                decided_by TEXT NULL,
                requested_at_utc TEXT NOT NULL,
                decided_at_utc TEXT NULL,
                FOREIGN KEY (workflow_id) REFERENCES workflows(id)
            );

            CREATE TABLE audit_events (
                id TEXT PRIMARY KEY,
                workflow_id TEXT NOT NULL,
                event_type TEXT NOT NULL,
                message TEXT NOT NULL,
                occurred_at_utc TEXT NOT NULL,
                FOREIGN KEY (workflow_id) REFERENCES workflows(id)
            );
            """;
        await command.ExecuteNonQueryAsync();
    }

    private static WorkflowSnapshot Snapshot(
        DateTimeOffset? createdAt = null)
    {
        var workflowId = Guid.NewGuid();
        var now = createdAt ?? CreatedAt;
        return new WorkflowSnapshot
        {
            Workflow = new WorkflowRecord
            {
                Id = workflowId,
                Request = new CapabilityExecutionRequest
                {
                    WorkflowId = workflowId.ToString("D"),
                    TaskId = "task-1",
                    TaskName = "Persist workflow",
                    Instruction = "Persist this workflow.",
                    Requirement = new CapabilityRequirement
                    {
                        Capability = EngineeringCapability.Coding,
                        RequiresApproval = true
                    },
                    CreatedAtUtc = now
                },
                Status = WorkflowStatus.PendingApproval,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            Approval = new ApprovalRecord
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                Prompt = "Approve execution?",
                RequestedAtUtc = now
            },
            AuditEvents =
            [
                new WorkflowAuditEvent
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = workflowId,
                    EventType = "workflow.approval-requested",
                    Message = "Workflow paused for approval.",
                    OccurredAtUtc = now
                }
            ]
        };
    }
}
