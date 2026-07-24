using System.Net;
using Microsoft.Extensions.Configuration;
using ProjectForge.Infrastructure.Providers;

namespace ProjectForge.Host.Configuration;

/// <summary>
/// Selects and configures the capability provider exposed by the host.
/// </summary>
public sealed class ProjectForgeProviderConfiguration
{
    /// <summary>
    /// Gets or sets the provider mode. The deterministic local mock remains the
    /// default so development and demonstration environments are reproducible.
    /// </summary>
    public ProjectForgeProviderMode Mode { get; set; } =
        ProjectForgeProviderMode.LocalMock;

    /// <summary>
    /// Gets or sets the Ollama configuration used when <see cref="Mode"/> is
    /// <see cref="ProjectForgeProviderMode.Ollama"/>.
    /// </summary>
    public OllamaHostConfiguration Ollama { get; set; } = new();

    /// <summary>
    /// Reads the provider configuration from the ProjectForge provider section.
    /// </summary>
    public static ProjectForgeProviderConfiguration From(
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var result = configuration
            .GetSection("ProjectForge:Providers")
            .Get<ProjectForgeProviderConfiguration>() ??
            new ProjectForgeProviderConfiguration();

        result.Validate();
        return result;
    }

    /// <summary>
    /// Validates the selected provider and converts its host settings to the
    /// provider-owned options contract.
    /// </summary>
    public OllamaProviderOptions CreateOllamaOptions()
    {
        Validate();

        return new OllamaProviderOptions
        {
            Endpoint = Ollama.Endpoint,
            Model = Ollama.Model,
            ExpectedDigest = Ollama.ExpectedDigest,
            MinimumRuntimeVersion = Ollama.MinimumRuntimeVersion,
            MaximumRuntimeVersionExclusive =
                Ollama.MaximumRuntimeVersionExclusive
        };
    }

    /// <summary>
    /// Ensures the selected provider is safe and fully configured.
    /// </summary>
    public void Validate()
    {
        if (!Enum.IsDefined(Mode))
        {
            throw new InvalidOperationException(
                $"The provider mode '{Mode}' is not supported.");
        }

        if (Mode == ProjectForgeProviderMode.Ollama)
        {
            Ollama.Validate();
        }
    }
}

/// <summary>
/// Identifies the capability provider selected for this host instance.
/// </summary>
public enum ProjectForgeProviderMode
{
    /// <summary>Uses the deterministic provider intended for demos and tests.</summary>
    LocalMock = 0,

    /// <summary>Uses an already running, verified local Ollama runtime.</summary>
    Ollama = 1
}

/// <summary>
/// Contains host-level configuration for the direct Ollama provider.
/// </summary>
public sealed class OllamaHostConfiguration
{
    private static readonly TimeSpan MaximumRequestTimeout =
        TimeSpan.FromMinutes(30);

    /// <summary>Gets or sets the loopback Ollama API endpoint.</summary>
    public Uri Endpoint { get; set; } = new("http://127.0.0.1:11434");

    /// <summary>Gets or sets the exact installed model name.</summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>Gets or sets the exact expected model digest.</summary>
    public string ExpectedDigest { get; set; } = string.Empty;

    /// <summary>Gets or sets the oldest accepted Ollama runtime version.</summary>
    public Version MinimumRuntimeVersion { get; set; } = new(0, 0);

    /// <summary>
    /// Gets or sets the exclusive upper bound for the Ollama runtime version.
    /// </summary>
    public Version MaximumRuntimeVersionExclusive { get; set; } = new(0, 0);

    /// <summary>Gets or sets the bounded HTTP request timeout in seconds.</summary>
    public int RequestTimeoutSeconds { get; set; } = 300;

    internal TimeSpan RequestTimeout =>
        TimeSpan.FromSeconds(RequestTimeoutSeconds);

    internal void Validate()
    {
        if (!Endpoint.IsAbsoluteUri ||
            Endpoint.Scheme != Uri.UriSchemeHttp ||
            !IPAddress.TryParse(Endpoint.Host, out var address) ||
            !IPAddress.IsLoopback(address) ||
            !string.IsNullOrEmpty(Endpoint.UserInfo) ||
            !string.IsNullOrEmpty(Endpoint.Query) ||
            !string.IsNullOrEmpty(Endpoint.Fragment))
        {
            throw new InvalidOperationException(
                "The Ollama endpoint must be an absolute HTTP loopback URI " +
                "without credentials, query parameters, or a fragment.");
        }

        if (string.IsNullOrWhiteSpace(Model))
        {
            throw new InvalidOperationException(
                "An exact Ollama model name is required.");
        }

        var digest = ExpectedDigest.StartsWith(
            "sha256:",
            StringComparison.OrdinalIgnoreCase)
            ? ExpectedDigest[7..]
            : ExpectedDigest;
        if (digest.Length != 64 || !digest.All(Uri.IsHexDigit))
        {
            throw new InvalidOperationException(
                "The expected Ollama model digest must contain exactly " +
                "64 hexadecimal characters.");
        }

        if (MinimumRuntimeVersion >= MaximumRuntimeVersionExclusive)
        {
            throw new InvalidOperationException(
                "The Ollama runtime version range must have an exclusive " +
                "upper bound greater than its minimum.");
        }

        var timeout = RequestTimeout;
        if (timeout <= TimeSpan.Zero || timeout > MaximumRequestTimeout)
        {
            throw new InvalidOperationException(
                "The Ollama request timeout must be between zero and " +
                $"{MaximumRequestTimeout.TotalMinutes:g} minutes.");
        }
    }
}
