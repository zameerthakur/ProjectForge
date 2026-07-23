namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Identifies where a capability provider performs its work.
/// </summary>
public enum ProviderExecutionLocation
{
    /// <summary>
    /// Executes entirely on the ProjectForge host without an AI model.
    /// </summary>
    LocalProcess = 1,

    /// <summary>
    /// Executes through a model runtime hosted in the local environment.
    /// </summary>
    LocalModel = 2,

    /// <summary>
    /// Executes through a remote service outside the local environment.
    /// </summary>
    Cloud = 3
}
