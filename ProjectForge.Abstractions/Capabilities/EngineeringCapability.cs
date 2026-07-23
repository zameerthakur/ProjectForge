namespace ProjectForge.Abstractions.Capabilities;

/// <summary>
/// Identifies a provider-independent engineering capability that may be
/// requested by a ProjectForge workflow task.
/// </summary>
/// <remarks>
/// ProjectForge workflows request capabilities rather than specific tools
/// or providers. The resource scheduler and plugin system are responsible
/// for selecting an appropriate implementation at runtime.
/// </remarks>
public enum EngineeringCapability
{
    /// <summary>
    /// Performs project idea discovery and initial requirement exploration.
    /// </summary>
    Discovery = 1,

    /// <summary>
    /// Evaluates whether a project supports the user's career and portfolio strategy.
    /// </summary>
    CareerReview = 2,

    /// <summary>
    /// Reviews architecture decisions, constraints, risks, and trade-offs.
    /// </summary>
    ArchitectureReview = 3,

    /// <summary>
    /// Produces system, component, interface, or user-experience designs.
    /// </summary>
    Design = 4,

    /// <summary>
    /// Converts approved requirements and designs into an implementation plan.
    /// </summary>
    Planning = 5,

    /// <summary>
    /// Creates or modifies application source code.
    /// </summary>
    Coding = 6,

    /// <summary>
    /// Builds, executes, and evaluates automated or manual tests.
    /// </summary>
    Testing = 7,

    /// <summary>
    /// Reviews source code, dependencies, configuration, and architecture for security risks.
    /// </summary>
    SecurityReview = 8,

    /// <summary>
    /// Produces or updates technical and project documentation.
    /// </summary>
    Documentation = 9,

    /// <summary>
    /// Performs source-control operations through a configured Git provider.
    /// </summary>
    SourceControl = 10,

    /// <summary>
    /// Deploys an application or supporting infrastructure.
    /// </summary>
    Deployment = 11,

    /// <summary>
    /// Packages and publishes an approved software release.
    /// </summary>
    Release = 12,

    /// <summary>
    /// Performs maintenance, diagnostics, upgrades, or operational support.
    /// </summary>
    Maintenance = 13
}
