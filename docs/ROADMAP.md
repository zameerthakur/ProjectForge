# ProjectForge Roadmap

This roadmap records product milestones and their evidence. Dates are not used
as promises; a milestone is complete only when its acceptance criteria are
verified.

## Current state

ProjectForge has an initial .NET solution with capability-oriented provider
contracts, deterministic provider selection, bounded health checks, shared
runtime-provisioning boundaries, and an automated test suite. There is not yet
an executable host, persisted workflow, concrete provider integration, or
dashboard.

## M0 — Repository foundation

Status: In progress

Outcome: The project has a buildable baseline and a professional, maintainable
engineering workflow.

Acceptance criteria:

- [x] Public README describes the product, status, architecture, and build.
- [x] Living roadmap and architecture overview exist.
- [x] Contribution and milestone check-in standards are documented.
- [x] Initial architecture decision is recorded.
- [x] Solution builds from a clean restore.
- [ ] Foundation is checked into Git and synchronized to GitHub.

## M1 — Provider selection policy

Status: In progress

Outcome: ProjectForge deterministically selects the best eligible provider and
explains the decision.

Acceptance criteria:

- [x] Provider metadata represents locality, execution features, and estimated
  cost.
- [x] Mandatory constraints eliminate ineligible providers.
- [x] Ranking applies explicit, testable policy after eligibility.
- [x] Health checks use one coherent abstraction with cancellation and timeouts.
- [x] Selection returns decision evidence, not only the chosen provider.
- [x] Unit tests cover local preference, cloud prohibition, cost limits,
  unhealthy providers, ties, cancellation, and no-match behavior.

## M2 — Durable approval workflow

Status: In progress

Outcome: A workflow can pause for approval, restart, and resume exactly once.

Acceptance criteria:

- [x] An executable worker/API host composes the application.
- [x] Microsoft Agent Framework workflow behavior is validated in an isolated
  spike.
- [x] Workflow and approval state persist in SQLite.
- [x] A pending approval survives process termination.
- Resumption is idempotent and produces an audit trail.

## M3 — First real execution provider

Status: Planned

Outcome: An approved workflow executes through one local AI provider.

Acceptance criteria:

- Ollama and LiteLLM boundaries are evaluated separately.
- Secrets and provider configuration remain outside source control.
- Provider health, model availability, timeout, and failure modes are observable.
- Execution produces structured JSON and human-readable Markdown artifacts.

## M4 — Coding provider

Status: Planned

Outcome: ProjectForge delegates a sandboxed repository task to OpenHands.

Acceptance criteria:

- OpenHands runs out of process behind the capability-provider contract.
- Repository access and write permissions are explicit.
- Tool activity and produced changes are auditable.
- Failed or cancelled work leaves the repository in a known state.

## M5 — Operator dashboard

Status: Planned

Outcome: A Blazor dashboard presents workflow state without owning orchestration
logic.

Acceptance criteria:

- Current workflows, tasks, providers, approvals, artifacts, and failures are
  visible.
- Operators can approve, reject, pause, and resume authorized workflows.
- Dashboard state comes from application APIs and persisted events.

## Later milestones

- GitHub provider and pull-request workflow
- Document and diagram providers
- Notifications
- Search and project memory
- Scheduled/background workflows
- Installer and local runtime automation
- Authentication and multi-user authorization
- Deployment, release, and maintenance capabilities
