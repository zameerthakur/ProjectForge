namespace ProjectForge.Infrastructure.Provisioning.Cache;

public sealed class ManagedCache
{
    private const string OwnershipMarker = ".projectforge-staging";
    private readonly ManagedCachePaths _paths;
    private readonly TimeProvider _timeProvider;

    public ManagedCache(ManagedCachePaths paths, TimeProvider? timeProvider = null)
    {
        _paths = paths ?? throw new ArgumentNullException(nameof(paths));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public ManagedCacheStaging CreateStaging(
        string artifactId,
        string version,
        string platform)
    {
        var finalPath = _paths.Resolve(artifactId, version, platform);
        var parent = Path.GetDirectoryName(finalPath)
            ?? throw new InvalidOperationException("The cache path has no parent.");
        Directory.CreateDirectory(parent);
        _paths.EnsureExistingPathHasNoReparsePoints(parent);

        var stagingPath = finalPath + ".partial-" + Guid.NewGuid().ToString("N");
        Directory.CreateDirectory(stagingPath);
        File.WriteAllText(
            Path.Combine(stagingPath, OwnershipMarker),
            _timeProvider.GetUtcNow().ToString("O"));

        return new ManagedCacheStaging(stagingPath, finalPath);
    }

    public string PromoteVerified(ManagedCacheStaging staging)
    {
        ArgumentNullException.ThrowIfNull(staging);
        ValidateOwnedStaging(staging);

        if (Directory.Exists(staging.FinalPath) || File.Exists(staging.FinalPath))
        {
            throw new IOException($"The final cache entry '{staging.FinalPath}' already exists.");
        }

        Directory.Move(staging.StagingPath, staging.FinalPath);
        File.Delete(Path.Combine(staging.FinalPath, OwnershipMarker));
        return staging.FinalPath;
    }

    public IReadOnlyList<string> CleanOwnedStaging(
        string artifactId,
        string version,
        string platform,
        TimeSpan staleAfter)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(
            staleAfter,
            TimeSpan.Zero);

        var finalPath = _paths.Resolve(artifactId, version, platform);
        var parent = Path.GetDirectoryName(finalPath)!;
        if (!Directory.Exists(parent))
        {
            return [];
        }

        _paths.EnsureExistingPathHasNoReparsePoints(parent);
        var cutoff = _timeProvider.GetUtcNow() - staleAfter;
        var removed = new List<string>();
        var prefix = Path.GetFileName(finalPath) + ".partial-";

        foreach (var candidate in Directory.EnumerateDirectories(parent, prefix + "*"))
        {
            _paths.EnsureContained(candidate);
            var info = new DirectoryInfo(candidate);
            var suffix = info.Name[prefix.Length..];
            if (!Guid.TryParseExact(suffix, "N", out _) ||
                info.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                continue;
            }

            var marker = Path.Combine(candidate, OwnershipMarker);
            if (!File.Exists(marker) || File.GetLastWriteTimeUtc(marker) > cutoff.UtcDateTime)
            {
                continue;
            }

            Directory.Delete(candidate, recursive: true);
            removed.Add(candidate);
        }

        return removed;
    }

    private void ValidateOwnedStaging(ManagedCacheStaging staging)
    {
        _paths.EnsureContained(staging.StagingPath);
        _paths.EnsureContained(staging.FinalPath);
        _paths.EnsureExistingPathHasNoReparsePoints(staging.StagingPath);

        var expectedPrefix = staging.FinalPath + ".partial-";
        if (!staging.StagingPath.StartsWith(expectedPrefix, StringComparison.OrdinalIgnoreCase) ||
            !Directory.Exists(staging.StagingPath) ||
            !File.Exists(Path.Combine(staging.StagingPath, OwnershipMarker)))
        {
            throw new InvalidOperationException("The staging directory is not owned by ProjectForge.");
        }
    }
}

public sealed record ManagedCacheStaging(string StagingPath, string FinalPath);
