namespace ProjectForge.Infrastructure.Provisioning.Cache;

public sealed class ManagedCachePaths
{
    private static readonly char[] AdditionalInvalidCharacters = ['/', '\\', ':'];
    private readonly string _rootWithSeparator;

    public ManagedCachePaths(string root)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(root);
        Root = Path.GetFullPath(root);
        _rootWithSeparator = Root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
            + Path.DirectorySeparatorChar;
    }

    public string Root { get; }

    public string Resolve(string artifactId, string version, string platform)
    {
        ValidateSegment(artifactId, nameof(artifactId));
        ValidateSegment(version, nameof(version));
        ValidateSegment(platform, nameof(platform));

        var path = Path.GetFullPath(Path.Combine(Root, artifactId, version, platform));
        EnsureContained(path);
        EnsureExistingPathHasNoReparsePoints(path);
        return path;
    }

    internal void EnsureContained(string path)
    {
        var fullPath = Path.GetFullPath(path);
        if (!fullPath.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("The managed cache path escapes the application-owned root.");
        }
    }

    internal void EnsureExistingPathHasNoReparsePoints(string path)
    {
        var current = new DirectoryInfo(Path.GetFullPath(path));
        while (current is not null &&
               current.FullName.StartsWith(_rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            if (current.Exists && current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    $"Managed cache path '{current.FullName}' contains a reparse point.");
            }

            current = current.Parent;
        }

        if (Directory.Exists(Root) &&
            new DirectoryInfo(Root).Attributes.HasFlag(FileAttributes.ReparsePoint))
        {
            throw new InvalidOperationException("The managed cache root cannot be a reparse point.");
        }
    }

    private static void ValidateSegment(string value, string parameterName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value, parameterName);
        if (Path.IsPathRooted(value) ||
            value is "." or ".." ||
            value.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ||
            value.IndexOfAny(AdditionalInvalidCharacters) >= 0)
        {
            throw new ArgumentException(
                "Cache identifiers must be stable, single path segments.",
                parameterName);
        }
    }
}
