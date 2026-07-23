# ProjectForge

ProjectForge is an open-source orchestration platform for AI-assisted software
engineering. It coordinates workflows, decisions, approvals, execution
providers, project state, and engineering artifacts while delegating specialist
work to established tools.

> ProjectForge owns orchestration. Providers own execution.

## Status

ProjectForge is in its foundation phase. The current code defines
provider-independent engineering capabilities, provider contracts, health
reporting, registration, and an initial resource scheduler.

The first technical milestone is an end-to-end workflow that can pause for human
approval, survive a process restart, select an eligible provider, and produce an
auditable result.

See the [roadmap](docs/ROADMAP.md) for current progress and upcoming milestones.

## Architecture

```mermaid
flowchart LR
    User[Engineer] --> PF[ProjectForge]
    PF --> WF[Workflow and decision layer]
    WF --> Approval[Human approval]
    WF --> Scheduler[Resource scheduler]
    Scheduler --> Provider[Capability provider]
    Provider --> Local[Local tools and models]
    Provider --> Remote[Remote engineering services]
    WF --> State[(Project and workflow state)]
    WF --> Artifacts[Markdown and JSON artifacts]
```

Workflows request an engineering capability and its constraints rather than a
specific product. ProjectForge then selects an eligible provider according to
policy, health, locality, cost, and operational requirements.

Read the [architecture overview](docs/architecture/overview.md) for boundaries,
design principles, and the planned vertical slice.

## Repository

- `ProjectForge.Abstractions` contains stable contracts and domain models.
- `ProjectForge.Core` contains provider registration and orchestration policy.
- `docs` contains the roadmap, architecture, and decision records.

Additional application, infrastructure, provider, and test projects will be
introduced only when required by a validated milestone.

## Prerequisites

- [.NET SDK 8.0.423](https://dotnet.microsoft.com/download/dotnet/8.0)

The required SDK is pinned by `global.json`.

## Build

```shell
dotnet restore
dotnet build --no-restore
```

Run the automated tests with:

```shell
dotnet test --no-restore
```

## Development approach

ProjectForge uses milestone-based delivery:

1. Define acceptance criteria and update the relevant documentation.
2. Implement the smallest end-to-end behavior that proves the milestone.
3. Add automated verification and record meaningful architecture decisions.
4. Update the roadmap and changelog in the same commit.
5. Check in only after the milestone is buildable and its evidence is recorded.

See [CONTRIBUTING.md](CONTRIBUTING.md) for the working agreement.

## License

A project license has not yet been selected. Until one is added, the repository
is publicly visible but no open-source license is granted.
