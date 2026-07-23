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
- Automated tests for provider policy and runtime-provisioning coordination.
- Application workflow coordination that durably creates pending approvals and
  records optimistic human decisions through the workflow-store boundary.
- Public project overview, architecture documentation, roadmap, contribution
  workflow, and Architecture Decision Record process.
