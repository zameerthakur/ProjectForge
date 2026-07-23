namespace ProjectForge.Abstractions.Capabilities;

/// <summary>
/// Represents a request to execute an engineering capability
/// as part of a ProjectForge workflow.
/// </summary>
public sealed class CapabilityExecutionRequest
{
    /// <summary>
    /// Gets the unique identifier of the execution request.
    /// </summary>
    public Guid RequestId { get; init; } = Guid.NewGuid();

    /// <summary>
    /// Gets the identifier of the workflow that created this request.
    /// </summary>
    public required string WorkflowId { get; init; }

    /// <summary>
    /// Gets the identifier of the workflow task that created this request.
    /// </summary>
    public required string TaskId { get; init; }

    /// <summary>
    /// Gets the human-readable name of the task.
    /// </summary>
    public required string TaskName { get; init; }

    /// <summary>
    /// Gets the instruction that should be executed by the selected provider.
    /// </summary>
    public required string Instruction { get; init; }

    /// <summary>
    /// Gets the required capability and provider-selection constraints.
    /// </summary>
    public required CapabilityRequirement Requirement { get; init; }

    /// <summary>
    /// Gets the working directory associated with the request.
    /// </summary>
    /// <remarks>
    /// This may identify a source repository, project directory,
    /// document workspace, or another task-specific location.
    /// </remarks>
    public string? WorkingDirectory { get; init; }

    /// <summary>
    /// Gets optional structured input values supplied to the provider.
    /// </summary>
    public IReadOnlyDictionary<string, string> Inputs { get; init; }
        = new Dictionary<string, string>();

    /// <summary>
    /// Gets the UTC date and time when the request was created.
    /// </summary>
    public DateTimeOffset CreatedAtUtc { get; init; }
        = DateTimeOffset.UtcNow;
}
