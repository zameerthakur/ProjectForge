# Architecture Overview

## Product boundary

ProjectForge coordinates the software-engineering lifecycle. It owns workflow
definitions, deterministic decisions, approvals, provider selection, governance,
project state, artifacts, and the operator experience.

It does not aim to become a coding agent, model runtime, Git implementation,
search engine, test framework, or build system. Those responsibilities are
integrated through providers.

## Layering

### Abstractions

Domain concepts and contracts that do not depend on a provider, storage engine,
web framework, or workflow runtime.

### Core

Deterministic orchestration policy: provider eligibility and ranking, decision
rules, execution coordination, and domain services.

### Application host

The future composition root. It will configure dependency injection, workflows,
background execution, APIs, authentication, telemetry, and process lifetime.

### Infrastructure and providers

Adapters for SQLite, Microsoft Agent Framework, Ollama, LiteLLM, OpenHands, Git
providers, notifications, documents, and other external systems. Provider
failures must not leak provider-specific types into the core.

### Dashboard

A Blazor operator interface over application APIs. It observes and commands the
system but does not become a second orchestration engine.

## Provider selection

Selection has two distinct phases:

1. **Eligibility:** enforce mandatory capability, privacy, access, approval,
   health, and cost constraints.
2. **Ranking:** compare eligible providers using explicit policy such as
   locality, predicted quality, total cost, latency, and current load.

The result must include decision evidence so operators can understand why a
provider was selected or rejected.

## Workflow ownership

ProjectForge will define business workflows and decision policy while using a
workflow runtime for graph execution, checkpointing, and resumption. Approval
is a workflow concern; provider execution must not begin until the workflow
proves that its required approval is satisfied.

## Persistence

SQLite is the initial local persistence target. Domain state and audit events
must be accessible through ProjectForge-owned interfaces so persistence choices
do not shape core policy.

## Observability

Workflow, decision, approval, scheduling, and provider operations will emit
structured telemetry through OpenTelemetry. Logs are diagnostic evidence, not a
substitute for durable business audit records.

## First vertical slice

The initial integration milestone will:

1. accept a capability request;
2. execute a deterministic decision;
3. pause for human approval;
4. persist the workflow and pending request;
5. survive a process restart;
6. resume from the recorded decision;
7. select and execute a provider;
8. emit Markdown and JSON artifacts;
9. expose status and audit history.

This slice is intentionally narrow. Additional providers and user-interface
features follow only after the lifecycle is reliable.
