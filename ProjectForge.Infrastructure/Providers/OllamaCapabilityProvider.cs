using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Infrastructure.Providers;

/// <summary>Executes bounded text tasks through a pinned local Ollama model.</summary>
public sealed class OllamaCapabilityProvider : ISchedulableCapabilityProvider
{
    private const string FailureMessage =
        "The local Ollama provider could not complete the request.";
    private static readonly IReadOnlyCollection<EngineeringCapability>
        Capabilities = Array.AsReadOnly(
            new[]
            {
                EngineeringCapability.Discovery,
                EngineeringCapability.ArchitectureReview,
                EngineeringCapability.Design,
                EngineeringCapability.Planning,
                EngineeringCapability.Coding,
                EngineeringCapability.Testing,
                EngineeringCapability.SecurityReview,
                EngineeringCapability.Documentation,
                EngineeringCapability.Maintenance
            });
    private static readonly JsonSerializerOptions SerializerOptions =
        new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly OllamaProviderOptions _options;
    private readonly TimeProvider _timeProvider;

    /// <summary>Initializes an Ollama provider with an injected HTTP transport.</summary>
    public OllamaCapabilityProvider(
        HttpClient httpClient,
        OllamaProviderOptions options,
        TimeProvider? timeProvider = null)
    {
        ArgumentNullException.ThrowIfNull(httpClient);
        ArgumentNullException.ThrowIfNull(options);
        ValidateOptions(options);
        _httpClient = httpClient;
        _options = options;
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    /// <inheritdoc />
    public string Name => "ollama";

    /// <inheritdoc />
    public IReadOnlyCollection<EngineeringCapability> SupportedCapabilities =>
        Capabilities;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new()
    {
        Name = "ollama",
        ExecutionLocation = ProviderExecutionLocation.LocalProcess,
        SupportsRepositoryAccess = false,
        SupportsFileWriteAccess = false,
        SupportsToolExecution = false
    };

    /// <inheritdoc />
    public async Task<bool> CanExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        if (!Capabilities.Contains(request.Requirement.Capability))
        {
            return false;
        }

        var health = await CheckHealthAsync(cancellationToken);
        return health.IsHealthy;
    }

