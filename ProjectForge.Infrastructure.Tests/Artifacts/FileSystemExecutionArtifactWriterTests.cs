using System.Text.Json;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Infrastructure.Artifacts;

namespace ProjectForge.Infrastructure.Tests.Artifacts;

public sealed class FileSystemExecutionArtifactWriterTests : IDisposable
{
    private static readonly Guid WorkflowId =
        Guid.Parse("a9e8968c-30f6-4ff3-948d-849b2c44b184");

    private static readonly Guid RequestId =
        Guid.Parse("56c164ab-03ec-493e-ad8b-05672991833c");

    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "ProjectForge.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task WritesDeterministicallyNamedMarkdownAndJsonArtifacts()
    {
        var writer = new FileSystemExecutionArtifactWriter(_directory);

        var artifacts = await writer.WriteAsync(WorkflowId, SuccessfulResult());

        var expectedDirectory = Path.Combine(_directory, WorkflowId.ToString("D"));
        Assert.Equal(
            Path.Combine(expectedDirectory, $"{RequestId:D}.md"),
            artifacts.MarkdownPath);
        Assert.Equal(
            Path.Combine(expectedDirectory, $"{RequestId:D}.json"),
            artifacts.JsonPath);
        Assert.True(File.Exists(artifacts.MarkdownPath));
        Assert.True(File.Exists(artifacts.JsonPath));

        var markdown = await File.ReadAllTextAsync(artifacts.MarkdownPath);
        Assert.Contains("# Capability execution result", markdown);
        Assert.Contains("Completed the bounded task.", markdown);
        Assert.Contains("Generated output", markdown);

        await using var jsonStream = File.OpenRead(artifacts.JsonPath);
        using var json = await JsonDocument.ParseAsync(jsonStream);
        Assert.Equal(
            WorkflowId,
            json.RootElement.GetProperty("workflowId").GetGuid());
        Assert.Equal(
            RequestId,
            json.RootElement.GetProperty("requestId").GetGuid());
        Assert.True(json.RootElement.GetProperty("isSuccessful").GetBoolean());
        Assert.Equal(
            "mock-local",
            json.RootElement.GetProperty("providerName").GetString());
    }

    [Fact]
    public async Task RepeatedWriteReplacesArtifactsWithoutTemporaryFiles()
    {
        var writer = new FileSystemExecutionArtifactWriter(_directory);
        await writer.WriteAsync(WorkflowId, SuccessfulResult("first"));

        var artifacts = await writer.WriteAsync(
            WorkflowId,
            SuccessfulResult("replacement"));

        Assert.Contains(
            "replacement",
            await File.ReadAllTextAsync(artifacts.MarkdownPath));
        Assert.Empty(
            Directory.EnumerateFiles(
                Path.GetDirectoryName(artifacts.MarkdownPath)!,
                "*.tmp"));
    }

    [Fact]
    public async Task RejectsFailedExecutionWithoutCreatingArtifacts()
    {
        var writer = new FileSystemExecutionArtifactWriter(_directory);
        var failed = ExecutionResult(
            isSuccessful: false,
            errorMessage: "Provider failed.");

        await Assert.ThrowsAsync<ArgumentException>(
            () => writer.WriteAsync(WorkflowId, failed));

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task PreCanceledWriteDoesNotCreateArtifacts()
    {
        var writer = new FileSystemExecutionArtifactWriter(_directory);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => writer.WriteAsync(
                WorkflowId,
                SuccessfulResult(),
                cancellation.Token));

        Assert.False(Directory.Exists(_directory));
    }

    [Fact]
    public async Task MetadataOrderIsStableAcrossFormats()
    {
        var writer = new FileSystemExecutionArtifactWriter(_directory);
        var result = ExecutionResult(
            metadata: new Dictionary<string, string>
            {
                ["zeta"] = "last",
                ["alpha"] = "first"
            });

        var artifacts = await writer.WriteAsync(WorkflowId, result);

        var markdown = await File.ReadAllTextAsync(artifacts.MarkdownPath);
        var json = await File.ReadAllTextAsync(artifacts.JsonPath);
        Assert.True(
            markdown.IndexOf("alpha", StringComparison.Ordinal) <
            markdown.IndexOf("zeta", StringComparison.Ordinal));
        Assert.True(
            json.IndexOf("alpha", StringComparison.Ordinal) <
            json.IndexOf("zeta", StringComparison.Ordinal));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    private static CapabilityExecutionResult SuccessfulResult(
        string output = "Generated output") =>
        ExecutionResult(output: output);

    private static CapabilityExecutionResult ExecutionResult(
        bool isSuccessful = true,
        string output = "Generated output",
        string? errorMessage = null,
        IReadOnlyDictionary<string, string>? metadata = null) =>
        new()
        {
            RequestId = RequestId,
            ProviderName = "mock-local",
            IsSuccessful = isSuccessful,
            Summary = "Completed the bounded task.",
            Output = output,
            ErrorMessage = errorMessage,
            StartedAtUtc = new DateTimeOffset(
                2026,
                7,
                23,
                10,
                0,
                0,
                TimeSpan.Zero),
            CompletedAtUtc = new DateTimeOffset(
                2026,
                7,
                23,
                10,
                0,
                2,
                TimeSpan.Zero),
            EstimatedCost = 0m,
            Metadata = metadata ?? new Dictionary<string, string>()
        };
}
