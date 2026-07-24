# Ollama Evaluation for M3

Status: Recommended for a bounded local-provider implementation, with managed
installation deferred until ProjectForge has an integrity-pinned runtime and
model manifest.

Evaluated: 2026-07-24

## Decision summary

Ollama is a good fit for ProjectForge's first real local AI provider. Its local
HTTP API directly supports service/version checks, installed-model discovery,
model metadata, chat generation, streaming, and model pulls. The provider can
remain out of process, declare local execution, and avoid repository, file, and
tool access.

The first implementation should connect to an already running Ollama instance
on loopback and require an already installed, configured model whose digest is
allowlisted. This delivers real execution without silently installing
software, accepting model terms, or relying on mutable model tags.

Automatic runtime and model provisioning should follow as a separate slice.
ProjectForge must own a pinned manifest containing the exact Ollama version,
platform artifact URL, published SHA-256, expected model digest, model license
metadata, cache location, and consent policy. Ollama's API exposes useful
digests after installation, but its documented pull request does not accept an
expected digest. A tag such as `gemma3` is therefore not, by itself, a
sufficient integrity pin.

## Fit with existing contracts

An `OllamaCapabilityProvider` can implement `ISchedulableCapabilityProvider`
with these characteristics:

- `ExecutionLocation`: local.
- Repository access, file writes, and tool execution: false.
- Initial supported capability: bounded text generation only.
- `CanExecuteAsync`: validate the request shape and confirm the configured
  model name and expected digest through `GET /api/tags` (or model details
  through `POST /api/show`).
- `CheckHealthAsync`: call `GET /api/version`, enforce the configured compatible
  version range, then verify the configured model and digest. A listening
  server without the required model is not ready.
- `ExecuteAsync`: call `POST /api/chat` with `stream: false` for the first
  implementation, map the response into ProjectForge's structured execution
  result, and preserve Ollama timing/token metadata as provider evidence.

The existing `ProviderHealthChecker` supplies a bounded outer health timeout.
The provider must still use an `HttpClient` with request cancellation and
separate bounded connect, response, and generation policies. The existing
workflow execution timeout remains the final upper bound.

The existing `IRuntimeProvisioner` split is appropriate:

1. An Ollama runtime provisioner owns the executable archive and server
   lifecycle.
2. A separate Ollama model provisioner owns one exact model dependency.
3. The capability provider only checks readiness and executes requests; it
   must not download or install dependencies.

## Runtime and pinning options

Ollama supports native Windows, macOS, and Linux distributions, plus Docker.
On Windows, the documented installer is per-user and does not require
Administrator privileges. A standalone Windows ZIP is also available and is
the better eventual managed-runtime candidate because ProjectForge can unpack
it into an application-owned per-user cache and run `ollama serve` without
making system-wide changes. On Linux, the standard script and service
instructions use privileged system locations and systemd, so they do not meet
ProjectForge's unattended, non-elevated provisioning policy. A portable,
verified archive in the ProjectForge cache is preferable where supported.

Ollama's Linux installer accepts `OLLAMA_VERSION`, and official GitHub releases
are versioned. The official release workflow generates `sha256sum.txt` and
uploads it with release artifacts. ProjectForge must pin a reviewed release
version and copy its artifact hash into a checked-in manifest; it must never
execute a remote install script or follow "latest" at runtime. A candidate
version is not approved merely because it is the newest release.

The managed process should bind only to loopback on a dynamically reserved or
configured port, use an application-owned `OLLAMA_MODELS` directory, and avoid
changing user or system environment variables. Ollama defaults to
`127.0.0.1:11434`; `OLLAMA_HOST` can change the bind address. ProjectForge
should reject non-loopback endpoints by default and require explicit operator
configuration for remote endpoints.

## HTTP API boundary

The initial adapter needs only four native endpoints:

| Purpose | Endpoint | ProjectForge use |
| --- | --- | --- |
| Runtime identity | `GET /api/version` | Process health and compatible-version check |
| Installed models | `GET /api/tags` | Availability, size, details, and digest verification |
| Model metadata | `POST /api/show` | Capabilities and model license visibility |
| Execution | `POST /api/chat` | Non-streaming text generation |

`POST /api/pull` belongs to the model provisioner, not the execution provider.
It streams progress by default and supports non-streaming mode. Streaming API
responses use newline-delimited JSON. For the first execution adapter,
`stream: false` reduces parsing and mid-stream failure complexity. Streaming
can be added later with explicit accumulation, output-size limits, and handling
for an `error` object after an HTTP 200 response has begun.

Ollama documents its API as stable and backwards compatible, but not strictly
versioned. ProjectForge should therefore test against the pinned runtime and
treat an unsupported version as unhealthy rather than assuming compatibility.

## Readiness and model availability

Readiness is a conjunction, not merely a successful TCP connection:

1. The endpoint is loopback (unless explicitly configured otherwise).
2. `GET /api/version` succeeds within the health timeout.
3. The returned runtime version satisfies the configured policy.
4. `GET /api/tags` contains the exact configured model.
5. Its reported digest equals the manifest's expected digest.
6. `POST /api/show` reports required capabilities, such as `completion`.

`GET /api/ps` reports models currently loaded in memory, VRAM use, context
length, and expiry. It is useful operational telemetry but must not be a
readiness requirement because an installed model need not remain loaded.

