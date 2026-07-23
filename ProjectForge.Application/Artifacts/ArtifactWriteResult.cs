namespace ProjectForge.Application.Artifacts;

/// <summary>
/// Identifies the artifacts produced for a successful capability execution.
/// </summary>
public sealed class ArtifactWriteResult
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
