using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents the ProjectForge-owned durable record for one workflow instance.
/// </summary>
public sealed class WorkflowRecord
{
    /// <summary>
    /// Gets the stable workflow identifier.
    /// </summary>
    public required Guid Id { get; init; }

    /// <summary>
    /// Gets the capability execution request owned by this workflow.
    /// </summary>
    public required CapabilityExecutionRequest Request { get; init; }

    /// <summary>
    /// Gets the current durable workflow status.
    /// </summary>
    public required WorkflowStatus Status { get; init; }

    /// <summary>
    /// Gets the optimistic concurrency version.
    /// </summary>
    public required long Version { get; init; }

    /// <summary>
    /// Gets the UTC time at which the workflow was created.
    /// </summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }

    /// <summary>
    /// Gets the UTC time at which the workflow was last updated.
    /// </summary>
    public required DateTimeOffset UpdatedAtUtc { get; init; }

    /// <summary>
    /// Gets an optional terminal failure description.
    /// </summary>
    public string? FailureMessage { get; init; }
}
