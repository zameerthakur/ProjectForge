using ProjectForge.Abstractions.Capabilities;
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private string DatabasePath() =>
        Path.Combine(_directory, "projectforge.db");

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
