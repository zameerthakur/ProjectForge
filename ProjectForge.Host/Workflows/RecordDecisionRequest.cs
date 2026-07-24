using ProjectForge.Application.Workflows;

namespace ProjectForge.Host.Workflows;

public sealed class RecordDecisionRequest
{
    public required long ExpectedVersion { get; init; }

    public required ApprovalDecision Decision { get; init; }

    public string? DecidedBy { get; init; }
}
