using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Application.Workflows;

namespace ProjectForge.Application.Tests.Workflows;

public sealed class WorkflowCoordinatorTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CreatesDurablePendingApprovalBeforeReturning()
    {
        var store = new RecordingWorkflowStore();
        var coordinator = Coordinator(store);

        var result = await coordinator.CreatePendingApprovalAsync(
            Request(),
            "Approve provider execution?");

        Assert.Same(result, store.Created);
        Assert.NotEqual(Guid.Empty, result.Workflow.Id);
        Assert.Equal(
            result.Workflow.Id.ToString("D"),
            result.Workflow.Request.WorkflowId);
        Assert.Equal(WorkflowStatus.PendingApproval, result.Workflow.Status);
        Assert.Equal(1, result.Workflow.Version);
        Assert.Equal(Now, result.Workflow.CreatedAtUtc);
        Assert.Equal(Now, result.Workflow.UpdatedAtUtc);

        var approval = Assert.IsType<ApprovalRecord>(result.Approval);
        Assert.Equal(result.Workflow.Id, approval.WorkflowId);
        Assert.Null(approval.Decision);
        Assert.Equal(Now, approval.RequestedAtUtc);

        var auditEvent = Assert.Single(result.AuditEvents);
        Assert.Equal("workflow.approval-requested", auditEvent.EventType);
        Assert.Equal(result.Workflow.Id, auditEvent.WorkflowId);
    }

    [Theory]
    [InlineData(ApprovalDecision.Approved)]
    [InlineData(ApprovalDecision.Rejected)]
    public async Task RecordsDecisionWithExpectedVersionAndTimestamp(
        ApprovalDecision decision)
    {
        var store = new RecordingWorkflowStore();
        var coordinator = Coordinator(store);
        var workflow = await coordinator.CreatePendingApprovalAsync(
            Request(),
            "Approve provider execution?");

        await coordinator.RecordDecisionAsync(
            workflow.Workflow.Id,
            workflow.Workflow.Version,
            decision,
            "maintainer");

        Assert.Equal(workflow.Workflow.Id, store.DecisionWorkflowId);
        Assert.Equal(workflow.Workflow.Version, store.DecisionExpectedVersion);
        Assert.Equal(decision, store.Decision);
        Assert.Equal("maintainer", store.DecidedBy);
        Assert.Equal(Now, store.DecidedAtUtc);
    }

    [Fact]
    public async Task DoesNotPersistCanceledCreation()
    {
        var store = new RecordingWorkflowStore();
        var coordinator = Coordinator(store);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => coordinator.CreatePendingApprovalAsync(
                Request(),
                "Approve provider execution?",
                cancellation.Token));

        Assert.Null(store.Created);
    }

    private static WorkflowCoordinator Coordinator(
        RecordingWorkflowStore store) =>
        new(store, new FixedTimeProvider(Now));

    private static CapabilityExecutionRequest Request() =>
        new()
        {
            WorkflowId = "workflow-1",
            TaskId = "task-1",
            TaskName = "Test workflow",
            Instruction = "Execute the approved task.",
            Requirement = new CapabilityRequirement
            {
                Capability = EngineeringCapability.Coding
            }
        };

    private sealed class FixedTimeProvider(DateTimeOffset utcNow)
        : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingWorkflowStore : IWorkflowStore
    {
        public WorkflowSnapshot? Created { get; private set; }

        public Guid? DecisionWorkflowId { get; private set; }

        public long? DecisionExpectedVersion { get; private set; }

        public ApprovalDecision? Decision { get; private set; }

        public string? DecidedBy { get; private set; }

        public DateTimeOffset? DecidedAtUtc { get; private set; }

        public Task CreateAsync(
            WorkflowSnapshot workflow,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Created = workflow;
            return Task.CompletedTask;
        }

        public Task<WorkflowSnapshot?> GetAsync(
            Guid workflowId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyCollection<WorkflowSnapshot>> ListAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowMutationResult> TryRecordDecisionAsync(
            Guid workflowId,
            long expectedVersion,
            ApprovalDecision decision,
            string? decidedBy,
            DateTimeOffset decidedAtUtc,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DecisionWorkflowId = workflowId;
            DecisionExpectedVersion = expectedVersion;
            Decision = decision;
            DecidedBy = decidedBy;
            DecidedAtUtc = decidedAtUtc;

            return Task.FromResult(
                new WorkflowMutationResult
                {
                    WasApplied = true,
                    Current = Created ??
                        throw new InvalidOperationException(
                            "Create a workflow before recording a decision.")
                });
        }

        public Task<WorkflowMutationResult> TryTransitionAsync(
            Guid workflowId,
            long expectedVersion,
            WorkflowStatus expectedStatus,
            WorkflowStatus nextStatus,
            string auditEventType,
            string auditMessage,
            DateTimeOffset occurredAtUtc,
            string? failureMessage = null,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<WorkflowMutationResult> TryStartExecutionAsync(
            Guid workflowId,
            long expectedVersion,
            WorkflowProviderSelectionEvidence providerSelection,
            DateTimeOffset startedAtUtc,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

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
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<int> ReconcileInterruptedExecutionsAsync(
            DateTimeOffset detectedAtUtc,
            string reason,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
