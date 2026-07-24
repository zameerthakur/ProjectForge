namespace ProjectForge.Application.Workflows;

/// <summary>
/// Identifies the outcome of a human approval request.
/// </summary>
public enum ApprovalDecision
{
    /// <summary>
    /// Execution is permitted to continue.
    /// </summary>
    Approved = 1,

    /// <summary>
    /// Execution must terminate without invoking a provider.
    /// </summary>
    Rejected = 2
}
