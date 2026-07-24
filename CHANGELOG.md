# Changelog

All notable changes to ProjectForge will be documented in this file.

The format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/).
ProjectForge does not yet publish versioned releases.

## Unreleased

### Added

- Initial capability, provider, health, and scheduling contracts.
- Initial provider registry and first-match technical-spike scheduler.
- Deterministic provider selection with eligibility evidence, cost and locality
  policy, cancellation, and bounded health checks.
- Shared runtime-provisioning contracts and coordination boundaries with
  lifecycle reporting, verification, concurrency control, and retry recovery.
- Targeted runtime readiness by stable dependency ID with observable snapshots,
  shared operations, waiter isolation, and application-shutdown cancellation.
- Provider-neutral trusted artifact manifests with strict validation for pinned
  sources, integrity, platform, archive, entrypoint, license, and consent data.
- Automated tests for provider policy and runtime-provisioning coordination.
- Application workflow coordination that durably creates pending approvals and
  records optimistic human decisions through the workflow-store boundary.
- SQLite workflow, approval, and audit persistence with transactional,
  version-checked decisions and restart recovery.
- Executable HTTP host for creating, inspecting, approving, and rejecting
  workflows with validated requests and stable error responses.
- Isolated Microsoft Agent Framework validation for typed graphs,
  human-in-the-loop requests, and checkpoint rehydration.
- End-to-end approved workflow execution through the deterministic local
  provider, with durable evidence, Markdown/JSON artifacts, and interrupted-run
  reconciliation.
- Separate Ollama and LiteLLM boundary evaluations selecting direct,
  loopback-only Ollama integration as the first real local AI provider.
- Bounded direct Ollama execution with runtime, model, digest, and capability
  readiness checks, external configuration, sanitized failures, and token
  evidence.
- Operator-safe provider readiness through `GET /providers`.
- Public project overview, architecture documentation, roadmap, contribution
  workflow, and Architecture Decision Record process.
