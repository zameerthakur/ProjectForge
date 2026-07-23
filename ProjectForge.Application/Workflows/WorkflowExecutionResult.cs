using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Application.Workflows;

/// <summary>
/// Represents the result of an attempt to resume workflow execution.
/// </summary>
public sealed class WorkflowExecutionResult
{
    /// <summary>
    /// Gets whether this call acquired the execution claim and invoked a
    /// provider.
    /// </summary>
    public required bool WasExecutionStarted { get; init; }

    /// <summary>
    /// Gets the current durable workflow state.
    /// </summary>
    public required WorkflowSnapshot Current { get; init; }

    /// <summary>
    /// Gets the provider-selection evidence when selection occurred.
    /// </summary>
    public ProviderSelectionResult? Selection { get; init; }

    /// <summary>
    /// Gets the provider result when execution returned normally.
    /// </summary>
    public CapabilityExecutionResult? Execution { get; init; }
}
