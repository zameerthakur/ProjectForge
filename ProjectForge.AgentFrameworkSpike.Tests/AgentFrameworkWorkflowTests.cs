using Microsoft.Agents.AI.Workflows;

namespace ProjectForge.AgentFrameworkSpike.Tests;

public sealed class AgentFrameworkWorkflowTests
{
    [Fact]
    public async Task ExecutesTypeSafeWorkflowAndEmitsOutput()
    {
        var workflow = BuildWorkflow(
            new UppercaseExecutor(),
            new ReverseExecutor());

        await using var run = await InProcessExecution.RunAsync(
            workflow,
            "ProjectForge");

        var output = Assert.Single(
            run.NewEvents.OfType<WorkflowOutputEvent>());

        Assert.Equal("EGROFTCEJORP", output.Data);
    }

    [Fact]
    public async Task RehydratesNewRunFromSuperstepCheckpoint()
    {
        var checkpointManager = CheckpointManager.CreateInMemory();
        var originalUppercase = new UppercaseExecutor();
        var originalReverse = new ReverseExecutor();
        var originalWorkflow = BuildWorkflow(
            originalUppercase,
            originalReverse);
        await using var originalRun =
            await InProcessExecution.RunStreamingAsync(
                originalWorkflow,
                "ProjectForge",
                checkpointManager);

        await DrainAsync(originalRun);

        var checkpoint = Assert.Single(originalRun.Checkpoints.Take(1));
        var resumedUppercase = new UppercaseExecutor();
        var resumedReverse = new ReverseExecutor();
        var resumedWorkflow = BuildWorkflow(
            resumedUppercase,
            resumedReverse);
        await using var resumedRun =
            await InProcessExecution.ResumeStreamingAsync(
                resumedWorkflow,
                checkpoint,
                checkpointManager);

        var events = await DrainAsync(resumedRun);
        var output = Assert.Single(events.OfType<WorkflowOutputEvent>());

        Assert.Equal("EGROFTCEJORP", output.Data);
        Assert.Equal(0, resumedUppercase.ExecutionCount);
        Assert.Equal(1, resumedReverse.ExecutionCount);
    }

    [Fact]
    public async Task PausesForTypedExternalApprovalAndResumes()
    {
        var approvalPort = RequestPort.Create<string, bool>("approval");
        Func<bool, string> describeDecision = approved =>
            approved ? "approved" : "rejected";
        var decision = describeDecision.BindAsExecutor("decision");
        var workflow = new WorkflowBuilder(approvalPort)
            .AddEdge(approvalPort, decision)
            .WithOutputFrom(decision)
            .Build();
        await using var run = await InProcessExecution.RunStreamingAsync(
            workflow,
            "Approve provider execution?");
        var events = new List<WorkflowEvent>();

        await foreach (var workflowEvent in run.WatchStreamAsync())
        {
            events.Add(workflowEvent);

            if (workflowEvent is RequestInfoEvent request)
            {
                await run.SendResponseAsync(
                    request.Request.CreateResponse(true));
            }
        }

        var requestEvent = Assert.Single(events.OfType<RequestInfoEvent>());
        Assert.Equal(
            "Approve provider execution?",
            requestEvent.Request.Data.As<string>());
        var output = Assert.Single(events.OfType<WorkflowOutputEvent>());
        Assert.Equal("approved", output.Data);
    }

    private static Workflow BuildWorkflow(
        UppercaseExecutor uppercase,
        ReverseExecutor reverse)
    {
        var builder = new WorkflowBuilder(uppercase);
        builder.AddEdge(uppercase, reverse).WithOutputFrom(reverse);
        return builder.Build();
    }

    private static async Task<IReadOnlyCollection<WorkflowEvent>> DrainAsync(
        StreamingRun run)
    {
        var events = new List<WorkflowEvent>();

        await foreach (var workflowEvent in run.WatchStreamAsync())
        {
            events.Add(workflowEvent);
        }

        return events;
    }

    private sealed class UppercaseExecutor()
        : Executor<string, string>("uppercase")
    {
        public int ExecutionCount { get; private set; }

        public override ValueTask<string> HandleAsync(
            string message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionCount++;
            return ValueTask.FromResult(message.ToUpperInvariant());
        }
    }

    private sealed class ReverseExecutor()
        : Executor<string, string>("reverse")
    {
        public int ExecutionCount { get; private set; }

        public override ValueTask<string> HandleAsync(
            string message,
            IWorkflowContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ExecutionCount++;
            return ValueTask.FromResult(string.Concat(message.Reverse()));
        }
    }
}
