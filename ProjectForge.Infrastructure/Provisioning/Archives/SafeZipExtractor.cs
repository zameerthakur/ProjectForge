using System.IO.Compression;

namespace ProjectForge.Infrastructure.Provisioning.Archives;

public sealed class SafeZipExtractor
{
    private const string OwnershipMarker = ".projectforge-staging";
    private const int UnixFileTypeMask = 0xF000;
    private const int UnixRegularFile = 0x8000;
    private const int UnixDirectory = 0x4000;
    private const int DosReparsePoint = 0x0400;

    private readonly SafeZipExtractionOptions _options;

    public SafeZipExtractor(SafeZipExtractionOptions? options = null)
    {
        _options = options ?? new SafeZipExtractionOptions();
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaximumEntryCount, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaximumEntryBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(_options.MaximumExpandedBytes, 1);
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(_options.MaximumCompressionRatio, 0);
    }

    public async Task ExtractAsync(
        Stream archiveStream,
        string stagingDirectory,
        string expectedEntrypoint,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(archiveStream);
        ArgumentException.ThrowIfNullOrWhiteSpace(stagingDirectory);
        ArgumentException.ThrowIfNullOrWhiteSpace(expectedEntrypoint);

        var root = ValidateOwnedEmptyStaging(stagingDirectory);
        var expected = NormalizeEntryName(expectedEntrypoint, allowDirectory: false);
        var createdFiles = new List<string>();
        var createdDirectories = new List<string>();

        try
        {
            using var archive = new ZipArchive(
                archiveStream,
                ZipArchiveMode.Read,
                leaveOpen: true);
            var entries = ValidateArchive(archive, expected);

            long expandedBytes = 0;
            foreach (var item in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var destination = ResolveDestination(root, item.Name);

                if (item.IsDirectory)
                {
                    CreateDirectories(root, destination, createdDirectories);
                    continue;
                }

                var parent = Path.GetDirectoryName(destination)!;
                CreateDirectories(root, parent, createdDirectories);
                EnsurePathHasNoReparsePoints(root, parent);

                if (File.Exists(destination) || Directory.Exists(destination))
                {
                    throw new InvalidDataException(
                        $"Archive entry '{item.Name}' would overwrite an existing path.");
                }

                await using var source = item.Entry.Open();
                await using var target = new FileStream(
                    destination,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    bufferSize: 81920,
                    FileOptions.Asynchronous);
                createdFiles.Add(destination);

                var buffer = new byte[81920];
                long entryBytes = 0;
                while (true)
                {
                    var read = await source.ReadAsync(buffer, cancellationToken);
                    if (read == 0)
                    {
                        break;
                    }

                    entryBytes = checked(entryBytes + read);
                    expandedBytes = checked(expandedBytes + read);
                    if (entryBytes > _options.MaximumEntryBytes ||
                        expandedBytes > _options.MaximumExpandedBytes ||
                        entryBytes > item.Entry.Length)
                    {
                        throw new InvalidDataException(
                            $"Archive entry '{item.Name}' exceeded its extraction limit.");
                    }

                    await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                }

                if (entryBytes != item.Entry.Length)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{item.Name}' did not match its declared length.");
                }
            }
        }
        catch
        {
            CleanupCreatedOutputs(root, createdFiles, createdDirectories);
            throw;
        }
    }

    private List<ValidatedEntry> ValidateArchive(
        ZipArchive archive,
        string expectedEntrypoint)
    {
        if (archive.Entries.Count > _options.MaximumEntryCount)
        {
            throw new InvalidDataException("The archive contains too many entries.");
        }

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var files = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var validated = new List<ValidatedEntry>(archive.Entries.Count);
        long totalLength = 0;
        var foundEntrypoint = false;

        foreach (var entry in archive.Entries)
        {
            var isDirectory = entry.FullName.EndsWith('/');
            var name = NormalizeEntryName(entry.FullName, isDirectory);
            RejectSpecialFile(entry, isDirectory);

            if (!seen.Add(name))
            {
                throw new InvalidDataException(
                    $"Archive entry '{entry.FullName}' duplicates another path.");
            }

            foreach (var parent in EnumerateParents(name))
            {
                if (files.Contains(parent))
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' conflicts with a file path.");
                }
            }

            if (!isDirectory)
            {
                if (seen.Any(candidate =>
                    candidate.Length > name.Length &&
                    candidate.StartsWith(name + "/", StringComparison.OrdinalIgnoreCase)))
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' conflicts with a directory path.");
                }

                files.Add(name);
                if (entry.Length > _options.MaximumEntryBytes)
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' is too large.");
                }

                totalLength = checked(totalLength + entry.Length);
                if (totalLength > _options.MaximumExpandedBytes)
                {
                    throw new InvalidDataException("The archive expands beyond the allowed size.");
                }

                if (entry.Length > 0 &&
                    (entry.CompressedLength == 0 ||
                     entry.Length / (double)entry.CompressedLength >
                     _options.MaximumCompressionRatio))
                {
                    throw new InvalidDataException(
                        $"Archive entry '{entry.FullName}' exceeds the compression-ratio limit.");
                }

                foundEntrypoint |= string.Equals(
                    name,
                    expectedEntrypoint,
                    StringComparison.OrdinalIgnoreCase);
            }

            validated.Add(new ValidatedEntry(entry, name, isDirectory));
        }

        if (!foundEntrypoint)
        {
            throw new InvalidDataException(
                $"The expected entrypoint '{expectedEntrypoint}' is missing.");
        }

        return validated;
    }

    private static string ValidateOwnedEmptyStaging(string stagingDirectory)
    {
        var root = Path.GetFullPath(stagingDirectory);
        if (!Directory.Exists(root) ||
            !File.Exists(Path.Combine(root, OwnershipMarker)))
        {
            throw new InvalidOperationException(
                "Extraction requires an existing ProjectForge-owned staging directory.");
        }

        EnsurePathHasNoReparsePoints(root, root);
        var contents = Directory.EnumerateFileSystemEntries(root).ToArray();
        if (contents.Length != 1 ||
            !string.Equals(
                Path.GetFileName(contents[0]),
                OwnershipMarker,
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "Extraction requires an otherwise empty staging directory.");
        }

        return root.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    private static string NormalizeEntryName(string name, bool allowDirectory)
    {
        if (string.IsNullOrWhiteSpace(name) ||
            name.Contains('\\', StringComparison.Ordinal) ||
            name.Contains(':', StringComparison.Ordinal) ||
            name.StartsWith('/') ||
            Path.IsPathRooted(name))
        {
            throw new InvalidDataException($"Archive entry '{name}' has an unsafe path.");
        }

        var normalized = allowDirectory ? name.TrimEnd('/') : name;
        var segments = normalized.Split('/');
        if (segments.Length == 0 ||
            segments.Any(segment =>
                string.IsNullOrWhiteSpace(segment) ||
                segment is "." or ".."))
        {
            throw new InvalidDataException($"Archive entry '{name}' has an unsafe path.");
        }

        return string.Join('/', segments);
    }

    private static void RejectSpecialFile(ZipArchiveEntry entry, bool isDirectory)
    {
        var unixMode = (entry.ExternalAttributes >> 16) & UnixFileTypeMask;
        if ((entry.ExternalAttributes & DosReparsePoint) != 0 ||
            (unixMode != 0 &&
             unixMode != (isDirectory ? UnixDirectory : UnixRegularFile)))
        {
            throw new InvalidDataException(
                $"Archive entry '{entry.FullName}' is a link or special file.");
        }
    }

    private static IEnumerable<string> EnumerateParents(string name)
    {
        var separator = name.IndexOf('/');
        while (separator >= 0)
        {
            yield return name[..separator];
            separator = name.IndexOf('/', separator + 1);
        }
    }

    private static string ResolveDestination(string root, string entryName)
    {
        var destination = Path.GetFullPath(
            Path.Combine(root, entryName.Replace('/', Path.DirectorySeparatorChar)));
        var prefix = root + Path.DirectorySeparatorChar;
        if (!destination.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidDataException(
                $"Archive entry '{entryName}' escapes the staging directory.");
        }

        return destination;
    }

    private static void CreateDirectories(
        string root,
        string directory,
        List<string> createdDirectories)
    {
        if (string.Equals(root, directory, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        var relative = Path.GetRelativePath(root, directory);
        var current = root;
        foreach (var segment in relative.Split(
                     Path.DirectorySeparatorChar,
                     StringSplitOptions.RemoveEmptyEntries))
        {
            current = Path.Combine(current, segment);
            if (Directory.Exists(current))
            {
                if (new DirectoryInfo(current).Attributes.HasFlag(FileAttributes.ReparsePoint))
                {
                    throw new InvalidDataException("The extraction path contains a reparse point.");
                }

                continue;
            }

            if (File.Exists(current))
            {
                throw new InvalidDataException("The extraction path collides with a file.");
            }

            Directory.CreateDirectory(current);
            createdDirectories.Add(current);
        }
    }

    private static void EnsurePathHasNoReparsePoints(string root, string path)
    {
        var current = new DirectoryInfo(path);
        var rootPath = Path.GetFullPath(root);
        while (current is not null)
        {
            if (current.Attributes.HasFlag(FileAttributes.ReparsePoint))
            {
                throw new InvalidOperationException(
                    "The staging path contains a reparse point.");
            }

            if (string.Equals(
                    current.FullName.TrimEnd(Path.DirectorySeparatorChar),
                    rootPath.TrimEnd(Path.DirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            current = current.Parent;
        }

        throw new InvalidOperationException("The path is outside the staging directory.");
    }

    private static void CleanupCreatedOutputs(
        string root,
        IEnumerable<string> files,
        IEnumerable<string> directories)
    {
        foreach (var file in files.Reverse())
        {
            try
            {
                if (File.Exists(file) &&
                    ResolveDestination(root, Path.GetRelativePath(root, file)) == file)
                {
                    File.Delete(file);
                }
            }
            catch
            {
                // Preserve the original extraction failure.
            }
        }

        foreach (var directory in directories.Reverse())
        {
            try
            {
                if (Directory.Exists(directory) &&
                    !Directory.EnumerateFileSystemEntries(directory).Any())
                {
                    Directory.Delete(directory);
                }
            }
            catch
            {
                // Preserve the original extraction failure.
            }
        }
    }

    private sealed record ValidatedEntry(
        ZipArchiveEntry Entry,
        string Name,
        bool IsDirectory);
}
