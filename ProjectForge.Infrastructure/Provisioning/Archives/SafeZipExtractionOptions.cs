namespace ProjectForge.Infrastructure.Provisioning.Archives;

public sealed record SafeZipExtractionOptions
{
    public int MaximumEntryCount { get; init; } = 10_000;

    public long MaximumEntryBytes { get; init; } = 512L * 1024 * 1024;

    public long MaximumExpandedBytes { get; init; } = 2L * 1024 * 1024 * 1024;

    public double MaximumCompressionRatio { get; init; } = 1_000;
}
