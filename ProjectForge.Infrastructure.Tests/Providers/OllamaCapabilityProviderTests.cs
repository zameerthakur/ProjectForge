using System.Net;
using System.Text;
using System.Text.Json;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Infrastructure.Providers;

namespace ProjectForge.Infrastructure.Tests.Providers;

public sealed class OllamaCapabilityProviderTests
{
    private const string Digest =
        "0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef";
    private static readonly string[] ReadinessPaths =
        ["/api/version", "/api/tags", "/api/show"];

    [Fact]
    public async Task ReportsReadyForAPinnedCompatibleCompletionModel()
    {
        var handler = Handler(
            Json("""{"version":"0.10.1"}"""),
            Json($$"""{"models":[{"name":"qwen:1b","digest":"sha256:{{Digest}}"}]}"""),
            Json("""{"capabilities":["completion"]}"""));

        var health = await Provider(handler).CheckHealthAsync();

        Assert.True(health.IsHealthy);
        Assert.Equal("0.10.1", health.Metadata["runtime.version"]);
        Assert.Equal(Digest, health.Metadata["model.digest"]);
        Assert.Equal(
            ReadinessPaths,
            handler.Paths);
    }

    [Fact]
    public async Task RejectsAMissingModel()
    {
        var health = await Provider(Handler(
            Json("""{"version":"0.10.1"}"""),
            Json("""{"models":[]}"""))).CheckHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Contains("not installed", health.StatusMessage);
    }

    [Fact]
    public async Task RejectsADigestMismatch()
    {
        var health = await Provider(Handler(
            Json("""{"version":"0.10.1"}"""),
            Json("""{"models":[{"name":"qwen:1b","digest":"bad"}]}"""),
            Json("""{"capabilities":["completion"]}"""))).CheckHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Contains("integrity", health.StatusMessage);
    }

    [Fact]
    public async Task RejectsAnIncompatibleRuntimeVersion()
    {
        var health = await Provider(Handler(
            Json("""{"version":"1.0.0"}"""))).CheckHealthAsync();

        Assert.False(health.IsHealthy);
        Assert.Contains("version", health.StatusMessage);
    }

    [Fact]
    public async Task SanitizesHttpAndMalformedResponses()
    {
        var failedHealth = await Provider(Handler(
            new HttpResponseMessage(HttpStatusCode.InternalServerError)))
            .CheckHealthAsync();
        var failedExecution = await Provider(Handler(
            Json("""{"message":"""))).ExecuteAsync(Request());

        Assert.False(failedHealth.IsHealthy);
        Assert.DoesNotContain("500", failedHealth.StatusMessage);
        Assert.False(failedExecution.IsSuccessful);
        Assert.Equal(
            "The local Ollama provider could not complete the request.",
            failedExecution.ErrorMessage);
    }

    [Fact]
    public async Task ExecutesNonStreamingChatWithTokenMetadata()
    {
        var handler = Handler(Json(
            """
            {
              "message":{"role":"assistant","content":"bounded answer"},
              "prompt_eval_count":12,
              "eval_count":7
            }
            """));

        var result = await Provider(handler).ExecuteAsync(Request());

        Assert.True(result.IsSuccessful);
        Assert.Equal("bounded answer", result.Output);
        Assert.Equal("12", result.Metadata["tokens.prompt"]);
        Assert.Equal("7", result.Metadata["tokens.completion"]);
        Assert.Contains(@"""stream"":false", handler.Bodies.Single());
        Assert.Contains(@"""model"":""qwen:1b""", handler.Bodies.Single());
    }

    [Fact]
    public async Task PropagatesTransportCancellation()
    {
        var provider = Provider(new DelegateHandler(async (_, cancellation) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            throw new InvalidOperationException();
        }));
        using var cancellation = new CancellationTokenSource();

        var execution = provider.ExecuteAsync(Request(), cancellation.Token);
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => execution);
    }

    [Fact]
    public async Task RejectsInvalidRequestsAndStructuredInputs()
    {
        var provider = Provider(Handler());
        var invalid = Request(instruction: " ");
        var inputs = Request(
            inputs: new Dictionary<string, string> { ["context"] = "ignored" });

        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ExecuteAsync(invalid));
        await Assert.ThrowsAsync<ArgumentException>(
            () => provider.ExecuteAsync(inputs));
    }

    [Fact]
    public void RequiresLoopbackAndAHexadecimalPinnedDigest()
    {
        Assert.Throws<ArgumentException>(() => Provider(
            Handler(),
            Options() with { Endpoint = new Uri("http://example.test") }));
        Assert.Throws<ArgumentException>(() => Provider(
            Handler(),
            Options() with { ExpectedDigest = new string('z', 64) }));
    }

    [Fact]
    public async Task RejectsResponsesBeyondTheConfiguredLimit()
    {
        var provider = Provider(
            Handler(Json("""{"message":{"content":"too large"}}""")),
            Options() with { MaximumResponseBytes = 10 });

        var result = await provider.ExecuteAsync(Request());

        Assert.False(result.IsSuccessful);
        Assert.Equal(
            "The local Ollama provider could not complete the request.",
            result.ErrorMessage);
    }

    private static OllamaCapabilityProvider Provider(
        HttpMessageHandler handler,
        OllamaProviderOptions? options = null) =>
        new(new HttpClient(handler), options ?? Options());

    private static OllamaProviderOptions Options() => new()
    {
        Endpoint = new Uri("http://127.0.0.1:11434"),
        Model = "qwen:1b",
        ExpectedDigest = Digest,
        MinimumRuntimeVersion = new Version(0, 10, 0),
        MaximumRuntimeVersionExclusive = new Version(0, 11, 0)
    };

    private static CapabilityExecutionRequest Request(
        string instruction = "Review the design.",
        IReadOnlyDictionary<string, string>? inputs = null) =>
        new()
        {
            RequestId = Guid.Parse("e8581193-3eb6-4685-97b2-af12d306b716"),
            WorkflowId = "workflow-1",
            TaskId = "task-1",
            TaskName = "Review",
            Instruction = instruction,
            Requirement = new CapabilityRequirement
            {
                Capability = EngineeringCapability.ArchitectureReview,
                AllowCloudExecution = false
            },
            Inputs = inputs ?? new Dictionary<string, string>()
        };

    private static QueueHandler Handler(params HttpResponseMessage[] responses) =>
        new(responses);

    private static HttpResponseMessage Json(string content) =>
        new(HttpStatusCode.OK)
        {
            Content = new StringContent(
                content,
                Encoding.UTF8,
                "application/json")
        };

    private sealed class QueueHandler(IEnumerable<HttpResponseMessage> responses)
        : HttpMessageHandler
    {
        private readonly Queue<HttpResponseMessage> _responses = new(responses);

        public List<string> Paths { get; } = [];

        public List<string> Bodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.AbsolutePath);
            if (request.Content is not null)
            {
                Bodies.Add(await request.Content.ReadAsStringAsync(
                    cancellationToken));
            }

            return _responses.Dequeue();
        }
    }

    private sealed class DelegateHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>
            handler)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            handler(request, cancellationToken);
    }
}
