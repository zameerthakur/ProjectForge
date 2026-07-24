using System.Text;
using System.Text.Json;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Application.Workflows;
using ProjectForge.Infrastructure.Workflows;

namespace ProjectForge.Host.Tests;

public sealed class WorkflowEndpointTests
{
    [Fact]
    public async Task HealthReturnsHealthyStatus()
    {
        using var host = new TestHost();

        using var response = await host.Client.GetAsync("/health");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            "Healthy",
            document.RootElement.GetProperty("status").GetString());
    }

    [Fact]
    public async Task CreateGetAndListReturnPersistedWorkflow()
    {
        using var host = new TestHost();

        using var createResponse = await CreateWorkflowAsync(host.Client);
        var created = await ReadWorkflowAsync(createResponse);

        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);
        Assert.Equal(
            $"/workflows/{created.Workflow.Id:D}",
            createResponse.Headers.Location?.OriginalString);
        Assert.Equal(WorkflowStatus.PendingApproval, created.Workflow.Status);
        Assert.Equal("task-1", created.Workflow.Request.TaskId);

        var fetched = await host.Client.GetFromJsonAsync<WorkflowSnapshot>(
            $"/workflows/{created.Workflow.Id:D}");
        var listed = await host.Client
            .GetFromJsonAsync<WorkflowSnapshot[]>("/workflows");

        Assert.NotNull(fetched);
        Assert.Equal(created.Workflow.Id, fetched.Workflow.Id);
        var onlyWorkflow = Assert.Single(Assert.IsType<WorkflowSnapshot[]>(listed));
        Assert.Equal(created.Workflow.Id, onlyWorkflow.Workflow.Id);
    }

    [Theory]
    [InlineData(
        ApprovalDecision.Approved,
        WorkflowStatus.Succeeded,
        4,
        true)]
    [InlineData(
        ApprovalDecision.Rejected,
        WorkflowStatus.Rejected,
        1,
        false)]
    public async Task RecordDecisionTransitionsPendingWorkflow(
        ApprovalDecision decision,
        WorkflowStatus expectedStatus,
        long expectedVersionIncrease,
        bool expectsExecution)
    {
        using var host = new TestHost();
        using var createResponse = await CreateWorkflowAsync(host.Client);
        var created = await ReadWorkflowAsync(createResponse);

        using var response = await host.Client.PostAsJsonAsync(
            $"/workflows/{created.Workflow.Id:D}/decisions",
            new
            {
                expectedVersion = created.Workflow.Version,
                decision,
                decidedBy = "integration-test"
            });
        var updated = await ReadWorkflowAsync(response);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(expectedStatus, updated.Workflow.Status);
        Assert.Equal(
            created.Workflow.Version + expectedVersionIncrease,
            updated.Workflow.Version);
        Assert.Equal(decision, updated.Approval?.Decision);
        Assert.Equal("integration-test", updated.Approval?.DecidedBy);
        Assert.Equal(expectsExecution, updated.Execution is not null);
        Assert.Equal(expectsExecution, updated.ProviderSelection is not null);
        Assert.Equal(expectsExecution, updated.Artifacts is not null);
        if (expectsExecution)
        {
            Assert.True(File.Exists(updated.Artifacts!.MarkdownPath));
            Assert.True(File.Exists(updated.Artifacts.JsonPath));
        }
    }

    [Fact]
    public async Task RecordDecisionWithStaleVersionReturnsConflictProblem()
    {
        using var host = new TestHost();
        using var createResponse = await CreateWorkflowAsync(host.Client);
        var created = await ReadWorkflowAsync(createResponse);
        var path = $"/workflows/{created.Workflow.Id:D}/decisions";

        using var appliedResponse = await host.Client.PostAsJsonAsync(
            path,
            new
            {
                expectedVersion = created.Workflow.Version,
                decision = ApprovalDecision.Approved,
                decidedBy = "first-operator"
            });
        appliedResponse.EnsureSuccessStatusCode();

        using var conflictResponse = await host.Client.PostAsJsonAsync(
            path,
            new
            {
                expectedVersion = created.Workflow.Version,
                decision = ApprovalDecision.Rejected,
                decidedBy = "stale-operator"
            });
        using var document = JsonDocument.Parse(
            await conflictResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.Conflict, conflictResponse.StatusCode);
        Assert.Equal(
            "Workflow update conflict.",
            document.RootElement.GetProperty("title").GetString());
        var current = document.RootElement.GetProperty("current");
        Assert.Equal(
            (int)WorkflowStatus.Succeeded,
            current.GetProperty("workflow").GetProperty("status").GetInt32());
        Assert.Equal(
            created.Workflow.Version + 4,
            current.GetProperty("workflow").GetProperty("version").GetInt64());
    }

    [Fact]
    public async Task CreateWithInvalidValuesReturnsValidationProblem()
    {
        using var host = new TestHost();

        using var response = await host.Client.PostAsJsonAsync(
            "/workflows",
            new
            {
                taskId = " ",
                taskName = "",
                instruction = " ",
                approvalPrompt = "",
                capability = 0,
                maximumEstimatedCost = -1
            });
        var problem = await response.Content
            .ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(6, problem.Errors.Count);
        Assert.Contains("TaskId", problem.Errors.Keys);
        Assert.Contains("Capability", problem.Errors.Keys);
        Assert.Contains("MaximumEstimatedCost", problem.Errors.Keys);
    }

    [Fact]
    public async Task RecordDecisionWithInvalidValuesReturnsValidationProblem()
    {
        using var host = new TestHost();

        using var response = await host.Client.PostAsJsonAsync(
            $"/workflows/{Guid.NewGuid():D}/decisions",
            new
            {
                expectedVersion = 0,
                decision = 0,
                decidedBy = " "
            });
        var problem = await response.Content
            .ReadFromJsonAsync<ValidationProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal(3, problem.Errors.Count);
        Assert.Contains("ExpectedVersion", problem.Errors.Keys);
        Assert.Contains("Decision", problem.Errors.Keys);
        Assert.Contains("DecidedBy", problem.Errors.Keys);
    }

    [Fact]
    public async Task CreateWithMalformedJsonReturnsBadRequestProblem()
    {
        using var host = new TestHost();
        using var content = new StringContent(
            """{"taskId":""",
            Encoding.UTF8,
            "application/json");

        using var response = await host.Client.PostAsync(
            "/workflows",
            content);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(problem);
        Assert.Equal("Invalid request.", problem.Title);
    }

    [Fact]
    public async Task MissingWorkflowReturnsNotFoundProblems()
    {
        using var host = new TestHost();
        var workflowId = Guid.NewGuid();

        using var getResponse = await host.Client.GetAsync(
            $"/workflows/{workflowId:D}");
        var getProblem = await getResponse.Content
            .ReadFromJsonAsync<ProblemDetails>();

        using var decisionResponse = await host.Client.PostAsJsonAsync(
            $"/workflows/{workflowId:D}/decisions",
            new
            {
                expectedVersion = 1,
                decision = ApprovalDecision.Approved,
                decidedBy = "integration-test"
            });
        var decisionProblem = await decisionResponse.Content
            .ReadFromJsonAsync<ProblemDetails>();

        Assert.Equal(HttpStatusCode.NotFound, getResponse.StatusCode);
        Assert.Equal("Workflow not found.", getProblem?.Title);
        Assert.Equal(HttpStatusCode.NotFound, decisionResponse.StatusCode);
        Assert.Equal("Workflow not found.", decisionProblem?.Title);
    }

    [Fact]
    public async Task StartupReconcilesPersistedRunningWorkflowForGet()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ProjectForge.Host.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "workflows.db");
        Directory.CreateDirectory(directory);

        try
        {
            WorkflowSnapshot running;
            using (var store = new SqliteWorkflowStore(databasePath))
            {
                var coordinator = new WorkflowCoordinator(store);
                var pending = await coordinator.CreatePendingApprovalAsync(
                    new CapabilityExecutionRequest
                    {
                        WorkflowId = "pending",
                        TaskId = "restart-test",
                        TaskName = "Restart recovery",
                        Instruction = "Exercise interrupted execution recovery.",
                        Requirement = new CapabilityRequirement
                        {
                            Capability = EngineeringCapability.Testing,
                            RequiresApproval = true,
                            AllowCloudExecution = false
                        }
                    },
                    "Approve restart recovery test?");
                var approved = await coordinator.RecordDecisionAsync(
                    pending.Workflow.Id,
                    pending.Workflow.Version,
                    ApprovalDecision.Approved,
                    "integration-test");
                var queued = await store.TryTransitionAsync(
                    pending.Workflow.Id,
                    approved.Current.Workflow.Version,
                    WorkflowStatus.Approved,
                    WorkflowStatus.Queued,
                    "workflow.execution-queued",
                    "Workflow queued.",
                    DateTimeOffset.UtcNow);
                var claimed = await store.TryStartExecutionAsync(
                    pending.Workflow.Id,
                    queued.Current.Workflow.Version,
                    new WorkflowProviderSelectionEvidence
                    {
                        SelectedProviderName = "local-provider",
                        EstimatedCost = decimal.Zero,
                        Candidates =
                        [
                            new WorkflowProviderCandidateEvidence
                            {
                                ProviderName = "local-provider",
                                EstimatedCost = decimal.Zero
                            }
                        ]
                    },
                    DateTimeOffset.UtcNow);
                running = claimed.Current;
            }

            using var restartedHost = new TestHost(databasePath);
            using var response = await restartedHost.Client.GetAsync(
                $"/workflows/{running.Workflow.Id:D}");
            var recovered = await ReadWorkflowAsync(response);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal(
                WorkflowStatus.ReconciliationRequired,
                recovered.Workflow.Status);
            Assert.Equal(
                running.Workflow.Version + 1,
                recovered.Workflow.Version);
            Assert.Equal(
                WorkflowStatus.Running,
                recovered.Recovery!.InterruptedStatus);
            Assert.Equal(
                running.Workflow.UpdatedAtUtc,
                recovered.Recovery.InterruptedAtUtc);
            Assert.Equal(
                recovered.Workflow.FailureMessage,
                recovered.Recovery.Reason);
            Assert.Equal(
                "workflow.execution-interrupted",
                recovered.AuditEvents.Last().EventType);
            Assert.Equal(
                "local-provider",
                recovered.ProviderSelection!.SelectedProviderName);
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    [Fact]
    public async Task PendingApprovalSurvivesRestartAndExecutesOnce()
    {
        var directory = Path.Combine(
            Path.GetTempPath(),
            "ProjectForge.Host.Tests",
            Guid.NewGuid().ToString("N"));
        var databasePath = Path.Combine(directory, "workflows.db");
        Directory.CreateDirectory(directory);

        try
        {
            WorkflowSnapshot created;
            using (var initialHost = new TestHost(databasePath))
            {
                using var createResponse = await CreateWorkflowAsync(
                    initialHost.Client);
                created = await ReadWorkflowAsync(createResponse);
                Assert.Equal(
                    WorkflowStatus.PendingApproval,
                    created.Workflow.Status);
            }

            using var restartedHost = new TestHost(databasePath);
            var restored = await restartedHost.Client
                .GetFromJsonAsync<WorkflowSnapshot>(
                    $"/workflows/{created.Workflow.Id:D}");

            Assert.NotNull(restored);
            Assert.Equal(
                WorkflowStatus.PendingApproval,
                restored.Workflow.Status);
            Assert.Equal(created.Approval!.Id, restored.Approval!.Id);

            var decisionPath =
                $"/workflows/{created.Workflow.Id:D}/decisions";
            using var approvedResponse =
                await restartedHost.Client.PostAsJsonAsync(
                    decisionPath,
                    new
                    {
                        expectedVersion = created.Workflow.Version,
                        decision = ApprovalDecision.Approved,
                        decidedBy = "restart-test"
                    });
            var completed = await ReadWorkflowAsync(approvedResponse);

            Assert.Equal(HttpStatusCode.OK, approvedResponse.StatusCode);
            Assert.Equal(
                WorkflowStatus.Succeeded,
                completed.Workflow.Status);
            Assert.NotNull(completed.ProviderSelection);
            Assert.NotNull(completed.Execution);
            Assert.True(File.Exists(completed.Artifacts!.MarkdownPath));
            Assert.True(File.Exists(completed.Artifacts.JsonPath));

            using var repeatedResponse =
                await restartedHost.Client.PostAsJsonAsync(
                    decisionPath,
                    new
                    {
                        expectedVersion = created.Workflow.Version,
                        decision = ApprovalDecision.Approved,
                        decidedBy = "duplicate-test"
                    });
            using var repeatedDocument = JsonDocument.Parse(
                await repeatedResponse.Content.ReadAsStreamAsync());

            Assert.Equal(
                HttpStatusCode.Conflict,
                repeatedResponse.StatusCode);
            var current = repeatedDocument.RootElement.GetProperty("current");
            Assert.Equal(
                completed.Workflow.Version,
                current.GetProperty("workflow").GetProperty("version")
                    .GetInt64());
            Assert.Equal(
                (int)WorkflowStatus.Succeeded,
                current.GetProperty("workflow").GetProperty("status")
                    .GetInt32());
        }
        finally
        {
            if (Directory.Exists(directory))
            {
                Directory.Delete(directory, recursive: true);
            }
        }
    }

    private static Task<HttpResponseMessage> CreateWorkflowAsync(
        HttpClient client) =>
        client.PostAsJsonAsync(
            "/workflows",
            new
            {
                taskId = "task-1",
                taskName = "Host integration test",
                instruction = "Persist a workflow.",
                approvalPrompt = "Approve this test workflow?",
                capability = EngineeringCapability.Testing,
                allowCloudExecution = false,
                preferLocalExecution = true
            });

    private static async Task<WorkflowSnapshot> ReadWorkflowAsync(
        HttpResponseMessage response) =>
        await response.Content.ReadFromJsonAsync<WorkflowSnapshot>() ??
        throw new InvalidOperationException(
            "The response did not contain a workflow.");
}
