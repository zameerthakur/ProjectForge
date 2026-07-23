using Microsoft.AspNetCore.Diagnostics;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Application.Workflows;
using ProjectForge.Host.Workflows;
using ProjectForge.Infrastructure.Workflows;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddProblemDetails();
builder.Services.Configure<RouteHandlerOptions>(
    options => options.ThrowOnBadRequest = true);

var databasePath = builder.Configuration["ProjectForge:DatabasePath"];
if (string.IsNullOrWhiteSpace(databasePath))
{
    databasePath = Path.Combine(
        Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData),
        "ProjectForge",
        "projectforge.db");
}

builder.Services.AddSingleton<IWorkflowStore>(
    _ => new SqliteWorkflowStore(databasePath));
builder.Services.AddSingleton<IWorkflowCoordinator, WorkflowCoordinator>();

var app = builder.Build();

app.UseExceptionHandler(
    errorApplication =>
    {
        errorApplication.Run(
            async context =>
            {
                var exception = context.Features
                    .Get<IExceptionHandlerFeature>()?
                    .Error;
                var (statusCode, title, detail) = exception switch
                {
                    BadHttpRequestException badRequest => (
                        StatusCodes.Status400BadRequest,
                        "Invalid request.",
                        badRequest.Message),
                    KeyNotFoundException => (
                        StatusCodes.Status404NotFound,
                        "Workflow not found.",
                        "The requested workflow does not exist."),
                    _ => (
                        StatusCodes.Status500InternalServerError,
                        "An unexpected error occurred.",
                        "The server could not complete the request.")
                };

                await Results.Problem(
                        statusCode: statusCode,
                        title: title,
                        detail: detail)
                    .ExecuteAsync(context);
            });
    });

app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }));

app.MapGet(
    "/workflows",
    async (IWorkflowStore store, CancellationToken cancellationToken) =>
        Results.Ok(await store.ListAsync(cancellationToken)));

app.MapGet(
    "/workflows/{workflowId:guid}",
    async (
        Guid workflowId,
        IWorkflowStore store,
        CancellationToken cancellationToken) =>
    {
        var workflow = await store.GetAsync(workflowId, cancellationToken);
        return workflow is null
            ? Results.Problem(
                statusCode: StatusCodes.Status404NotFound,
                title: "Workflow not found.",
                detail: $"Workflow '{workflowId:D}' does not exist.")
            : Results.Ok(workflow);
    });

app.MapPost(
    "/workflows",
    async (
        CreateWorkflowRequest? input,
        IWorkflowCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        var validationErrors = WorkflowRequestValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        ArgumentNullException.ThrowIfNull(input);
        var request = new CapabilityExecutionRequest
        {
            WorkflowId = "pending",
            TaskId = input.TaskId,
            TaskName = input.TaskName,
            Instruction = input.Instruction,
            WorkingDirectory = input.WorkingDirectory,
            Requirement = new CapabilityRequirement
            {
                Capability = input.Capability,
                RequiresApproval = true,
                AllowCloudExecution = input.AllowCloudExecution,
                PreferLocalExecution = input.PreferLocalExecution,
                RequiresRepositoryAccess = input.RequiresRepositoryAccess,
                RequiresFileWriteAccess = input.RequiresFileWriteAccess,
                RequiresToolExecution = input.RequiresToolExecution,
                MaximumEstimatedCost = input.MaximumEstimatedCost
            }
        };

        var workflow = await coordinator.CreatePendingApprovalAsync(
            request,
            input.ApprovalPrompt,
            cancellationToken);
        return Results.Created(
            $"/workflows/{workflow.Workflow.Id:D}",
            workflow);
    });

app.MapPost(
    "/workflows/{workflowId:guid}/decisions",
    async (
        Guid workflowId,
        RecordDecisionRequest? input,
        IWorkflowCoordinator coordinator,
        CancellationToken cancellationToken) =>
    {
        var validationErrors = WorkflowRequestValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return Results.ValidationProblem(validationErrors);
        }

        ArgumentNullException.ThrowIfNull(input);
        var result = await coordinator.RecordDecisionAsync(
            workflowId,
            input.ExpectedVersion,
            input.Decision,
            input.DecidedBy,
            cancellationToken);

        return result.WasApplied
            ? Results.Ok(result.Current)
            : Results.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Workflow update conflict.",
                detail:
                    "The workflow changed or is no longer awaiting approval.",
                extensions: new Dictionary<string, object?>
                {
                    ["current"] = result.Current
                });
    });

await app.RunAsync();

public partial class Program;
