using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Application.Workflows;

namespace ProjectForge.Host.Workflows;

internal static class WorkflowRequestValidator
{
    private const int IdentifierMaximumLength = 200;
    private const int InstructionMaximumLength = 50_000;
    private const int PromptMaximumLength = 2_000;
    private const int PathMaximumLength = 4_096;

    public static IDictionary<string, string[]> Validate(
        CreateWorkflowRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["A request body is required."];
            return errors;
        }

        ValidateRequiredText(
            request.TaskId,
            nameof(request.TaskId),
            IdentifierMaximumLength,
            errors);
        ValidateRequiredText(
            request.TaskName,
            nameof(request.TaskName),
            IdentifierMaximumLength,
            errors);
        ValidateRequiredText(
            request.Instruction,
            nameof(request.Instruction),
            InstructionMaximumLength,
            errors);
        ValidateRequiredText(
            request.ApprovalPrompt,
            nameof(request.ApprovalPrompt),
            PromptMaximumLength,
            errors);

        if (!Enum.IsDefined(request.Capability))
        {
            errors[nameof(request.Capability)] =
                ["A supported engineering capability is required."];
        }

        if (request.WorkingDirectory is { Length: > PathMaximumLength })
        {
            errors[nameof(request.WorkingDirectory)] =
                [$"The value cannot exceed {PathMaximumLength} characters."];
        }

        if (request.MaximumEstimatedCost < 0)
        {
            errors[nameof(request.MaximumEstimatedCost)] =
                ["The maximum estimated cost cannot be negative."];
        }

        return errors;
    }

    public static IDictionary<string, string[]> Validate(
        RecordDecisionRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["A request body is required."];
            return errors;
        }

        if (request.ExpectedVersion < 1)
        {
            errors[nameof(request.ExpectedVersion)] =
                ["The expected version must be positive."];
        }

        if (!Enum.IsDefined(request.Decision))
        {
            errors[nameof(request.Decision)] =
                ["A supported approval decision is required."];
        }

        if (request.DecidedBy is not null)
        {
            if (string.IsNullOrWhiteSpace(request.DecidedBy))
            {
                errors[nameof(request.DecidedBy)] =
                    ["The value cannot be empty when provided."];
            }
            else if (request.DecidedBy.Length > IdentifierMaximumLength)
            {
                errors[nameof(request.DecidedBy)] =
                    [
                        "The value cannot exceed " +
                        $"{IdentifierMaximumLength} characters."
                    ];
            }
        }

        return errors;
    }

    public static IDictionary<string, string[]> Validate(
        ResumeWorkflowRequest? request)
    {
        var errors = new Dictionary<string, string[]>();
        if (request is null)
        {
            errors["request"] = ["A request body is required."];
            return errors;
        }

        if (request.ExpectedVersion < 1)
        {
            errors[nameof(request.ExpectedVersion)] =
                ["The expected version must be positive."];
        }

        return errors;
    }

    private static void ValidateRequiredText(
        string? value,
        string propertyName,
        int maximumLength,
        IDictionary<string, string[]> errors)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            errors[propertyName] = ["The value is required."];
        }
        else if (value.Length > maximumLength)
        {
            errors[propertyName] =
                [$"The value cannot exceed {maximumLength} characters."];
        }
    }
}
