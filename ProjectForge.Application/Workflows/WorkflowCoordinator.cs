using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Application.Workflows;

/// <summary>
/// Creates approval-gated workflows and records human decisions.
/// </summary>
public sealed class WorkflowCoordinator : IWorkflowCoordinator
{
    private readonly IWorkflowStore _store;
    private readonly TimeProvider _timeProvider;

    public WorkflowCoordinator(
        IWorkflowStore store,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(store);

        _store = store;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public async Task<WorkflowSnapshot> CreatePendingApprovalAsync(
        CapabilityExecutionRequest request,
        string approvalPrompt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(approvalPrompt);
        cancellationToken.ThrowIfCancellationRequested();

        var workflowId = Guid.NewGuid();
        var now = _timeProvider.GetUtcNow();
        var workflowRequest = BindToWorkflow(request, workflowId);
        var snapshot = new WorkflowSnapshot
        {
            Workflow = new WorkflowRecord
            {
                Id = workflowId,
                Request = workflowRequest,
                Status = WorkflowStatus.PendingApproval,
                Version = 1,
                CreatedAtUtc = now,
                UpdatedAtUtc = now
            },
            Approval = new ApprovalRecord
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                Prompt = approvalPrompt,
                RequestedAtUtc = now
            },
            AuditEvents =
            [
                new WorkflowAuditEvent
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = workflowId,
                    EventType = "workflow.approval-requested",
                    Message = "Workflow created and paused for approval.",
                    OccurredAtUtc = now
                }
            ]
        };

        await _store.CreateAsync(snapshot, cancellationToken);
        return snapshot;
    }

    private static CapabilityExecutionRequest BindToWorkflow(
        CapabilityExecutionRequest request,
        Guid workflowId)
    {
        return new CapabilityExecutionRequest
        {
            RequestId = request.RequestId,
            WorkflowId = workflowId.ToString("D"),
            TaskId = request.TaskId,
            TaskName = request.TaskName,
            Instruction = request.Instruction,
            Requirement = request.Requirement,
            WorkingDirectory = request.WorkingDirectory,
            Inputs = request.Inputs,
            CreatedAtUtc = request.CreatedAtUtc
        };
    }

    /// <inheritdoc />
    public Task<WorkflowMutationResult> RecordDecisionAsync(
        Guid workflowId,
        long expectedVersion,
        ApprovalDecision decision,
        string? decidedBy,
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

        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, null);
        }

        cancellationToken.ThrowIfCancellationRequested();

        return _store.TryRecordDecisionAsync(
            workflowId,
            expectedVersion,
            decision,
            decidedBy,
            _timeProvider.GetUtcNow(),
            cancellationToken);
    }
}
