using System.Globalization;
using System.Text;
using System.Text.Json;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Application.Artifacts;

namespace ProjectForge.Infrastructure.Artifacts;

/// <summary>
/// Writes execution artifacts beneath an application-owned filesystem root.
/// </summary>
public sealed class FileSystemExecutionArtifactWriter : IExecutionArtifactWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new(
        JsonSerializerDefaults.Web)
    {
        WriteIndented = true
    };

    private readonly string _artifactRoot;

    /// <summary>
    /// Initializes a new instance of the
    /// <see cref="FileSystemExecutionArtifactWriter"/> class.
    /// </summary>
    /// <param name="artifactRoot">
    /// The application-owned root beneath which artifacts are written.
    /// </param>
    public FileSystemExecutionArtifactWriter(string artifactRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(artifactRoot);
        _artifactRoot = Path.TrimEndingDirectorySeparator(
            Path.GetFullPath(artifactRoot));
    }

    /// <inheritdoc />
    public async Task<ArtifactWriteResult> WriteAsync(
        Guid workflowId,
        CapabilityExecutionResult result,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(result);

        if (workflowId == Guid.Empty)
        {
            throw new ArgumentException(
                "A workflow identifier is required.",
                nameof(workflowId));
        }

        if (result.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "The execution result must identify its request.",
                nameof(result));
        }

        if (!result.IsSuccessful)
        {
            throw new ArgumentException(
                "Artifacts can only be written for a successful execution.",
                nameof(result));
        }

        cancellationToken.ThrowIfCancellationRequested();

        var directory = ContainedPath(
            _artifactRoot,
            workflowId.ToString("D", CultureInfo.InvariantCulture));
        var baseName = result.RequestId.ToString(
            "D",
            CultureInfo.InvariantCulture);
        var markdownPath = ContainedPath(directory, $"{baseName}.md");
        var jsonPath = ContainedPath(directory, $"{baseName}.json");

        Directory.CreateDirectory(directory);

        var markdown = RenderMarkdown(workflowId, result);
        var json = JsonSerializer.Serialize(
            ArtifactDocument.From(workflowId, result),
            JsonOptions);

        string? markdownTemporaryPath = null;
        string? jsonTemporaryPath = null;
        try
        {
            markdownTemporaryPath = await StageAsync(
                markdownPath,
                markdown,
                cancellationToken);
            jsonTemporaryPath = await StageAsync(
                jsonPath,
                json,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            File.Move(
                markdownTemporaryPath,
                markdownPath,
                overwrite: true);
            markdownTemporaryPath = null;
            File.Move(jsonTemporaryPath, jsonPath, overwrite: true);
            jsonTemporaryPath = null;
        }
        finally
        {
            DeleteTemporaryFile(markdownTemporaryPath);
            DeleteTemporaryFile(jsonTemporaryPath);
        }

        return new ArtifactWriteResult
        {
            MarkdownPath = markdownPath,
            JsonPath = jsonPath
        };
    }

    private static string ContainedPath(string parent, string child)
    {
        var path = Path.GetFullPath(Path.Combine(parent, child));
        var relative = Path.GetRelativePath(parent, path);
        if (Path.IsPathRooted(relative) ||
            relative.Equals("..", StringComparison.Ordinal) ||
            relative.StartsWith(
                $"..{Path.DirectorySeparatorChar}",
                StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                "The artifact path must remain beneath its configured root.");
        }

        return path;
    }

    private static async Task<string> StageAsync(
        string destinationPath,
        string contents,
        CancellationToken cancellationToken)
    {
        var temporaryPath = $"{destinationPath}.{Guid.NewGuid():N}.tmp";
        try
        {
            await using (var stream = new FileStream(
                             temporaryPath,
                             FileMode.CreateNew,
                             FileAccess.Write,
                             FileShare.None,
                             bufferSize: 4096,
                             FileOptions.Asynchronous |
                             FileOptions.WriteThrough))
            await using (var writer = new StreamWriter(
                             stream,
                             new UTF8Encoding(encoderShouldEmitUTF8Identifier: false)))
            {
                await writer.WriteAsync(contents.AsMemory(), cancellationToken);
                await writer.FlushAsync(cancellationToken);
                stream.Flush(flushToDisk: true);
            }

            return temporaryPath;
        }
        catch
        {
            DeleteTemporaryFile(temporaryPath);
            throw;
        }
    }

    private static void DeleteTemporaryFile(string? temporaryPath)
    {
        if (temporaryPath is not null)
        {
            File.Delete(temporaryPath);
        }
    }

    private static string RenderMarkdown(
        Guid workflowId,
        CapabilityExecutionResult result)
    {
        var builder = new StringBuilder();
        builder.AppendLine("# Capability execution result");
        builder.AppendLine();
        AppendField(builder, "Workflow", workflowId.ToString("D"));
        AppendField(builder, "Request", result.RequestId.ToString("D"));
        AppendField(builder, "Provider", result.ProviderName);
        AppendField(builder, "Status", "Successful");
        AppendField(builder, "Started (UTC)", Format(result.StartedAtUtc));
        AppendField(builder, "Completed (UTC)", Format(result.CompletedAtUtc));
        AppendField(
            builder,
            "Duration",
            result.Duration.ToString("c", CultureInfo.InvariantCulture));
        AppendField(
            builder,
            "Estimated cost",
            result.EstimatedCost.ToString(CultureInfo.InvariantCulture));

        AppendSection(builder, "Summary", result.Summary);
        AppendSection(builder, "Output", result.Output);

        if (result.Metadata.Count > 0)
        {
            builder.AppendLine("## Metadata");
            builder.AppendLine();
            foreach (var pair in result.Metadata.OrderBy(
                         pair => pair.Key,
                         StringComparer.Ordinal))
            {
                AppendField(builder, pair.Key, pair.Value);
            }
        }

        return builder.ToString();
    }

    private static void AppendField(
        StringBuilder builder,
        string name,
        string? value)
    {
        builder.Append("- **");
        builder.Append(name);
        builder.Append(":** ");
        builder.AppendLine(value ?? string.Empty);
    }

    private static void AppendSection(
        StringBuilder builder,
        string heading,
        string? value)
    {
        builder.AppendLine();
        builder.Append("## ");
        builder.AppendLine(heading);
        builder.AppendLine();
        builder.AppendLine(value ?? string.Empty);
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private sealed class ArtifactDocument
    {
        public required Guid WorkflowId { get; init; }

        public required Guid RequestId { get; init; }

        public required string ProviderName { get; init; }

        public required bool IsSuccessful { get; init; }

        public string? Summary { get; init; }

        public string? Output { get; init; }

        public required DateTimeOffset StartedAtUtc { get; init; }

        public required DateTimeOffset CompletedAtUtc { get; init; }

        public required TimeSpan Duration { get; init; }

        public required decimal EstimatedCost { get; init; }

        public required IReadOnlyDictionary<string, string> Metadata { get; init; }

        public static ArtifactDocument From(
            Guid workflowId,
            CapabilityExecutionResult result) =>
            new()
            {
                WorkflowId = workflowId,
                RequestId = result.RequestId,
                ProviderName = result.ProviderName,
                IsSuccessful = result.IsSuccessful,
                Summary = result.Summary,
                Output = result.Output,
                StartedAtUtc = result.StartedAtUtc,
                CompletedAtUtc = result.CompletedAtUtc,
                Duration = result.Duration,
                EstimatedCost = result.EstimatedCost,
                Metadata = SortMetadata(result.Metadata)
            };

        private static SortedDictionary<string, string> SortMetadata(
            IReadOnlyDictionary<string, string> metadata)
        {
            var sorted = new SortedDictionary<string, string>(
                StringComparer.Ordinal);
            foreach (var pair in metadata)
            {
                sorted.Add(pair.Key, pair.Value);
            }

            return sorted;
        }
    }
}
