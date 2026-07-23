namespace ProjectForge.Abstractions.Provisioning;

public enum ProvisioningStatus
{
    Checking,
    Downloading,
    Verifying,
    Installing,
    Starting,
    Retrying,
    Ready,
    Canceled,
    Failed
}
