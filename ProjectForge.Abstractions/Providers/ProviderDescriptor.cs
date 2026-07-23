namespace ProjectForge.Abstractions.Providers;

/// <summary>
/// Describes provider characteristics used for eligibility and ranking.
/// </summary>
/// <remarks>
/// The descriptor contains stable operational characteristics. Request-specific
/// availability and cost are evaluated through the provider's asynchronous
/// methods.
/// </remarks>
public sealed class ProviderDescriptor
{
    /// <summary>
    /// Gets the unique human-readable provider name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the location where the provider performs its work.
    /// </summary>
    public required ProviderExecutionLocation ExecutionLocation { get; init; }

    /// <summary>
    /// Gets whether the provider can access a source-code repository.
    /// </summary>
    public bool SupportsRepositoryAccess { get; init; }

    /// <summary>
    /// Gets whether the provider can modify files.
    /// </summary>
    public bool SupportsFileWriteAccess { get; init; }

    /// <summary>
    /// Gets whether the provider can execute tools or operating-system commands.
    /// </summary>
    public bool SupportsToolExecution { get; init; }
}
