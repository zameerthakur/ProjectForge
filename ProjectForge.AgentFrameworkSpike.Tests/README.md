# Microsoft Agent Framework workflow spike

This isolated test project validates the Microsoft Agent Framework workflow
surface without adding the framework to a ProjectForge production project.

The spike pins `Microsoft.Agents.AI.Workflows` to version `1.14.0` and verifies:

- a type-safe two-executor graph runs and emits its declared output;
- a typed external approval request pauses and resumes a streaming run; and
- a new run can rehydrate from an in-memory superstep checkpoint without
  repeating the executor that completed before that checkpoint.

Run the spike independently:

```powershell
dotnet test ProjectForge.AgentFrameworkSpike.Tests `
  --disable-build-servers -m:1 /nodeReuse:false
```

## Decision

The framework APIs are technically viable for graph execution and checkpoint
rehydration. They should not replace ProjectForge-owned workflow, approval,
audit, and SQLite state during M2:

- the in-memory checkpoint manager used here is not restart durable;
- adopting a durable checkpoint store would introduce a second workflow state
  authority beside ProjectForge's SQLite store; and
- checkpoint rehydration can repeat work after the selected checkpoint, so
  external provider execution still requires ProjectForge idempotency and
  execution-claim boundaries.

Keep the framework isolated until a later milestone demonstrates one durable
state owner and an idempotent provider execution protocol.

## Official sources

- [Workflow overview](https://learn.microsoft.com/en-us/agent-framework/workflows/)
- [Workflow builder and execution](https://learn.microsoft.com/en-us/agent-framework/workflows/workflows)
- [Checkpoints and rehydration](https://learn.microsoft.com/en-us/agent-framework/workflows/checkpoints)
- [Human-in-the-loop requests](https://learn.microsoft.com/en-us/agent-framework/workflows/human-in-the-loop)
- [Microsoft NuGet package](https://www.nuget.org/packages/Microsoft.Agents.AI.Workflows/1.14.0)
