namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents the outcome of an optimistic workflow mutation.
/// </summary>
public sealed class WorkflowMutationResult
{
    /// <summary>
    /// Gets whether the requested mutation was applied.
    /// </summary>
    public required bool WasApplied { get; init; }

    /// <summary>
    /// Gets the current workflow state after the mutation attempt.
    /// </summary>
    public required WorkflowSnapshot Current { get; init; }
}
