# ProjectForge Repository Instructions

## Team working agreement

All agents working in this repository must follow
`docs/TEAM_WORKING_AGREEMENT.md`. The manager assigns file ownership,
coordinates parallel work, integrates changes, runs final verification, and is
the only agent permitted to stage or commit team changes.

Commit subjects and descriptions must read naturally and follow established
industry practice. Use a concise Conventional Commit subject and, for
non-trivial changes, a short body that explains the reason for the change, the
important behavior, and how it was verified.

## Zero-friction runtime provisioning

ProjectForge must minimize manual setup for end users.

- When a configured provider requires a model, repository, executable, or other
  runtime artifact, the application must detect whether it is available and
  provision it automatically in the background.
- Use pinned versions and trusted sources. Verify downloaded artifacts before
  execution using a published checksum, signature, or an equally strong
  integrity mechanism.
- Store managed dependencies in an application-owned, per-user cache. Reuse
  valid cached artifacts and avoid repeated downloads.
- Concurrent requests for the same dependency must share one provisioning
  operation.
- Surface download, verification, installation, startup, retry, and failure
  states to the user. Normal first-run setup should not require terminal
  commands.
- A provider must not be reported as ready until all required dependencies pass
  health and integrity checks.
- Provisioning must support cancellation, bounded retries, cleanup of partial
  downloads, and recovery after application restart.
- Do not silently make system-wide changes, elevate privileges, weaken host
  security, accept third-party licenses, or download unpinned executable code.
  When one of these actions is unavoidable, explain it clearly and request the
  minimum necessary user consent.
- Keep provider-specific installers and manifests behind provisioning
  contracts. Do not place download or installation logic in orchestration
  policy.
- Tests must cover already-installed dependencies, successful first-run
  provisioning, concurrent requests, integrity failure, cancellation, retry,
  and restart recovery.
