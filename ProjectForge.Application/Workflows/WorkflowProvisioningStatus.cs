namespace ProjectForge.Application.Workflows;

/// <summary>
/// Identifies the durable outcome of a provider provisioning attempt.
/// </summary>
public enum WorkflowProvisioningStatus
{
    InProgress = 1,
    Succeeded = 2,
    Failed = 3
}
