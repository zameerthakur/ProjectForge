using ProjectForge.Abstractions.Capabilities;

namespace ProjectForge.Application.Artifacts;

/// <summary>
/// Writes the durable artifacts for a successful capability execution.
/// </summary>
public interface IExecutionArtifactWriter
{
    /// <summary>
    /// Writes one human-readable and one machine-readable artifact.
    /// </summary>
    /// <param name="workflowId">The stable workflow identifier.</param>
    /// <param name="result">The successful execution result to write.</param>
    /// <param name="cancellationToken">
    /// A token used to cancel the write before artifacts are published.
    /// </param>
    /// <returns>The absolute paths of the published artifacts.</returns>
    Task<ArtifactWriteResult> WriteAsync(
        Guid workflowId,
        CapabilityExecutionResult result,
        CancellationToken cancellationToken = default);
}