    /// <inheritdoc />
    public Task<decimal> EstimateCostAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(0m);
    }

    /// <inheritdoc />
    public async Task<CapabilityExecutionResult> ExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();
        var startedAt = _timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();

        try
        {
            using var message = new HttpRequestMessage(
                HttpMethod.Post,
                Endpoint("api/chat"))
            {
                Content = JsonContent.Create(
                    new ChatRequest(
                        _options.Model,
                        false,
                        new[] { new ChatMessage("user", request.Instruction) }))
            };
            using var response = await _httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            response.EnsureSuccessStatusCode();
            var result = await ReadJsonAsync<ChatResponse>(
                response,
                cancellationToken);
            if (string.IsNullOrWhiteSpace(result.Message?.Content))
            {
                throw new InvalidDataException("Ollama returned no content.");
            }

            var completedAt = _timeProvider.GetUtcNow();
            return new CapabilityExecutionResult
            {
                RequestId = request.RequestId,
                ProviderName = Name,
                IsSuccessful = true,
                Summary = $"Completed local Ollama execution using model " +
                    $"'{_options.Model}'.",
                Output = result.Message.Content,
                StartedAtUtc = startedAt,
                CompletedAtUtc = completedAt,
                EstimatedCost = 0m,
                Metadata = Metadata(
                    stopwatch.Elapsed,
                    result.PromptEvalCount,
                    result.EvalCount)
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or
            InvalidDataException)
        {
            var completedAt = _timeProvider.GetUtcNow();
            return new CapabilityExecutionResult
            {
                RequestId = request.RequestId,
                ProviderName = Name,
                IsSuccessful = false,
                ErrorMessage = FailureMessage,
                StartedAtUtc = startedAt,
                CompletedAtUtc = completedAt,
                EstimatedCost = 0m,
                Metadata = Metadata(stopwatch.Elapsed, null, null)
            };
        }
    }

    /// <inheritdoc />
    public async Task<ProviderHealthReport> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        var startedAt = _timeProvider.GetUtcNow();
        var stopwatch = Stopwatch.StartNew();
        try
        {
            var runtime = await GetAsync<VersionResponse>(
                "api/version",
                cancellationToken);
            if (!Version.TryParse(runtime.Version, out var version) ||
                version < _options.MinimumRuntimeVersion ||
                version >= _options.MaximumRuntimeVersionExclusive)
            {
                return Unhealthy(
                    "The Ollama runtime version is not compatible.",
                    startedAt,
                    stopwatch.Elapsed);
            }

            var tags = await GetAsync<TagsResponse>("api/tags", cancellationToken);
            var model = tags.Models?.SingleOrDefault(
                candidate => string.Equals(
                    candidate.Name,
                    _options.Model,
                    StringComparison.Ordinal));
            if (model is null)
            {
                return Unhealthy(
                    "The configured Ollama model is not installed.",
                    startedAt,
                    stopwatch.Elapsed);
            }

            var shown = await PostAsync<ShowRequest, ShowResponse>(
                "api/show",
                new ShowRequest(_options.Model),
                cancellationToken);
            if (shown.Capabilities is null ||
                !shown.Capabilities.Contains(
                    "completion",
                    StringComparer.OrdinalIgnoreCase))
            {
                return Unhealthy(
                    "The configured Ollama model does not support completion.",
                    startedAt,
                    stopwatch.Elapsed);
            }

            if (!string.Equals(
                    NormalizeDigest(model.Digest),
                    NormalizeDigest(_options.ExpectedDigest),
                    StringComparison.OrdinalIgnoreCase))
            {
                return Unhealthy(
                    "The installed Ollama model failed its integrity check.",
                    startedAt,
                    stopwatch.Elapsed);
            }

            return new ProviderHealthReport
            {
                ProviderName = Name,
                IsHealthy = true,
                StatusMessage = "The pinned local Ollama model is ready.",
                CheckedAtUtc = _timeProvider.GetUtcNow(),
                ResponseTime = stopwatch.Elapsed,
                Metadata = new ReadOnlyDictionary<string, string>(
                    new Dictionary<string, string>(StringComparer.Ordinal)
                    {
                        ["model"] = _options.Model,
                        ["model.digest"] = NormalizeDigest(model.Digest),
                        ["runtime.version"] = version.ToString()
                    })
            };
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception) when (
            exception is HttpRequestException or JsonException or
            InvalidDataException)
        {
            return Unhealthy(
                "The local Ollama runtime did not return a valid response.",
                startedAt,
                stopwatch.Elapsed);
        }
    }

    private async Task<T> GetAsync<T>(
        string path,
        CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(
            Endpoint(path),
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<T>(response, cancellationToken);
    }

    private async Task<TResponse> PostAsync<TRequest, TResponse>(
        string path,
        TRequest request,
        CancellationToken cancellationToken)
    {
        using var message = new HttpRequestMessage(HttpMethod.Post, Endpoint(path))
        {
            Content = JsonContent.Create(request)
        };
        using var response = await _httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        response.EnsureSuccessStatusCode();
        return await ReadJsonAsync<TResponse>(response, cancellationToken);
    }

    private async Task<T> ReadJsonAsync<T>(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        await using var source = await response.Content.ReadAsStreamAsync(
            cancellationToken);
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        while (true)
        {
            var read = await source.ReadAsync(chunk, cancellationToken);
            if (read == 0)
            {
                break;
            }

            if (buffer.Length + read > _options.MaximumResponseBytes)
            {
                throw new InvalidDataException(
                    "The response exceeded the configured limit.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        buffer.Position = 0;
        var value = await JsonSerializer.DeserializeAsync<T>(
            buffer,
            SerializerOptions,
            cancellationToken: cancellationToken);
        return value ?? throw new InvalidDataException("The response was empty.");
    }

    private Uri Endpoint(string path) => new(_options.Endpoint, path);

    private ProviderHealthReport Unhealthy(
        string message,
        DateTimeOffset startedAt,
        TimeSpan elapsed) =>
        new()
        {
            ProviderName = Name,
            IsHealthy = false,
            StatusMessage = message,
            CheckedAtUtc = _timeProvider.GetUtcNow(),
            ResponseTime = elapsed,
            Metadata = new Dictionary<string, string>()
        };

    private ReadOnlyDictionary<string, string> Metadata(
        TimeSpan elapsed,
        long? promptTokens,
        long? completionTokens)
    {
        var values = new SortedDictionary<string, string>(StringComparer.Ordinal)
        {
            ["duration.ms"] = elapsed.TotalMilliseconds.ToString(
                "F3",
                CultureInfo.InvariantCulture),
            ["execution.location"] = "local-process",
            ["model"] = _options.Model,
            ["provider.kind"] = "ollama"
        };
        if (promptTokens.HasValue)
        {
            values["tokens.prompt"] =
                promptTokens.Value.ToString(CultureInfo.InvariantCulture);
        }

        if (completionTokens.HasValue)
        {
            values["tokens.completion"] =
                completionTokens.Value.ToString(CultureInfo.InvariantCulture);
        }

        return new ReadOnlyDictionary<string, string>(values);
    }

    private void ValidateRequest(CapabilityExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.RequestId == Guid.Empty ||
            string.IsNullOrWhiteSpace(request.WorkflowId) ||
            string.IsNullOrWhiteSpace(request.TaskId) ||
            string.IsNullOrWhiteSpace(request.TaskName) ||
            string.IsNullOrWhiteSpace(request.Instruction) ||
            request.Instruction.Length > _options.MaximumInstructionCharacters ||
            request.Requirement is null ||
            !Enum.IsDefined(request.Requirement.Capability) ||
            request.Inputs is null ||
            request.Inputs.Count != 0)
        {
            throw new ArgumentException(
                "The capability request is invalid or exceeds configured limits.",
                nameof(request));
        }
    }

    private static void ValidateOptions(OllamaProviderOptions options)
    {
        var digest = NormalizeDigest(options.ExpectedDigest);
        var isLoopback = options.Endpoint is not null && IPAddress.TryParse(
                options.Endpoint.Host,
                out var address) &&
            IPAddress.IsLoopback(address);
        if (options.Endpoint is null ||
            !options.Endpoint.IsAbsoluteUri ||
            options.Endpoint.Scheme != Uri.UriSchemeHttp ||
            (!options.AllowRemoteEndpoint && !isLoopback) ||
            string.IsNullOrWhiteSpace(options.Model) ||
            digest.Length != 64 ||
            !digest.All(Uri.IsHexDigit) ||
            options.MinimumRuntimeVersion >=
                options.MaximumRuntimeVersionExclusive ||
            options.MaximumInstructionCharacters is < 1 or > 1_000_000 ||
            options.MaximumResponseBytes is < 1 or > 16_777_216)
        {
            throw new ArgumentException(
                "The Ollama provider options are invalid.",
                nameof(options));
        }
    }

    private static string NormalizeDigest(string? digest) =>
        digest?.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase) == true
            ? digest[7..]
            : digest ?? string.Empty;

    private sealed record VersionResponse(string Version);
    private sealed record TagsResponse(ModelTag[]? Models);
    private sealed record ModelTag(string Name, string? Digest);
    private sealed record ShowRequest(string Model);
    private sealed record ShowResponse(string[]? Capabilities);
    private sealed record ChatRequest(
        string Model,
        bool Stream,
        ChatMessage[] Messages);
    private sealed record ChatMessage(string Role, string Content);
    private sealed record ChatResponse(
        ChatMessage? Message,
        [property: JsonPropertyName("prompt_eval_count")]
        long? PromptEvalCount,
        [property: JsonPropertyName("eval_count")]
        long? EvalCount);
}