Model pulls may be very large: Ollama's Windows documentation warns that models
can consume tens to hundreds of gigabytes. Before pulling, the provisioner
should show the expected download and installed size, model identity, license,
destination, and consent requirement; validate free disk space; use a partial
staging area; and expose progress. After completion it must verify the digest
reported by `/api/tags` against the ProjectForge manifest before declaring
readiness. A mismatch must quarantine or remove only the staged managed
artifact and fail closed.

## Integrity, security, and consent

The Ollama repository is MIT licensed. Model licenses are independent and can
be returned by `POST /api/show`; they may impose additional terms. ProjectForge
must not infer that the runtime license covers a model.

The security boundary should include:

- loopback-only access by default, because the local API is HTTP and the
  documented default bind address is loopback;
- no automatic use of `insecure: true` on model pulls;
- no cloud model or authenticated Ollama account behavior in the first slice;
- redaction of prompts and generated content from routine logs;
- maximum request and response sizes, generation limits, and a bounded context;
- exact runtime and model digest checks before scheduling;
- no shell interpolation or CLI execution for prompts; and
- explicit operator consent before downloading a model whose terms require
  acceptance.

The documented model API exposes the installed model digest and license, but
the pull documentation does not establish that callers can supply an expected
digest or inspect license terms before downloading. Consequently, ProjectForge
needs a trusted, reviewed manifest created before provisioning. If an
acceptable license and expected digest cannot be established out of band, the
model must remain operator-provided rather than automatically downloaded.

## Cancellation, timeouts, and lifecycle

All HTTP calls should pass the workflow cancellation token. Cancellation of
chat generation should dispose the response/request promptly. ProjectForge
should distinguish operator cancellation from connect timeout, health timeout,
generation timeout, and provider failure in durable evidence.

For a ProjectForge-managed runtime, process ownership must be explicit:

- start `ollama serve` as a child process with redirected logs;
- wait for version and model readiness with bounded retry/backoff;
- terminate only the process ProjectForge started;
- clean incomplete downloads after cancellation or failure;
- preserve verified cached artifacts for reuse;
- recover stale staging state after restart; and
- coordinate concurrent requests through the existing provisioning
  coordinator so only one runtime/model operation runs.

The current coordinator lets an individual waiter cancel without cancelling
the shared operation. That is useful for concurrency, but the underlying
provisioning call is presently started without a cancellation token. Before
large Ollama model downloads are managed automatically, the coordinator needs
an owned lifecycle cancellation policy that can stop provisioning during
application shutdown while preserving other waiters during normal operation.

## Observable failure modes

The adapter and provisioners should map at least these conditions:

- connection refused or DNS/endpoint configuration failure;
- incompatible or malformed runtime version;
- missing model or model digest mismatch;
- insufficient disk space or unwritable cache;
- pull network, proxy, TLS, registry, or integrity failure;
- HTTP `400`, `404`, `429`, `500`, and `502`;
- an NDJSON error emitted after streaming has begun;
- model load failure, out-of-memory, or GPU/driver failure;
- generation timeout or caller cancellation;
- malformed/truncated JSON and unsupported response shape; and
- managed child process exit, crash, or failure to become ready.

Health metadata should expose endpoint classification (without credentials),
runtime version, configured model, installed digest, model size/format/
quantization, last failure category, and measured latency. It should not expose
prompts, secrets, or full model license text.

## Bounded first implementation

Implement one `OllamaCapabilityProvider` using `HttpClient` and native Ollama
JSON contracts, with configuration for a loopback base URI, exact model name,
expected digest, compatible runtime version, and generation timeout.

Scope:

- support non-streaming `/api/chat` text generation only;
- advertise no repository, file-write, or tool-execution access;
- require an already running Ollama runtime and already installed model;
- verify runtime, model, digest, and completion capability in health checks;
- produce the existing structured JSON and Markdown workflow artifacts;
- classify errors and record provider timing/token evidence; and
- add deterministic tests with a fake HTTP handler for success, model missing,
  digest mismatch, incompatible version, HTTP failures, malformed responses,
  timeout, and cancellation.

Do not include runtime installation, model pulling, remote endpoints, cloud
models, authentication, streaming, embeddings, vision, thinking traces, tool
calling, or automatic license acceptance in this slice.

This boundary provides genuine local AI execution for M3 while keeping
zero-friction provisioning as a safe follow-up. The follow-up should first add
the signed-off runtime/model manifest and lifecycle cancellation semantics,
then implement cached runtime acquisition and model pulling behind the
existing provisioning contracts.

## Official sources

- Ollama API introduction and versioning:
  <https://docs.ollama.com/api/introduction>
- Chat endpoint:
  <https://docs.ollama.com/api/chat>
- Streaming behavior:
  <https://docs.ollama.com/api/streaming>
- Error format and status codes:
  <https://docs.ollama.com/api/errors>
- Runtime version endpoint:
  <https://docs.ollama.com/api-reference/get-version>
- Installed model list and digests:
  <https://docs.ollama.com/api/tags>
- Model metadata and license:
  <https://docs.ollama.com/api-reference/show-model-details>
- Running-model telemetry:
  <https://docs.ollama.com/api/ps>
- Model pull endpoint:
  <https://docs.ollama.com/api/pull>
- Windows installation and storage:
  <https://docs.ollama.com/windows>
- Linux installation and version selection:
  <https://docs.ollama.com/linux>
- Server configuration, bind address, proxy, and model storage:
  <https://docs.ollama.com/faq>
- Ollama runtime license:
  <https://github.com/ollama/ollama/blob/main/LICENSE>
- Official release workflow and checksum generation:
  <https://github.com/ollama/ollama/blob/main/.github/workflows/release.yaml>
