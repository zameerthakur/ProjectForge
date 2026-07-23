# ADR 0001: Own orchestration, integrate execution

- Status: Accepted
- Date: 2026-07-23

## Context

AI engineering tools evolve quickly and already provide specialized coding,
model inference, source-control, search, testing, and document capabilities.
Rebuilding them would dilute ProjectForge's focus and create permanent
maintenance obligations.

ProjectForge's distinct value is coordinating these capabilities through
repeatable engineering workflows with decisions, approvals, policy, state, and
auditability.

## Decision

ProjectForge will own:

- engineering workflow definitions and lifecycle;
- deterministic decision and routing policy;
- human approvals and governance;
- capability-based provider selection;
- project and workflow state;
- artifact coordination and operator visibility.

Execution systems will be integrated behind ProjectForge-owned contracts.
Workflows request capabilities and constraints rather than concrete products.
Provider-specific SDKs and data types remain in adapter projects.

ProjectForge may adopt an external workflow runtime while retaining ownership of
the workflow definitions and application semantics.

## Consequences

Positive:

- Execution providers can evolve or be replaced independently.
- Core workflows remain testable without external services.
- ProjectForge stays focused on governance and lifecycle coordination.

Costs:

- Adapter boundaries and compatibility testing are required.
- External processes introduce deployment, health, security, and versioning
  concerns.
- A capability model must be expressive enough to avoid provider-specific
  leakage.

## Follow-up decisions

Separate ADRs will evaluate:

- Microsoft Agent Framework as the workflow runtime;
- SQLite persistence boundaries;
- Ollama and LiteLLM integration topology;
- OpenHands process and sandbox isolation;
- provider-selection policy and decision evidence.
