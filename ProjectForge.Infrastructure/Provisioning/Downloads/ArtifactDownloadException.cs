namespace ProjectForge.Infrastructure.Provisioning.Downloads;

public sealed class ArtifactDownloadException : Exception
{
    public ArtifactDownloadException(string message)
        : base(message)
    {
    }

    public ArtifactDownloadException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
