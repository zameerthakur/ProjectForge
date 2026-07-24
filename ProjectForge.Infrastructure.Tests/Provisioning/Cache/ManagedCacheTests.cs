using ProjectForge.Infrastructure.Provisioning.Cache;

namespace ProjectForge.Infrastructure.Tests.Provisioning.Cache;

public sealed class ManagedCacheTests : IDisposable
{
    private readonly string _testRoot = Path.Combine(
        Path.GetTempPath(),
        "ProjectForge.ManagedCache.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public void ResolveBuildsStableVersionedApplicationPath()
    {
        var paths = new ManagedCachePaths(_testRoot);

        var first = paths.Resolve("ollama", "0.30.8", "windows-x64");
        var second = paths.Resolve("ollama", "0.30.8", "windows-x64");

        Assert.Equal(first, second);
        Assert.Equal(
            Path.Combine(_testRoot, "ollama", "0.30.8", "windows-x64"),
            first);
    }

    [Theory]
    [InlineData("..")]
    [InlineData("../escape")]
    [InlineData(@"..\escape")]
    [InlineData(@"C:\escape")]
    [InlineData("nested/path")]
    public void ResolveRejectsUnsafeSegments(string segment)
    {
        var paths = new ManagedCachePaths(_testRoot);

        Assert.Throws<ArgumentException>(
            () => paths.Resolve(segment, "1.0.0", "windows-x64"));
    }

    [Fact]
    public void PromoteVerifiedAtomicallyMovesOwnedStagingToFinalPath()
    {
        var cache = CreateCache();
        var staging = cache.CreateStaging("runtime", "1.0.0", "windows-x64");
        File.WriteAllText(Path.Combine(staging.StagingPath, "runtime.exe"), "verified");

        var finalPath = cache.PromoteVerified(staging);

        Assert.False(Directory.Exists(staging.StagingPath));
        Assert.Equal("verified", File.ReadAllText(Path.Combine(finalPath, "runtime.exe")));
        Assert.False(File.Exists(Path.Combine(finalPath, ".projectforge-staging")));
    }

    [Fact]
    public void PromoteVerifiedRefusesAFinalPathCollision()
    {
        var cache = CreateCache();
        var staging = cache.CreateStaging("runtime", "1.0.0", "windows-x64");
        Directory.CreateDirectory(staging.FinalPath);

        Assert.Throws<IOException>(() => cache.PromoteVerified(staging));
        Assert.True(Directory.Exists(staging.StagingPath));
    }

    [Fact]
    public void CleanupRemovesOnlyOwnedStalePartialDirectories()
    {
        var cache = CreateCache();
        var stale = cache.CreateStaging("runtime", "1.0.0", "windows-x64");
        var fresh = cache.CreateStaging("runtime", "1.0.0", "windows-x64");
        var marker = Path.Combine(stale.StagingPath, ".projectforge-staging");
        File.SetLastWriteTimeUtc(marker, DateTime.UtcNow.AddHours(-2));

        var foreign = stale.FinalPath + ".partial-foreign";
        Directory.CreateDirectory(foreign);
        File.WriteAllText(Path.Combine(foreign, "keep.txt"), "foreign");

        var removed = cache.CleanOwnedStaging(
            "runtime",
            "1.0.0",
            "windows-x64",
            TimeSpan.FromHours(1));

        Assert.Equal([stale.StagingPath], removed);
        Assert.False(Directory.Exists(stale.StagingPath));
        Assert.True(Directory.Exists(fresh.StagingPath));
        Assert.True(Directory.Exists(foreign));
    }

    [Fact]
    public void PromoteRejectsUnownedPartialDirectory()
    {
        var paths = new ManagedCachePaths(_testRoot);
        var cache = new ManagedCache(paths);
        var finalPath = paths.Resolve("runtime", "1.0.0", "windows-x64");
        var stagingPath = finalPath + ".partial-foreign";
        Directory.CreateDirectory(stagingPath);

        Assert.Throws<InvalidOperationException>(
            () => cache.PromoteVerified(new ManagedCacheStaging(stagingPath, finalPath)));
    }

    public void Dispose()
    {
        if (Directory.Exists(_testRoot))
        {
            Directory.Delete(_testRoot, recursive: true);
        }
    }

    private ManagedCache CreateCache() =>
        new(new ManagedCachePaths(_testRoot));
}
