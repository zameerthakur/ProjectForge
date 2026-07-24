namespace ProjectForge.Host.Workflows;

public sealed class ResumeWorkflowRequest
{
    public required long ExpectedVersion { get; init; }
}
