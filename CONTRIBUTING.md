# Contributing to ProjectForge

ProjectForge is developed through small, reviewable milestones. Code,
documentation, and verification evidence are treated as one deliverable.

## Working agreement

- Keep provider-specific behavior behind ProjectForge contracts.
- Prefer deterministic code for decisions that do not require AI.
- Do not introduce infrastructure before a milestone demonstrates its need.
- Preserve cancellation, auditability, and explicit failure behavior across
  asynchronous boundaries.
- Update public documentation when behavior or project status changes.
- Capture durable architectural choices in an Architecture Decision Record.

## Branches and commits

Use short-lived branches named by purpose, such as:

```text
feat/m1-provider-selection
fix/provider-health-timeout
docs/workflow-boundaries
```

Use conventional commit subjects:

```text
feat(scheduling): enforce provider eligibility policy
test(scheduling): cover local and cloud fallback
docs(roadmap): complete milestone 1
```

A milestone check-in must:

- build successfully;
- pass its automated tests;
- contain no generated build output;
- update `docs/ROADMAP.md`;
- update `CHANGELOG.md`;
- update the README or architecture documents when public behavior changed.

## Pull requests

Pull requests should explain:

- the user or engineering outcome;
- the important implementation choices;
- verification performed;
- known limitations or deferred work;
- documentation and decision records changed.

## Architecture decisions

Create an ADR in `docs/decisions` when a change establishes a lasting boundary,
selects a foundational dependency, or accepts a meaningful trade-off. ADRs are
append-only: supersede an accepted decision with a new record instead of
rewriting its history.
