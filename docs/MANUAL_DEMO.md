# Technical spike manual demonstration

This demonstration proves the ProjectForge technical-spike lifecycle with the
deterministic local provider. It requires only the pinned .NET SDK and does not
install a model, executable, or external provider.

## 1. Build and test

From the repository root:

```powershell
dotnet restore ProjectForge.sln
dotnet build ProjectForge.sln -c Release --no-restore
dotnet test ProjectForge.sln -c Release --no-build
dotnet test ProjectForge.AgentFrameworkSpike.Tests `
  -c Release --no-restore --disable-build-servers -m:1
```

The solution tests must pass without warnings. The isolated Agent Framework
tests must also pass.

## 2. Start the host

In terminal A:

```powershell
$demoRoot = Join-Path $env:TEMP "ProjectForge-Technical-Spike"
$env:ProjectForge__DatabasePath = Join-Path $demoRoot "projectforge.db"
$env:ProjectForge__ArtifactRoot = Join-Path $demoRoot "artifacts"
$env:ProjectForge__ExecutionTimeoutSeconds = "30"

dotnet run --project ProjectForge.Host `
  -c Release --no-build --urls http://127.0.0.1:5187
```

Keep this terminal open.

## 3. Create a paused workflow

In terminal B:

```powershell
$baseUrl = "http://127.0.0.1:5187"
$request = @{
  taskId = "manual-demo"
  taskName = "Technical spike demonstration"
  instruction = "Produce deterministic execution evidence."
  approvalPrompt = "Approve the technical spike execution?"
  capability = 7
  allowCloudExecution = $false
  preferLocalExecution = $true
} | ConvertTo-Json

$created = Invoke-RestMethod `
  -Method Post `
  -Uri "$baseUrl/workflows" `
  -ContentType "application/json" `
  -Body $request

$workflowId = $created.workflow.id
$pendingVersion = $created.workflow.version
$created.workflow | Format-List id, status, version
```

Expected state:

```text
status  = 1  # PendingApproval
version = 1
```

No provider has executed at this point.

## 4. Stop and restart

Stop terminal A with `Ctrl+C`. Start the host again in terminal A with the same
environment variables and `dotnet run` command from step 2.

In terminal B:

```powershell
$restored = Invoke-RestMethod `
  -Uri "$baseUrl/workflows/$workflowId"

$restored.workflow | Format-List id, status, version
$restored.approval | Format-List id, prompt, decision
```

The workflow and pending approval identifiers must match the values created
before restart. The workflow must still be `PendingApproval`.

## 5. Approve and execute

```powershell
$decision = @{
  expectedVersion = $pendingVersion
  decision = 1
  decidedBy = "manual-demo"
} | ConvertTo-Json

$completed = Invoke-RestMethod `
  -Method Post `
  -Uri "$baseUrl/workflows/$workflowId/decisions" `
  -ContentType "application/json" `
  -Body $decision

$completed.workflow | Format-List status, version, failureMessage
$completed.providerSelection | Format-List selectedProviderName, estimatedCost
$completed.execution | Format-List providerName, outcome, summary
$completed.artifacts | Format-List markdownPath, jsonPath
```

Expected state:

```text
status = 5  # Succeeded
```

The selected provider must be `local-mock`. Both artifact paths must exist:

```powershell
Test-Path $completed.artifacts.markdownPath
Test-Path $completed.artifacts.jsonPath
Get-Content $completed.artifacts.markdownPath
Get-Content $completed.artifacts.jsonPath
```

The workflow GET response now exposes approval, selection evidence, execution
outcome, artifact paths, and ordered audit history.

## 6. Prove repeated approval is idempotent

Run the same approval request again:

```powershell
try {
  Invoke-RestMethod `
    -Method Post `
    -Uri "$baseUrl/workflows/$workflowId/decisions" `
    -ContentType "application/json" `
    -Body $decision
} catch {
  $_.Exception.Response.StatusCode.value__
}
```

Expected HTTP status: `409 Conflict`. Fetch the workflow again and confirm its
version, execution evidence, and artifacts have not changed.

## 7. Prove rejection does not execute

```powershell
$rejectedRequest = $request
$rejected = Invoke-RestMethod `
  -Method Post `
  -Uri "$baseUrl/workflows" `
  -ContentType "application/json" `
  -Body $rejectedRequest

$rejection = @{
  expectedVersion = $rejected.workflow.version
  decision = 2
  decidedBy = "manual-demo"
} | ConvertTo-Json

$rejectedResult = Invoke-RestMethod `
  -Method Post `
  -Uri "$baseUrl/workflows/$($rejected.workflow.id)/decisions" `
  -ContentType "application/json" `
  -Body $rejection

$rejectedResult.workflow | Format-List status, version
$rejectedResult | Select-Object providerSelection, execution, artifacts
```

Expected state:

```text
status = 7  # Rejected
```

Selection evidence, execution evidence, and artifact paths must remain empty.

## 8. Inspect operator state

```powershell
$workflows = Invoke-RestMethod -Uri "$baseUrl/workflows"
$workflows |
  Select-Object `
    @{Name="Id"; Expression={$_.workflow.id}},
    @{Name="Status"; Expression={$_.workflow.status}},
    @{Name="Version"; Expression={$_.workflow.version}},
    @{Name="Provider"; Expression={$_.providerSelection.selectedProviderName}}

$completed.auditEvents |
  Select-Object occurredAtUtc, eventType, message
```

This completes the demonstrated create, pause, restart, approve or reject,
deterministic selection, bounded execution, artifact, status, and audit
lifecycle.
