namespace ProjectForge.Infrastructure.Providers;

/// <summary>
/// Defines the pinned local Ollama runtime and model accepted by the provider.
/// </summary>
public sealed record OllamaProviderOptions
{
    /// <summary>Gets the loopback Ollama endpoint.</summary>
    public required Uri Endpoint { get; init; }

    /// <summary>Gets the exact model name sent to Ollama.</summary>
    public required string Model { get; init; }

    /// <summary>Gets the expected model SHA-256 digest.</summary>
    public required string ExpectedDigest { get; init; }

    /// <summary>Gets the oldest compatible Ollama version.</summary>
    public required Version MinimumRuntimeVersion { get; init; }

    /// <summary>Gets the exclusive upper bound for compatible Ollama versions.</summary>
    public required Version MaximumRuntimeVersionExclusive { get; init; }

    /// <summary>Gets whether a non-loopback endpoint is explicitly allowed.</summary>
    public bool AllowRemoteEndpoint { get; init; }

    /// <summary>Gets the maximum instruction length.</summary>
    public int MaximumInstructionCharacters { get; init; } = 16_384;

    /// <summary>Gets the maximum response size.</summary>
    public int MaximumResponseBytes { get; init; } = 1_048_576;
}
