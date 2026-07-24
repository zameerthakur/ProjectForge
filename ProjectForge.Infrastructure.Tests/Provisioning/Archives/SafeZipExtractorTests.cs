using System.IO.Compression;
using ProjectForge.Infrastructure.Provisioning.Archives;

namespace ProjectForge.Infrastructure.Tests.Provisioning.Archives;

public sealed class SafeZipExtractorTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "ProjectForge.SafeZipExtractor.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task ExtractsValidatedArchiveAndPreservesOwnershipMarker()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(
            ("bin/runtime.exe", "runtime"),
            ("licenses/notice.txt", "notice"));

        await new SafeZipExtractor().ExtractAsync(
            archive,
            staging,
            "bin/runtime.exe");

        Assert.Equal(
            "runtime",
            await File.ReadAllTextAsync(Path.Combine(staging, "bin", "runtime.exe")));
        Assert.Equal(
            "notice",
            await File.ReadAllTextAsync(Path.Combine(staging, "licenses", "notice.txt")));
        Assert.True(File.Exists(Path.Combine(staging, ".projectforge-staging")));
    }

    [Theory]
    [InlineData("../escape.exe")]
    [InlineData("bin/../../escape.exe")]
    [InlineData(@"\server\share\runtime.exe")]
    [InlineData(@"C:\runtime.exe")]
    [InlineData("/root/runtime.exe")]
    [InlineData("bin/./runtime.exe")]
    public async Task RejectsUnsafeEntryPaths(string unsafeName)
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive((unsafeName, "bad"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SafeZipExtractor().ExtractAsync(
                archive,
                staging,
                unsafeName));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task RejectsCaseCollidingEntries()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(
            ("bin/runtime.exe", "first"),
            ("BIN/RUNTIME.EXE", "second"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SafeZipExtractor().ExtractAsync(
                archive,
                staging,
                "bin/runtime.exe"));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task RejectsFileAndDirectoryCollisions()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(
            ("bin", "file"),
            ("bin/runtime.exe", "runtime"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SafeZipExtractor().ExtractAsync(
                archive,
                staging,
                "bin/runtime.exe"));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task RejectsUnixSymbolicLinks()
    {
        var staging = CreateOwnedStaging();
        await using var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            var link = archive.CreateEntry("runtime.exe");
            link.ExternalAttributes = 0xA000 << 16;
            await using var writer = new StreamWriter(link.Open());
            await writer.WriteAsync("target");
        }

        stream.Position = 0;
        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SafeZipExtractor().ExtractAsync(
                stream,
                staging,
                "runtime.exe"));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task EnforcesEntryCountLimitBeforeExtraction()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(
            ("runtime.exe", "runtime"),
            ("notice.txt", "notice"));
        var extractor = new SafeZipExtractor(
            new SafeZipExtractionOptions { MaximumEntryCount = 1 });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(archive, staging, "runtime.exe"));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task EnforcesPerEntryAndTotalExpandedByteLimits()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(
            ("runtime.exe", "12345"),
            ("notice.txt", "67890"));
        var extractor = new SafeZipExtractor(
            new SafeZipExtractionOptions
            {
                MaximumEntryBytes = 5,
                MaximumExpandedBytes = 9,
            });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(archive, staging, "runtime.exe"));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task RejectsExcessiveCompressionRatio()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(
            ("runtime.exe", new string('A', 4096)));
        var extractor = new SafeZipExtractor(
            new SafeZipExtractionOptions { MaximumCompressionRatio = 2 });

        await Assert.ThrowsAsync<InvalidDataException>(
            () => extractor.ExtractAsync(archive, staging, "runtime.exe"));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task RejectsMissingExpectedEntrypoint()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(("readme.txt", "documentation"));

        await Assert.ThrowsAsync<InvalidDataException>(
            () => new SafeZipExtractor().ExtractAsync(
                archive,
                staging,
                "runtime.exe"));

        AssertOnlyMarkerRemains(staging);
    }

    [Fact]
    public async Task RejectsStagingThatContainsExistingOutput()
    {
        var staging = CreateOwnedStaging();
        await File.WriteAllTextAsync(Path.Combine(staging, "existing.txt"), "keep");
        await using var archive = CreateArchive(("runtime.exe", "runtime"));

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => new SafeZipExtractor().ExtractAsync(
                archive,
                staging,
                "runtime.exe"));

        Assert.Equal("keep", await File.ReadAllTextAsync(Path.Combine(staging, "existing.txt")));
    }

    [Fact]
    public async Task PreservesCancellationWithoutCreatingOutputs()
    {
        var staging = CreateOwnedStaging();
        await using var archive = CreateArchive(("runtime.exe", "runtime"));
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => new SafeZipExtractor().ExtractAsync(
                archive,
                staging,
                "runtime.exe",
                cancellation.Token));

        AssertOnlyMarkerRemains(staging);
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private string CreateOwnedStaging()
    {
        var staging = Path.Combine(_testRoot, Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        File.WriteAllText(Path.Combine(staging, ".projectforge-staging"), "owned");
        return staging;
    }

    private static MemoryStream CreateArchive(params (string Name, string Content)[] entries)
    {
        var stream = new MemoryStream();
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                var entry = archive.CreateEntry(name, CompressionLevel.SmallestSize);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(content);
            }
        }

        stream.Position = 0;
        return stream;
    }

    private static void AssertOnlyMarkerRemains(string staging)
    {
        Assert.Equal(
            [Path.Combine(staging, ".projectforge-staging")],
            Directory.EnumerateFileSystemEntries(staging));
    }
}
