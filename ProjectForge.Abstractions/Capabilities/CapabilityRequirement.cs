namespace ProjectForge.Abstractions.Capabilities;

/// <summary>
/// Describes an engineering capability required by a workflow task,
/// together with the constraints that affect provider selection.
/// </summary>
public sealed class CapabilityRequirement
{
    /// <summary>
    /// Gets the engineering capability required by the task.
    /// </summary>
    public required EngineeringCapability Capability { get; init; }

    /// <summary>
    /// Gets whether execution through a remote cloud provider is permitted.
    /// </summary>
    /// <remarks>
    /// Set this to <see langword="false"/> when project data,
    /// source code, credentials, or documents must remain local.
    /// </remarks>
    public bool AllowCloudExecution { get; init; } = true;

    /// <summary>
    /// Gets whether a local execution provider should be preferred
    /// when a suitable local provider is available.
    /// </summary>
    public bool PreferLocalExecution { get; init; } = true;

    /// <summary>
    /// Gets whether the task requires access to a source-code repository.
    /// </summary>
    public bool RequiresRepositoryAccess { get; init; }

    /// <summary>
    /// Gets whether the selected provider must be permitted to modify files.
    /// </summary>
    public bool RequiresFileWriteAccess { get; init; }

    /// <summary>
    /// Gets whether the selected provider must be permitted to execute tools
    /// or operating-system commands.
    /// </summary>
    public bool RequiresToolExecution { get; init; }

    /// <summary>
    /// Gets whether human approval is required before execution begins.
    /// </summary>
    public bool RequiresApproval { get; init; }

    /// <summary>
    /// Gets the maximum permitted estimated execution cost.
    /// </summary>
    /// <remarks>
    /// A null value means that no explicit cost ceiling has been defined.
    /// The scheduler should still prefer the lowest-cost suitable provider.
    /// </remarks>
    public decimal? MaximumEstimatedCost { get; init; }

    /// <summary>
    /// Gets optional instructions that may influence provider selection.
    /// </summary>
    public string? SelectionInstructions { get; init; }
}
