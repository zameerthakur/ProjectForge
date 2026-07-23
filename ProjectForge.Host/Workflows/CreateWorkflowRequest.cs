using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Host.Workflows;

public sealed class CreateWorkflowRequest
{
    public required string TaskId { get; init; }

    public required string TaskName { get; init; }

    public required string Instruction { get; init; }

    public required string ApprovalPrompt { get; init; }

    public required EngineeringCapability Capability { get; init; }

    public string? WorkingDirectory { get; init; }

    public bool AllowCloudExecution { get; init; } = true;

    public bool PreferLocalExecution { get; init; } = true;

    public bool RequiresRepositoryAccess { get; init; }

    public bool RequiresFileWriteAccess { get; init; }

    public bool RequiresToolExecution { get; init; }

    public decimal? MaximumEstimatedCost { get; init; }
}
