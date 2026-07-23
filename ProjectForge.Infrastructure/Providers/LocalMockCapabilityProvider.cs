using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Abstractions.Health;
using ProjectForge.Abstractions.Providers;

namespace ProjectForge.Infrastructure.Providers;

/// <summary>
/// Provides deterministic, in-process capability execution for demonstrations
/// and integration tests that must not depend on an external service.
/// </summary>
public sealed class LocalMockCapabilityProvider : ISchedulableCapabilityProvider
{
    private const int MaximumInstructionLength = 16_384;
    private const int MaximumInputCount = 100;
    private const int MaximumInputKeyLength = 256;
    private const int MaximumInputValueLength = 4_096;
    private static readonly TimeSpan MaximumExecutionDelay =
        TimeSpan.FromSeconds(30);

    private static readonly IReadOnlyCollection<EngineeringCapability>
        Capabilities = Array.AsReadOnly(
            Enum.GetValues<EngineeringCapability>());

    private static readonly IReadOnlyDictionary<string, string> HealthMetadata =
        new ReadOnlyDictionary<string, string>(
            new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["execution.location"] = "local-process",
                ["provider.kind"] = "deterministic-mock"
            });

    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _executionDelay;

    /// <summary>
    /// Initializes a deterministic local mock provider.
    /// </summary>
    /// <param name="timeProvider">
    /// Supplies timestamps for health and execution results.
    /// </param>
    /// <param name="executionDelay">
    /// Adds an optional bounded delay for exercising cancellation and timeout
    /// behavior without contacting an external service.
    /// </param>
    public LocalMockCapabilityProvider(
        TimeProvider? timeProvider = null,
        TimeSpan? executionDelay = null)
    {
        _timeProvider = timeProvider ?? TimeProvider.System;
        _executionDelay = executionDelay ?? TimeSpan.Zero;

        if (_executionDelay < TimeSpan.Zero ||
            _executionDelay > MaximumExecutionDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(executionDelay),
                _executionDelay,
                $"The execution delay must be between zero and " +
                $"{MaximumExecutionDelay.TotalSeconds:g} seconds.");
        }
    }

    /// <inheritdoc />
    public string Name => "local-mock";

    /// <inheritdoc />
    public IReadOnlyCollection<EngineeringCapability> SupportedCapabilities =>
        Capabilities;

    /// <inheritdoc />
    public ProviderDescriptor Descriptor { get; } = new()
    {
        Name = "local-mock",
        ExecutionLocation = ProviderExecutionLocation.LocalProcess,
        SupportsRepositoryAccess = false,
        SupportsFileWriteAccess = false,
        SupportsToolExecution = false
    };

    /// <inheritdoc />
    public Task<bool> CanExecuteAsync(
        CapabilityExecutionRequest request,
        CancellationToken cancellationToken = default)
    {
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            SupportedCapabilities.Contains(request.Requirement.Capability));
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

        var startedAtUtc = _timeProvider.GetUtcNow();
        if (_executionDelay > TimeSpan.Zero)
        {
            await Task.Delay(
                _executionDelay,
                _timeProvider,
                cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var completedAtUtc = _timeProvider.GetUtcNow();

        return new CapabilityExecutionResult
        {
            RequestId = request.RequestId,
            ProviderName = Name,
            IsSuccessful = true,
            Summary = $"Completed deterministic local mock execution for " +
                $"task '{request.TaskName}'.",
            Output = RenderOutput(request),
            StartedAtUtc = startedAtUtc,
            CompletedAtUtc = completedAtUtc,
            EstimatedCost = 0m,
            Metadata = CreateExecutionMetadata(request)
        };
    }

    /// <inheritdoc />
    public Task<ProviderHealthReport> CheckHealthAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        return Task.FromResult(
            new ProviderHealthReport
            {
                ProviderName = Name,
                IsHealthy = true,
                StatusMessage =
                    "The deterministic local mock provider is available.",
                CheckedAtUtc = _timeProvider.GetUtcNow(),
                ResponseTime = TimeSpan.Zero,
                Metadata = HealthMetadata
            });
    }

    private static void ValidateRequest(CapabilityExecutionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "The capability request must have an identifier.",
                nameof(request));
        }

        ValidateRequiredText(
            request.WorkflowId,
            "The capability request must identify its workflow.",
            nameof(request));
        ValidateRequiredText(
            request.TaskId,
            "The capability request must identify its task.",
            nameof(request));
        ValidateRequiredText(
            request.TaskName,
            "The capability request must provide a task name.",
            nameof(request));
        ValidateRequiredText(
            request.Instruction,
            "The capability request must provide an instruction.",
            nameof(request));

        if (request.Instruction.Length > MaximumInstructionLength)
        {
            throw new ArgumentException(
                $"The capability instruction cannot exceed " +
                $"{MaximumInstructionLength.ToString(CultureInfo.InvariantCulture)} " +
                "characters.",
                nameof(request));
        }

        if (request.Requirement is null)
        {
            throw new ArgumentException(
                "The capability request must provide a requirement.",
                nameof(request));
        }

        if (!Enum.IsDefined(request.Requirement.Capability))
        {
            throw new ArgumentException(
                "The capability request contains an unknown capability.",
                nameof(request));
        }

        if (request.Inputs is null)
        {
            throw new ArgumentException(
                "The capability request inputs cannot be null.",
                nameof(request));
        }

        if (request.Inputs.Count > MaximumInputCount)
        {
            throw new ArgumentException(
                $"The capability request cannot contain more than " +
                $"{MaximumInputCount.ToString(CultureInfo.InvariantCulture)} inputs.",
                nameof(request));
        }

        foreach (var input in request.Inputs)
        {
            ValidateInput(input, nameof(request));
        }
    }

    private static void ValidateRequiredText(
        string? value,
        string message,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException(message, parameterName);
        }
    }

    private static void ValidateInput(
        KeyValuePair<string, string> input,
        string parameterName)
    {
        if (string.IsNullOrWhiteSpace(input.Key) ||
            input.Key.Length > MaximumInputKeyLength)
        {
            throw new ArgumentException(
                $"Input keys must contain between 1 and " +
                $"{MaximumInputKeyLength.ToString(CultureInfo.InvariantCulture)} " +
                "characters.",
                parameterName);
        }

        if (input.Value is null ||
            input.Value.Length > MaximumInputValueLength)
        {
            throw new ArgumentException(
                $"Input values cannot be null or exceed " +
                $"{MaximumInputValueLength.ToString(CultureInfo.InvariantCulture)} " +
                "characters.",
                parameterName);
        }
    }

    private static string RenderOutput(CapabilityExecutionRequest request)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Local mock execution completed.");
        AppendField(builder, "Workflow", request.WorkflowId);
        AppendField(builder, "Task", request.TaskId);
        AppendField(builder, "Capability", request.Requirement.Capability);
        AppendField(builder, "Instruction", request.Instruction);
        builder.Append("Inputs: ");
        builder.AppendLine(
            request.Inputs.Count.ToString(CultureInfo.InvariantCulture));

        foreach (var input in request.Inputs.OrderBy(
                     pair => pair.Key,
                     StringComparer.Ordinal))
        {
            builder.Append("- ");
            builder.Append(input.Key);
            builder.Append(": ");
            builder.AppendLine(input.Value);
        }

        return builder.ToString();
    }

    private static void AppendField(
        StringBuilder builder,
        string name,
        object value)
    {
        builder.Append(name);
        builder.Append(": ");
        builder.AppendLine(
            Convert.ToString(value, CultureInfo.InvariantCulture));
    }

    private static ReadOnlyDictionary<string, string>
        CreateExecutionMetadata(CapabilityExecutionRequest request) =>
        new ReadOnlyDictionary<string, string>(
            new SortedDictionary<string, string>(StringComparer.Ordinal)
            {
                ["capability"] = request.Requirement.Capability.ToString(),
                ["execution.location"] = "local-process",
                ["input.count"] =
                    request.Inputs.Count.ToString(CultureInfo.InvariantCulture),
                ["provider.kind"] = "deterministic-mock",
                ["task.id"] = request.TaskId,
                ["workflow.id"] = request.WorkflowId
            });
}
