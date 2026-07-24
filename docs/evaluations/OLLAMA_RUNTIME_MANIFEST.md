# Ollama Windows runtime manifest evaluation

Evaluation date: 2026-07-24

Status: **pending maintainer and security approval**

This evaluation considers the official Ollama Windows x64 standalone runtime.
It does not download or execute any release artifact and does not select a
model or accept a model license.

## Decision

ProjectForge must not yet ship this candidate as an approved runtime manifest.

Ollama publishes an appropriate Windows x64 ZIP and publishes SHA-256 metadata
for it. However, the official versioned download URL is not demonstrably
immutable: Ollama's release workflow uploads release assets with
`gh release upload ... --clobber`. A release asset at the same tag and filename
can therefore be replaced. A reviewed SHA-256 pinned in ProjectForge would make
replacement detectable and fail closed, but the current evidence is not an
independently signed artifact attestation and has not yet received maintainer
approval.

The repository tag commit is shown by GitHub as signature-verified, but that
signature covers the source commit, not the separately uploaded Windows ZIP.
The release pipeline generates a checksum file and GitHub also exposes an asset
digest; neither is an independently signed artifact attestation.

## Evaluated release evidence

The following values are recorded as evaluation evidence only, not as an
approved runtime manifest:

| Field | Observed value |
| --- | --- |
| Version | `0.30.8` |
| Platform | Windows x64 (`windows-amd64`) |
| Artifact | `ollama-windows-amd64.zip` |
| Official URL | `https://github.com/ollama/ollama/releases/download/v0.30.8/ollama-windows-amd64.zip` |
| Observed SHA-256 | `c2d26d97e698027329c252629d7113bbc05d874b49960cbb03e93a39ae9fd95c` |
| Digest source | Official GitHub release API asset metadata for tag `v0.30.8`: `https://api.github.com/repos/ollama/ollama/releases/tags/v0.30.8` |
| Checksum-file URL | `https://github.com/ollama/ollama/releases/download/v0.30.8/sha256sum.txt` |
| Observed checksum-file SHA-256 | `31f7b5bdf8d2129d5679d67961b7f79b9cd362bb4a1e200f0bbe19f7f491a57e` |
| Archive handling | Extract the ZIP into an application-owned, versioned per-user directory; never merge it into an existing runtime directory |
| Entrypoint | `ollama.exe serve`, bound to loopback and launched without elevation |
| Runtime license | Ollama source is MIT-licensed; retain the copyright and license notice |
| Consent | Runtime installation itself does not require license acceptance or administrator elevation. Model acquisition is a separate consent decision |

Ollama's Windows documentation describes
`ollama-windows-amd64.zip` as the standalone CLI plus NVIDIA GPU library
dependencies and explicitly supports embedding it in another application or
running `ollama serve`. It also says old directories should be removed before
an upgrade. ProjectForge should meet that constraint by installing each
version into a fresh directory and switching an application-owned pointer only
after verification and health checks.

Optional AMD ROCm and MLX/CUDA ZIPs are separate artifacts. They are outside
this baseline evaluation and must each receive an independent platform,
hardware, license, URL-immutability, and checksum review before use.

## Integrity gap and approval conditions

An Ollama Windows runtime should be approved through one of these paths:

1. Pin the reviewed version and SHA-256 in source control, download only from
   the official versioned URL, and fail closed on any mismatch. Replacement at
   the upstream URL would then be detectable and rejected.
2. Prefer stronger evidence when available: a content-addressed upstream URL,
   signed provenance binding the archive to the release, or a trusted immutable
   mirror populated by a reviewed release process.

In either case, provisioning must download to a temporary file, verify the
expected SHA-256 before extraction, reject archive path traversal, extract to a
new version directory, verify the expected entrypoint, and perform a
loopback-only readiness check before reporting the provider as ready. A digest
mismatch must permanently reject that download attempt and remove partial
content.

Authenticode verification may be added as defense in depth if Ollama documents
the expected signer identity for standalone binaries. It must not be assumed
from the signed Git tag and was not established by this evaluation.

## Model manifest constraints

The Ollama runtime license does not grant rights to any model. ProjectForge
must treat runtime provisioning and model provisioning as separate manifests
and consent operations.

Ollama's model pull API reports progress for manifest retrieval and individual
digest layers, then reports `verifying sha256 digest` before writing the local
manifest. Official API documentation also identifies model and adapter files
by SHA-256 digest and permits a model to carry one or more license strings.
Accordingly, a ProjectForge model manifest must:

- pin an explicit registry, model name, and non-floating tag or immutable
  manifest digest; `latest` is not acceptable;
- snapshot and verify the registry manifest digest before downloading layers;
- record every layer's media type, byte size, and SHA-256 digest;
- require the runtime to verify every blob and independently verify the final
  cache before declaring readiness;
- identify and display every license layer or license string before download;
- require explicit user consent when model terms require acceptance, impose
  use restrictions, or cannot be classified automatically;
- keep license acceptance scoped to the exact model manifest digest and never
  infer it from acceptance of the Ollama MIT license;
- support cancellation, bounded retries, partial-download cleanup, concurrent
  request coalescing, and restart recovery; and
- avoid selecting a default model until its provenance, compatibility,
  resource requirements, redistribution terms, and usage license have been
  reviewed.

This evaluation intentionally does not name or select a model and does not
accept any third-party model terms.

## Primary sources

- Ollama Windows standalone CLI documentation:
  `https://github.com/ollama/ollama/blob/main/docs/windows.mdx#standalone-cli`
- Ollama release `v0.30.8`:
  `https://github.com/ollama/ollama/releases/tag/v0.30.8`
- Official release API metadata:
  `https://api.github.com/repos/ollama/ollama/releases/tags/v0.30.8`
- Ollama release workflow, including checksum generation and `--clobber` asset
  upload:
  `https://github.com/ollama/ollama/blob/main/.github/workflows/release.yaml`
- Ollama API documentation for model creation and pull verification:
  `https://github.com/ollama/ollama/blob/main/docs/api.md#create-a-model`
  and
  `https://github.com/ollama/ollama/blob/main/docs/api.md#pull-a-model`
- Ollama MIT license:
  `https://github.com/ollama/ollama/blob/main/LICENSE`
