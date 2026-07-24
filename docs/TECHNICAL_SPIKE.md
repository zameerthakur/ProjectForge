# Working Technical Spike

## Objective

Prove that ProjectForge can coordinate one durable, human-approved engineering
workflow across its own policy and state boundaries while delegating execution
through a provider.

The spike is complete when the demonstrated lifecycle works end to end. It is
not a production release and does not attempt broad provider coverage.

## Demonstrated lifecycle

```text
Create workflow
  → evaluate deterministic decision
  → pause for human approval
  → persist workflow and pending approval
  → stop and restart the process
  → approve and resume exactly once
  → select an eligible provider with decision evidence
  → execute a bounded capability
  → write Markdown and JSON artifacts
  → expose status and audit history
```

## Acceptance criteria

### Workflow and approval

- A workflow instance has a stable identifier and explicit state.
- Reaching an approval step persists a pending approval before execution stops.
- Restarting the host restores the workflow and pending approval from SQLite.
- Approving resumes the workflow once; repeated approval does not execute the
  provider again.
- Rejecting terminates the workflow without provider execution.

### Provider selection and execution

- Providers declare capability, locality, required permissions, and estimated
  request cost.
- Mandatory constraints reject ineligible providers before ranking.
- Eligible providers are ranked deterministically.
- The result records why each candidate was selected or rejected.
- A deterministic mock/local provider completes the spike without requiring a
  paid cloud account.

### Artifacts and visibility

- Successful execution writes one human-readable Markdown artifact.
- Successful execution writes one machine-readable JSON artifact.
- An operator can inspect workflow state, pending approvals, selection evidence,
  execution outcome, artifact paths, and audit history.
- An operator can approve or reject a pending workflow through the application.

### Verification

- Unit tests cover provider eligibility, ranking, cost ceilings, health,
  cancellation, and no-match behavior.
- Integration tests cover pause, persisted restart, approve/resume,
  rejection, and idempotent repeated approval.
- The complete solution builds without warnings.
- A documented manual demonstration reproduces the restart/resume lifecycle.

## Deliberate non-goals

- Production authentication or multi-tenancy
- Automatic installation of Ollama or other system software
- Broad AI-provider support
- Production email delivery
- OpenHands production hardening
- Distributed workflow execution
- Enterprise deployment
- Advanced scheduling or workload prediction
- Full dashboard design

These concerns remain on the roadmap after the orchestration boundary has been
proven.

## Delivery and approval

Work is developed on feature branches in reviewable, tested commits. AI tools
may research, implement, test, and document independent slices. The human
maintainer approves architecture changes, milestone completion, merges, and
releases.
