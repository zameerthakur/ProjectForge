using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using ProjectForge.Abstractions.Capabilities;
using ProjectForge.Application.Workflows;

namespace ProjectForge.Infrastructure.Workflows;

/// <summary>
/// Persists workflow, approval, and audit state in a local SQLite database.
/// </summary>
public sealed class SqliteWorkflowStore : IWorkflowStore, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

    private readonly string _connectionString;
    private readonly SemaphoreSlim _initialization = new(1, 1);
    private bool _initialized;
    private bool _disposed;

    public SqliteWorkflowStore(string databasePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(databasePath);

        var fullPath = Path.GetFullPath(databasePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared,
            Pooling = false
        }.ToString();
    }

    public async Task CreateAsync(
        WorkflowSnapshot workflow,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workflow);
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);

        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO workflows (
                id, request_json, status, version, created_at_utc,
                updated_at_utc, failure_message, provider_selection_json,
                execution_json, artifacts_json, recovery_json)
            VALUES (
                $id, $request, $status, $version, $created, $updated, $failure,
                $selection, $execution, $artifacts, $recovery);
            """,
            cancellationToken,
            ("$id", workflow.Workflow.Id.ToString("D")),
            ("$request", JsonSerializer.Serialize(
                workflow.Workflow.Request,
                JsonOptions)),
            ("$status", (int)workflow.Workflow.Status),
            ("$version", workflow.Workflow.Version),
            ("$created", Format(workflow.Workflow.CreatedAtUtc)),
            ("$updated", Format(workflow.Workflow.UpdatedAtUtc)),
            ("$failure", workflow.Workflow.FailureMessage),
            ("$selection", Serialize(workflow.ProviderSelection)),
            ("$execution", Serialize(workflow.Execution)),
            ("$artifacts", Serialize(workflow.Artifacts)),
            ("$recovery", Serialize(workflow.Recovery)));

        if (workflow.Approval is not null)
        {
            await InsertApprovalAsync(
                connection,
                transaction,
                workflow.Approval,
                cancellationToken);
        }

        foreach (var auditEvent in workflow.AuditEvents)
        {
            await InsertAuditAsync(
                connection,
                transaction,
                auditEvent,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<WorkflowSnapshot?> GetAsync(
        Guid workflowId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        var workflow = await GetAsync(
            connection,
            transaction,
            workflowId,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return workflow;
    }

    public async Task<IReadOnlyCollection<WorkflowSnapshot>> ListAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            "SELECT id FROM workflows ORDER BY updated_at_utc DESC, id;";

        var ids = new List<Guid>();
        await using (var reader = await command.ExecuteReaderAsync(
                         cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                ids.Add(Guid.Parse(reader.GetString(0)));
            }
        }

        var workflows = new List<WorkflowSnapshot>(ids.Count);
        foreach (var id in ids)
        {
            workflows.Add(
                await GetAsync(
                    connection,
                    transaction,
                    id,
                    cancellationToken) ??
                throw new InvalidOperationException(
                    $"Workflow '{id}' disappeared while it was being listed."));
        }

        await transaction.CommitAsync(cancellationToken);
        return workflows;
    }

    public async Task<WorkflowMutationResult> TryRecordDecisionAsync(
        Guid workflowId,
        long expectedVersion,
        ApprovalDecision decision,
        string? decidedBy,
        DateTimeOffset decidedAtUtc,
        CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(decision))
        {
            throw new ArgumentOutOfRangeException(nameof(decision), decision, null);
        }

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);

        var nextStatus = decision == ApprovalDecision.Approved
            ? WorkflowStatus.Approved
            : WorkflowStatus.Rejected;
        var changed = await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE workflows
            SET status = $next_status,
                version = version + 1,
                updated_at_utc = $decided
            WHERE id = $id
              AND version = $version
              AND status = $pending;
            """,
            cancellationToken,
            ("$next_status", (int)nextStatus),
            ("$decided", Format(decidedAtUtc)),
            ("$id", workflowId.ToString("D")),
            ("$version", expectedVersion),
            ("$pending", (int)WorkflowStatus.PendingApproval));

        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotApplied(
                await GetAsync(
                    connection,
                    transaction: null,
                    workflowId,
                    cancellationToken));
        }

        var approvalChanged = await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE approvals
            SET decision = $decision,
                decided_by = $decided_by,
                decided_at_utc = $decided
            WHERE workflow_id = $id
              AND decision IS NULL;
            """,
            cancellationToken,
            ("$decision", (int)decision),
            ("$decided_by", decidedBy),
            ("$decided", Format(decidedAtUtc)),
            ("$id", workflowId.ToString("D")));

        if (approvalChanged != 1)
        {
            throw new InvalidOperationException(
                $"Workflow '{workflowId}' has no matching pending approval.");
        }

        await InsertAuditAsync(
            connection,
            transaction,
            new WorkflowAuditEvent
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                EventType = decision == ApprovalDecision.Approved
                    ? "workflow.approved"
                    : "workflow.rejected",
                Message = decision == ApprovalDecision.Approved
                    ? "Workflow execution approved."
                    : "Workflow execution rejected.",
                OccurredAtUtc = decidedAtUtc
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Applied(
            await GetAsync(
                connection,
                transaction: null,
                workflowId,
                cancellationToken));
    }

    public async Task<WorkflowMutationResult> TryTransitionAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowStatus expectedStatus,
        WorkflowStatus nextStatus,
        string auditEventType,
        string auditMessage,
        DateTimeOffset occurredAtUtc,
        string? failureMessage = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(auditEventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditMessage);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        var changed = await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE workflows
            SET status = $next_status,
                version = version + 1,
                updated_at_utc = $occurred,
                failure_message = $failure
            WHERE id = $id
              AND version = $version
              AND status = $expected_status;
            """,
            cancellationToken,
            ("$next_status", (int)nextStatus),
            ("$occurred", Format(occurredAtUtc)),
            ("$failure", failureMessage),
            ("$id", workflowId.ToString("D")),
            ("$version", expectedVersion),
            ("$expected_status", (int)expectedStatus));

        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotApplied(
                await GetAsync(
                    connection,
                    transaction: null,
                    workflowId,
                    cancellationToken));
        }

        await InsertAuditAsync(
            connection,
            transaction,
            new WorkflowAuditEvent
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                EventType = auditEventType,
                Message = auditMessage,
                OccurredAtUtc = occurredAtUtc
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Applied(
            await GetAsync(
                connection,
                transaction: null,
                workflowId,
                cancellationToken));
    }

    public async Task<WorkflowMutationResult> TryStartExecutionAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowProviderSelectionEvidence providerSelection,
        DateTimeOffset startedAtUtc,
        CancellationToken cancellationToken = default)
    {
        ValidateProviderSelection(providerSelection);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        var changed = await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE workflows
            SET status = $running,
                version = version + 1,
                updated_at_utc = $started,
                failure_message = NULL,
                provider_selection_json = $selection,
                execution_json = NULL,
                artifacts_json = NULL,
                recovery_json = NULL
            WHERE id = $id
              AND version = $version
              AND status = $queued;
            """,
            cancellationToken,
            ("$running", (int)WorkflowStatus.Running),
            ("$started", Format(startedAtUtc)),
            ("$selection", Serialize(providerSelection)),
            ("$id", workflowId.ToString("D")),
            ("$version", expectedVersion),
            ("$queued", (int)WorkflowStatus.Queued));

        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotApplied(
                await GetAsync(
                    connection,
                    transaction: null,
                    workflowId,
                    cancellationToken));
        }

        await InsertAuditAsync(
            connection,
            transaction,
            new WorkflowAuditEvent
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                EventType = "workflow.execution-started",
                Message =
                    $"Provider '{providerSelection.SelectedProviderName}' " +
                    "claimed the workflow for execution.",
                OccurredAtUtc = startedAtUtc
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Applied(
            await GetAsync(
                connection,
                transaction: null,
                workflowId,
                cancellationToken));
    }

    public async Task<WorkflowMutationResult> TryCompleteExecutionAsync(
        Guid workflowId,
        long expectedVersion,
        WorkflowStatus terminalStatus,
        WorkflowExecutionEvidence execution,
        WorkflowArtifactPaths? artifacts,
        string auditEventType,
        string auditMessage,
        DateTimeOffset occurredAtUtc,
        string? failureMessage = null,
        CancellationToken cancellationToken = default)
    {
        ValidateCompletion(
            terminalStatus,
            execution,
            artifacts,
            auditEventType,
            auditMessage);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        var changed = await ExecuteAsync(
            connection,
            transaction,
            """
            UPDATE workflows
            SET status = $terminal_status,
                version = version + 1,
                updated_at_utc = $occurred,
                failure_message = $failure,
                execution_json = $execution,
                artifacts_json = $artifacts
            WHERE id = $id
              AND version = $version
              AND status = $running;
            """,
            cancellationToken,
            ("$terminal_status", (int)terminalStatus),
            ("$occurred", Format(occurredAtUtc)),
            ("$failure", failureMessage),
            ("$execution", Serialize(execution)),
            ("$artifacts", Serialize(artifacts)),
            ("$id", workflowId.ToString("D")),
            ("$version", expectedVersion),
            ("$running", (int)WorkflowStatus.Running));

        if (changed == 0)
        {
            await transaction.RollbackAsync(cancellationToken);
            return NotApplied(
                await GetAsync(
                    connection,
                    transaction: null,
                    workflowId,
                    cancellationToken));
        }

        var current = await GetAsync(
            connection,
            transaction,
            workflowId,
            cancellationToken) ??
            throw new InvalidOperationException(
                $"Workflow '{workflowId}' disappeared during completion.");
        ValidateExecutionCorrelation(current, execution);

        await InsertAuditAsync(
            connection,
            transaction,
            new WorkflowAuditEvent
            {
                Id = Guid.NewGuid(),
                WorkflowId = workflowId,
                EventType = auditEventType,
                Message = auditMessage,
                OccurredAtUtc = occurredAtUtc
            },
            cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return Applied(
            await GetAsync(
                connection,
                transaction: null,
                workflowId,
                cancellationToken));
    }

    public async Task<int> ReconcileInterruptedExecutionsAsync(
        DateTimeOffset detectedAtUtc,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        await using var connection = await OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, version, updated_at_utc
            FROM workflows
            WHERE status = $running
            ORDER BY id;
            """;
        command.Parameters.AddWithValue(
            "$running",
            (int)WorkflowStatus.Running);

        var interrupted = new List<(Guid Id, long Version, DateTimeOffset At)>();
        await using (var reader = await command.ExecuteReaderAsync(
                         cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                interrupted.Add(
                    (
                        Guid.Parse(reader.GetString(0)),
                        reader.GetInt64(1),
                        Parse(reader.GetString(2))));
            }
        }

        var reconciled = 0;
        foreach (var item in interrupted)
        {
            var recovery = new WorkflowRecoveryMetadata
            {
                InterruptedStatus = WorkflowStatus.Running,
                InterruptedAtUtc = item.At,
                DetectedAtUtc = detectedAtUtc,
                Reason = reason
            };
            var changed = await ExecuteAsync(
                connection,
                transaction,
                """
                UPDATE workflows
                SET status = $reconciliation_required,
                    version = version + 1,
                    updated_at_utc = $detected,
                    failure_message = $reason,
                    recovery_json = $recovery
                WHERE id = $id
                  AND version = $version
                  AND status = $running;
                """,
                cancellationToken,
                (
                    "$reconciliation_required",
                    (int)WorkflowStatus.ReconciliationRequired),
                ("$detected", Format(detectedAtUtc)),
                ("$reason", reason),
                ("$recovery", Serialize(recovery)),
                ("$id", item.Id.ToString("D")),
                ("$version", item.Version),
                ("$running", (int)WorkflowStatus.Running));

            if (changed == 0)
            {
                continue;
            }

            await InsertAuditAsync(
                connection,
                transaction,
                new WorkflowAuditEvent
                {
                    Id = Guid.NewGuid(),
                    WorkflowId = item.Id,
                    EventType = "workflow.execution-interrupted",
                    Message = reason,
                    OccurredAtUtc = detectedAtUtc
                },
                cancellationToken);
            reconciled++;
        }

        await transaction.CommitAsync(cancellationToken);
        return reconciled;
    }

    private async Task<SqliteConnection> OpenAsync(
        CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await EnsureInitializedAsync(cancellationToken);
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await SetForeignKeysAsync(connection, cancellationToken);
        return connection;
    }

    private async Task EnsureInitializedAsync(
        CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await _initialization.WaitAsync(cancellationToken);
        try
        {
            if (_initialized)
            {
                return;
            }

            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(cancellationToken);
            await SetForeignKeysAsync(connection, cancellationToken);
            await using var transaction = (SqliteTransaction)
                await connection.BeginTransactionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText =
                """
                CREATE TABLE IF NOT EXISTS workflows (
                    id TEXT PRIMARY KEY,
                    request_json TEXT NOT NULL,
                    status INTEGER NOT NULL,
                    version INTEGER NOT NULL,
                    created_at_utc TEXT NOT NULL,
                    updated_at_utc TEXT NOT NULL,
                    failure_message TEXT NULL,
                    provider_selection_json TEXT NULL,
                    execution_json TEXT NULL,
                    artifacts_json TEXT NULL,
                    recovery_json TEXT NULL
                );

                CREATE TABLE IF NOT EXISTS approvals (
                    id TEXT PRIMARY KEY,
                    workflow_id TEXT NOT NULL UNIQUE,
                    prompt TEXT NOT NULL,
                    decision INTEGER NULL,
                    decided_by TEXT NULL,
                    requested_at_utc TEXT NOT NULL,
                    decided_at_utc TEXT NULL,
                    FOREIGN KEY (workflow_id) REFERENCES workflows(id)
                );

                CREATE TABLE IF NOT EXISTS audit_events (
                    id TEXT PRIMARY KEY,
                    workflow_id TEXT NOT NULL,
                    event_type TEXT NOT NULL,
                    message TEXT NOT NULL,
                    occurred_at_utc TEXT NOT NULL,
                    FOREIGN KEY (workflow_id) REFERENCES workflows(id)
                );

                CREATE INDEX IF NOT EXISTS ix_audit_events_workflow_time
                    ON audit_events(workflow_id, occurred_at_utc, id);
                """;
            await command.ExecuteNonQueryAsync(cancellationToken);

            await EnsureWorkflowColumnAsync(
                connection,
                transaction,
                "provider_selection_json",
                "TEXT NULL",
                cancellationToken);
            await EnsureWorkflowColumnAsync(
                connection,
                transaction,
                "execution_json",
                "TEXT NULL",
                cancellationToken);
            await EnsureWorkflowColumnAsync(
                connection,
                transaction,
                "artifacts_json",
                "TEXT NULL",
                cancellationToken);
            await EnsureWorkflowColumnAsync(
                connection,
                transaction,
                "recovery_json",
                "TEXT NULL",
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);
            _initialized = true;
        }
        finally
        {
            _initialization.Release();
        }
    }

    private static async Task SetForeignKeysAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<WorkflowSnapshot?> GetAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        var workflow = await ReadWorkflowAsync(
            connection,
            transaction,
            workflowId,
            cancellationToken);
        if (workflow is null)
        {
            return null;
        }

        return new WorkflowSnapshot
        {
            Workflow = workflow,
            Approval = await ReadApprovalAsync(
                connection,
                transaction,
                workflowId,
                cancellationToken),
            AuditEvents = await ReadAuditAsync(
                connection,
                transaction,
                workflowId,
                cancellationToken),
            ProviderSelection =
                await ReadJsonColumnAsync<WorkflowProviderSelectionEvidence>(
                    connection,
                    transaction,
                    workflowId,
                    "provider_selection_json",
                    cancellationToken),
            Execution = await ReadJsonColumnAsync<WorkflowExecutionEvidence>(
                connection,
                transaction,
                workflowId,
                "execution_json",
                cancellationToken),
            Artifacts = await ReadJsonColumnAsync<WorkflowArtifactPaths>(
                connection,
                transaction,
                workflowId,
                "artifacts_json",
                cancellationToken),
            Recovery = await ReadJsonColumnAsync<WorkflowRecoveryMetadata>(
                connection,
                transaction,
                workflowId,
                "recovery_json",
                cancellationToken)
        };
    }

    private static async Task<WorkflowRecord?> ReadWorkflowAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT request_json, status, version, created_at_utc,
                   updated_at_utc, failure_message
            FROM workflows
            WHERE id = $id;
            """;
        command.Parameters.AddWithValue("$id", workflowId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new WorkflowRecord
        {
            Id = workflowId,
            Request = JsonSerializer.Deserialize<CapabilityExecutionRequest>(
                reader.GetString(0),
                JsonOptions) ??
                throw new InvalidOperationException(
                    $"Workflow '{workflowId}' has an invalid request payload."),
            Status = (WorkflowStatus)reader.GetInt32(1),
            Version = reader.GetInt64(2),
            CreatedAtUtc = Parse(reader.GetString(3)),
            UpdatedAtUtc = Parse(reader.GetString(4)),
            FailureMessage = reader.IsDBNull(5) ? null : reader.GetString(5)
        };
    }

    private static async Task<ApprovalRecord?> ReadApprovalAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid workflowId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, prompt, decision, decided_by, requested_at_utc,
                   decided_at_utc
            FROM approvals
            WHERE workflow_id = $id;
            """;
        command.Parameters.AddWithValue("$id", workflowId.ToString("D"));
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ApprovalRecord
        {
            Id = Guid.Parse(reader.GetString(0)),
            WorkflowId = workflowId,
            Prompt = reader.GetString(1),
            Decision = reader.IsDBNull(2)
                ? null
                : (ApprovalDecision)reader.GetInt32(2),
            DecidedBy = reader.IsDBNull(3) ? null : reader.GetString(3),
            RequestedAtUtc = Parse(reader.GetString(4)),
            DecidedAtUtc = reader.IsDBNull(5)
                ? null
                : Parse(reader.GetString(5))
        };
    }

    private static async Task<IReadOnlyCollection<WorkflowAuditEvent>>
        ReadAuditAsync(
            SqliteConnection connection,
            SqliteTransaction? transaction,
            Guid workflowId,
            CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            """
            SELECT id, event_type, message, occurred_at_utc
            FROM audit_events
            WHERE workflow_id = $id
            ORDER BY occurred_at_utc, id;
            """;
        command.Parameters.AddWithValue("$id", workflowId.ToString("D"));

        var events = new List<WorkflowAuditEvent>();
        await using var reader = await command.ExecuteReaderAsync(
            cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            events.Add(
                new WorkflowAuditEvent
                {
                    Id = Guid.Parse(reader.GetString(0)),
                    WorkflowId = workflowId,
                    EventType = reader.GetString(1),
                    Message = reader.GetString(2),
                    OccurredAtUtc = Parse(reader.GetString(3))
                });
        }

        return events;
    }

    private static async Task<T?> ReadJsonColumnAsync<T>(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        Guid workflowId,
        string columnName,
        CancellationToken cancellationToken)
        where T : class
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText =
            $"SELECT {columnName} FROM workflows WHERE id = $id;";
        command.Parameters.AddWithValue("$id", workflowId.ToString("D"));

        var serialized = await command.ExecuteScalarAsync(cancellationToken);
        if (serialized is null or DBNull)
        {
            return null;
        }

        return JsonSerializer.Deserialize<T>((string)serialized, JsonOptions) ??
            throw new InvalidOperationException(
                $"Workflow '{workflowId}' has invalid {columnName} evidence.");
    }

    private static async Task EnsureWorkflowColumnAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string columnName,
        string definition,
        CancellationToken cancellationToken)
    {
        await using var inspect = connection.CreateCommand();
        inspect.Transaction = transaction;
        inspect.CommandText = "PRAGMA table_info(workflows);";

        var exists = false;
        await using (var reader = await inspect.ExecuteReaderAsync(
                         cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (string.Equals(
                        reader.GetString(1),
                        columnName,
                        StringComparison.Ordinal))
                {
                    exists = true;
                    break;
                }
            }
        }

        if (exists)
        {
            return;
        }

        await using var migrate = connection.CreateCommand();
        migrate.Transaction = transaction;
        migrate.CommandText =
            $"ALTER TABLE workflows ADD COLUMN {columnName} {definition};";
        await migrate.ExecuteNonQueryAsync(cancellationToken);
    }

    private static Task<int> InsertApprovalAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        ApprovalRecord approval,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO approvals (
                id, workflow_id, prompt, decision, decided_by,
                requested_at_utc, decided_at_utc)
            VALUES (
                $id, $workflow_id, $prompt, $decision, $decided_by,
                $requested, $decided);
            """,
            cancellationToken,
            ("$id", approval.Id.ToString("D")),
            ("$workflow_id", approval.WorkflowId.ToString("D")),
            ("$prompt", approval.Prompt),
            ("$decision", approval.Decision is null
                ? null
                : (int)approval.Decision.Value),
            ("$decided_by", approval.DecidedBy),
            ("$requested", Format(approval.RequestedAtUtc)),
            ("$decided", approval.DecidedAtUtc is null
                ? null
                : Format(approval.DecidedAtUtc.Value)));

    private static Task<int> InsertAuditAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        WorkflowAuditEvent auditEvent,
        CancellationToken cancellationToken) =>
        ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO audit_events (
                id, workflow_id, event_type, message, occurred_at_utc)
            VALUES ($id, $workflow_id, $type, $message, $occurred);
            """,
            cancellationToken,
            ("$id", auditEvent.Id.ToString("D")),
            ("$workflow_id", auditEvent.WorkflowId.ToString("D")),
            ("$type", auditEvent.EventType),
            ("$message", auditEvent.Message),
            ("$occurred", Format(auditEvent.OccurredAtUtc)));

    private static async Task<int> ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        CancellationToken cancellationToken,
        params (string Name, object? Value)[] parameters)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.AddWithValue(
                parameter.Name,
                parameter.Value ?? DBNull.Value);
        }

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static WorkflowMutationResult Applied(WorkflowSnapshot? current) =>
        new()
        {
            WasApplied = true,
            Current = current ??
                throw new InvalidOperationException(
                    "The updated workflow could not be loaded.")
        };

    private static WorkflowMutationResult NotApplied(
        WorkflowSnapshot? current) =>
        new()
        {
            WasApplied = false,
            Current = current ??
                throw new KeyNotFoundException("The workflow does not exist.")
        };

    private static string? Serialize<T>(T? value)
        where T : class =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static void ValidateProviderSelection(
        WorkflowProviderSelectionEvidence providerSelection)
    {
        ArgumentNullException.ThrowIfNull(providerSelection);
        ArgumentException.ThrowIfNullOrWhiteSpace(
            providerSelection.SelectedProviderName);
        if (providerSelection.EstimatedCost < decimal.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(providerSelection),
                providerSelection.EstimatedCost,
                "The selected provider cost cannot be negative.");
        }

        foreach (var candidate in providerSelection.Candidates)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(candidate.ProviderName);
            if (candidate.EstimatedCost < decimal.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(providerSelection),
                    candidate.EstimatedCost,
                    "A candidate provider cost cannot be negative.");
            }

            foreach (var rejection in candidate.Rejections)
            {
                if (!Enum.IsDefined(rejection.Code))
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(providerSelection),
                        rejection.Code,
                        "A provider rejection code is invalid.");
                }

                ArgumentException.ThrowIfNullOrWhiteSpace(rejection.Message);
            }
        }
    }

    private static void ValidateCompletion(
        WorkflowStatus terminalStatus,
        WorkflowExecutionEvidence execution,
        WorkflowArtifactPaths? artifacts,
        string auditEventType,
        string auditMessage)
    {
        ArgumentNullException.ThrowIfNull(execution);
        ArgumentException.ThrowIfNullOrWhiteSpace(execution.ProviderName);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditEventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(auditMessage);

        if (execution.RequestId == Guid.Empty)
        {
            throw new ArgumentException(
                "Execution evidence must identify its request.",
                nameof(execution));
        }

        if (!Enum.IsDefined(execution.Outcome))
        {
            throw new ArgumentOutOfRangeException(
                nameof(execution),
                execution.Outcome,
                "The execution outcome is invalid.");
        }

        if (execution.CompletedAtUtc < execution.StartedAtUtc)
        {
            throw new ArgumentException(
                "Execution cannot complete before it starts.",
                nameof(execution));
        }

        if (execution.EstimatedCost < decimal.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(execution),
                execution.EstimatedCost,
                "The execution cost cannot be negative.");
        }

        if (terminalStatus == WorkflowStatus.Succeeded)
        {
            if (execution.Outcome != WorkflowExecutionOutcome.Succeeded)
            {
                throw new ArgumentException(
                    "A successful workflow requires successful execution " +
                    "evidence.",
                    nameof(execution));
            }

            ArgumentNullException.ThrowIfNull(artifacts);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifacts.MarkdownPath);
            ArgumentException.ThrowIfNullOrWhiteSpace(artifacts.JsonPath);
            return;
        }

        if (terminalStatus != WorkflowStatus.Failed)
        {
            throw new ArgumentOutOfRangeException(
                nameof(terminalStatus),
                terminalStatus,
                "Execution can only complete in a succeeded or failed state.");
        }

        if (execution.Outcome == WorkflowExecutionOutcome.Succeeded)
        {
            throw new ArgumentException(
                "A failed workflow cannot contain successful execution " +
                "evidence.",
                nameof(execution));
        }

        if (artifacts is not null)
        {
            throw new ArgumentException(
                "Artifacts are only supported for successful execution.",
                nameof(artifacts));
        }
    }

    private static void ValidateExecutionCorrelation(
        WorkflowSnapshot current,
        WorkflowExecutionEvidence execution)
    {
        if (current.Workflow.Request.RequestId != execution.RequestId)
        {
            throw new ArgumentException(
                "Execution evidence does not match the workflow request.",
                nameof(execution));
        }

        if (current.ProviderSelection is null ||
            !string.Equals(
                current.ProviderSelection.SelectedProviderName,
                execution.ProviderName,
                StringComparison.Ordinal))
        {
            throw new ArgumentException(
                "Execution evidence does not match the selected provider.",
                nameof(execution));
        }
    }

    private static string Format(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind);

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _initialization.Dispose();
        _disposed = true;
    }
}
