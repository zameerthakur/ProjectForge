namespace ProjectForge.Application.Workflows;

/// <summary>
/// Identifies durable execution artifacts published for a workflow.
/// </summary>
public sealed record WorkflowArtifactPaths
{
    /// <summary>
    /// Gets the absolute path to the human-readable Markdown artifact.
    /// </summary>
    public required string MarkdownPath { get; init; }

    /// <summary>
    /// Gets the absolute path to the machine-readable JSON artifact.
    /// </summary>
    public required string JsonPath { get; init; }
}
