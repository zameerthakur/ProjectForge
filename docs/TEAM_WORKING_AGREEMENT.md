# ProjectForge Team Working Agreement

## Purpose

This agreement defines how the ProjectForge agent team plans, implements,
reviews, verifies, and commits work. It applies to the manager and every senior
team member.

## Team structure

The approved team has five roles:

1. Manager and integration owner
2. Senior application and workflow engineer
3. Senior persistence and infrastructure engineer
4. Senior verification and quality engineer
5. Senior architecture and API engineer

The execution environment supports four active agents at once, including the
manager. The manager runs with three senior engineers in parallel and rotates
the fourth senior role into the next available slot.

## Manager responsibilities

The manager:

- decomposes milestones into independent, bounded assignments;
- assigns exclusive file and directory ownership before parallel work begins;
- makes routine code, architecture, integration, and test-scope decisions;
- prevents or resolves overlapping edits;
- reviews every contribution against repository instructions and acceptance
  criteria;
- owns shared files, including the solution, roadmap, changelog, root
  documentation, and Git configuration;
- runs final Release builds, tests, formatting, and staged-diff checks;
- is the only agent that stages files, creates commits, changes branches,
  pushes, or opens pull requests; and
- communicates material risks, external blockers, and milestone status to the
  maintainer.

Platform security controls remain outside the manager's authority. Sandbox
elevation, network access, credentials, external-system mutations, and other
protected actions may still require explicit maintainer approval.

## Senior engineer responsibilities

Each senior engineer:

- works only within the assigned scope and owned files;
- reads and follows the applicable `AGENTS.md` instructions before editing;
- follows the established architecture, naming, formatting, nullable-reference,
  asynchronous, cancellation, and testing conventions;
- reports interface or ownership conflicts to the manager instead of editing
  another agent's files;
- adds behavior-focused tests with production changes;
- runs the narrowest relevant build and test commands;
- reports files changed, decisions, risks, and verification results; and
- never stages, commits, rebases, pushes, or modifies shared Git state.

## Parallel work rules

- Parallel assignments must be independently useful and have non-overlapping
  file ownership.
- Shared files are manager-owned unless explicitly reassigned.
- Agents may read any in-scope code but must not edit files owned by another
  active agent.
- Cross-cutting contract changes are proposed to the manager before
  implementation.
- The manager integrates completed slices one at a time and reruns affected
  tests after every integration.
- A finished or blocked senior role releases its concurrency slot so the next
  approved role can rotate in.

## Engineering standards

All production code must:

- use clear domain language and existing ProjectForge naming conventions;
- preserve separation between abstractions, application orchestration, core
  policy, infrastructure, providers, and hosts;
- keep provider-specific installation and execution details behind contracts;
- validate public inputs and maintain state invariants;
- use cancellation tokens for asynchronous work and bounded timeouts for
  external operations;
- avoid blocking asynchronous calls and fire-and-forget work without explicit
  lifecycle ownership;
- make concurrent and repeated operations idempotent where required;
- persist related workflow state and audit changes transactionally;
- avoid secrets, machine-specific configuration, and system-wide changes;
- use pinned dependencies from trusted sources; and
- compile without warnings in Release configuration.

Tests must be deterministic, isolated, and cover successful behavior, invalid
input, cancellation, failure, concurrency, retry or stale-state behavior, and
restart recovery where applicable.

## Git and commit policy

- Commits must be atomic, coherent, tested, and reviewable.
- Commit messages use Conventional Commits, such as `feat(scope): summary`,
  `fix(scope): summary`, `test(scope): summary`, or `docs(scope): summary`.
- Subjects use natural, human-written, imperative language, remain concise,
  and describe the observable change rather than the editing activity.
- Non-trivial commits include a short human-readable body explaining why the
  change is needed, the important design or behavior, and the verification
  performed.
- Commit messages avoid generated-sounding boilerplate, exhaustive file lists,
  vague summaries, internal agent terminology, and claims not supported by
  tests or other evidence.
- Unrelated changes must never be bundled into the same commit.
- Partial scaffolding, failing builds, generated output, local databases,
  secrets, and temporary files must not be committed.
- Before each commit, the manager reviews the staged diff, runs
  `git diff --cached --check`, and executes verification proportional to risk.
- Architecture changes, milestone completion, merges, releases, destructive
  Git operations, and publication remain subject to maintainer authority.

## Documentation and acceptance

- Milestone implementation and its evidence must remain aligned with
  `docs/TECHNICAL_SPIKE.md` and `docs/ROADMAP.md`.
- Material user-visible or architectural changes update the changelog and
  relevant documentation in the same coherent commit.
- Acceptance criteria are marked complete only after automated evidence exists.
- The manager must not report a spike or milestone complete while required
  behavior, restart evidence, artifacts, operator visibility, or manual
  demonstration remains missing.

## Communication and continuity

- The manager provides concise progress updates while work is active.
- Agents escalate blockers and contract gaps early.
- Routine implementation decisions do not interrupt the maintainer.
- Work continues until the maintainer explicitly says `pause` or `stop`.
- On `pause` or `stop`, the manager halts new work, preserves the current
  workspace state, and reports uncommitted changes and active-agent status.
- Resuming work requires an explicit maintainer instruction.
