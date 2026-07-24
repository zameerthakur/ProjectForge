# LiteLLM boundary evaluation

## Decision

LiteLLM is a good **optional gateway provider** for ProjectForge, but it should
not be the first local execution provider in M3. Implement a direct Ollama
provider first, then add LiteLLM when ProjectForge needs one OpenAI-compatible
boundary across multiple local and cloud model backends.

The distinction matters: running the LiteLLM gateway on the user's machine does
not make an execution local. The configured upstream determines where prompts,
source excerpts, and outputs are processed. A LiteLLM route targeting Ollama is
local; a route targeting a hosted API is cloud execution and must be described
and scheduled as such.

## Product shape and deployment modes

LiteLLM offers two integration modes:

- a Python SDK embedded in a Python application; and
- an out-of-process proxy/gateway with an OpenAI-compatible HTTP API.

The proxy is the appropriate ProjectForge boundary. It preserves the existing
.NET provider contract, isolates Python dependencies, and allows ProjectForge
to treat LiteLLM as a supervised external runtime. Embedding the SDK would
introduce a Python bridge into the application and couple orchestration to
provider-specific implementation details.

The proxy can run:

- without a database, using a generated configuration file;
- with PostgreSQL and the Admin UI for model management, virtual keys, and
  spend tracking; or
- as an externally managed gateway supplied by an operator.

For the first adapter, use the database-free proxy and an application-owned
configuration. PostgreSQL, Redis, the Admin UI, and multi-worker deployment add
operational weight that is not justified for a single-user local provider.

Sources:

- [LiteLLM getting started](https://docs.litellm.ai/)
- [Proxy quickstart and database-free mode](https://docs.litellm.ai/docs/proxy/docker_quick_start)

## Mapping to ProjectForge contracts

Implement LiteLLM as an `ISchedulableCapabilityProvider` backed by a typed
`HttpClient`:

- `Name`: a stable configured identity, not a model display name.
- `Descriptor.ExecutionLocation`: derived from every upstream deployment in the
  selected model group. Mark it local only when ProjectForge can prove all
  eligible upstreams are local. Mixed or unknown routes must be treated as
  cloud-capable and rejected when cloud execution is prohibited.
- Repository, file-write, and tool-execution flags: `false`. LiteLLM is a model
  gateway, not a repository sandbox or command runner. Model tool-call output
  is data and must not be confused with permission to execute a tool.
- `CanExecuteAsync`: require a supported ProjectForge capability, valid model
  mapping, acceptable execution location, reachable gateway, and a healthy
  selected model.
- `EstimateCostAsync`: use configured conservative metadata. Local Ollama
  routes may report zero monetary cost; unknown hosted pricing must not be
  represented as zero.
- `ExecuteAsync`: call `/v1/chat/completions` (or a later explicitly selected
  Responses API boundary), correlate the ProjectForge request/workflow IDs in
  adapter metadata, and normalize output and usage into
  `CapabilityExecutionResult`.

LiteLLM supports Ollama through `ollama/...` and `ollama_chat/...` model
prefixes and a configured Ollama `api_base`. This makes LiteLLM useful as a
later normalization layer, but it still requires Ollama and the model to be
provisioned separately.

Source: [LiteLLM Ollama provider](https://docs.litellm.ai/docs/providers/ollama)

## Health and model availability

Gateway process health and model health are separate:

- `GET /health/liveliness` checks only that the proxy process is alive.
- `GET /health/readiness` checks that the worker can accept traffic and, when
  configured, that its database is reachable.
- authenticated `GET /health?model=<name>` performs a real model request and
  can consume tokens or incur cost.
- `/v1/model/info` supplies model identifiers used by targeted health checks.

ProjectForge must not report the provider ready after liveness alone. Readiness
requires the proxy probe plus a bounded check of the configured model. Cache a
recent model-health result to avoid a paid request on every scheduler
evaluation, expose its age in `ProviderHealthReport.Metadata`, and redact
endpoint URLs and upstream error details before returning operator-visible
metadata.

LiteLLM's documented default per-model health timeout is 60 seconds, longer than
ProjectForge's current 10-second provider-health budget. Configure a shorter
LiteLLM health timeout and retain ProjectForge's outer timeout.

Source: [LiteLLM health checks](https://docs.litellm.ai/docs/proxy/health)

## Secrets and configuration

Provider API keys, the LiteLLM master key, the salt key, virtual keys, database
credentials, and generated configuration containing secrets must remain
outside source control.

For an application-managed local proxy:

- bind to loopback, not all interfaces;
- generate a strong per-installation master key and store it in the operating
  system credential store;
- pass provider secrets by a protected environment or secret-file mechanism,
  while configuration refers to `os.environ/NAME`;
- never log authorization headers, raw configuration, prompts, or provider
  responses by default;
- restrict the generated config and logs to the current user; and
- omit the Admin UI and database unless a later feature requires them.

If database-backed key storage is enabled, `LITELLM_SALT_KEY` must be durable:
changing it prevents decryption of existing credentials. The master key is an
administrator credential and must not be reused as an ordinary workflow key.

Sources:

- [Proxy quickstart: salt, master key, and environment references](https://docs.litellm.ai/docs/proxy/docker_quick_start)
- [Production best practices](https://docs.litellm.ai/docs/proxy/prod)
- [Configuration environment references](https://docs.litellm.ai/docs/proxy/configs)

## Runtime pinning, integrity, and provisioning

The source project currently declares Python `>=3.10, <3.15`; its SDK
dependencies use compatible ranges, while the project states that reproducible
Docker/CI resolution comes from its lock file. ProjectForge therefore must not
install an unpinned `litellm` package into a user's global Python environment.

Prefer a pinned, non-root container image by immutable digest when a compatible
container runtime already exists. Verify the image with the publisher's cosign
key and expected identity before first execution, record the digest and
signature evidence, and reject unsigned or mismatched artifacts. LiteLLM
publishes signature verification instructions, but individual releases still
need validation: its v1.86.0 notes, for example, identify an unsigned non-root
image.

A managed-Python fallback would need an application-owned Python runtime,
exactly locked wheels and transitive dependencies, hashes for every artifact,
an isolated environment, vulnerability policy, and provenance tied to an
official GitHub release. This is materially harder to provision safely. The
project's official incident record states that PyPI versions 1.82.7 and 1.82.8
were compromised; those versions must be permanently denied, and a bare
`pip install litellm` is unacceptable.

Provisioning must use `IRuntimeProvisioner` requirements for the proxy runtime
and for each upstream runtime/model independently. It must share concurrent
downloads, report download/verification/startup states, clean partial
artifacts, use bounded retries, recover after restart, and become ready only
after signature, liveness, readiness, and model checks pass.

Installing Docker Desktop or another system-wide container runtime is not a
silent provisioning step. If none is available, ProjectForge must explain the
requirement and obtain consent, or offer the separately secured managed-Python
path. It must not elevate privileges or accept third-party terms on the user's
behalf.

Sources:

- [LiteLLM project metadata](https://github.com/BerriAI/litellm/blob/main/pyproject.toml)
- [Signed container verification](https://github.com/BerriAI/litellm#verify-docker-image-signatures)
- [LiteLLM releases](https://github.com/BerriAI/litellm/releases)
- [Official LiteLLM PyPI compromise record](https://github.com/BerriAI/litellm/issues/24518)

## Cancellation, timeouts, and retries

ProjectForge should pass its cancellation token to `HttpClient.SendAsync` and
use its existing bounded execution timeout as the outer authority. Configure
LiteLLM's `request_timeout` below that outer deadline so the gateway has time to
return a normalized timeout response. Configure retries explicitly; the
documented proxy configuration supports both request retries and router
retries, which otherwise can multiply latency.

Cancelling the client request closes ProjectForge's wait, but it does not prove
that a remote upstream stopped generating or billing. Record cancellation as
an indeterminate upstream outcome where applicable. Do not automatically replay
a request after an ambiguous disconnect unless the selected API and upstream
offer a durable idempotency guarantee. ProjectForge's workflow claim remains
the exactly-once scheduling boundary, not proof of exactly-once model inference.

Source: [LiteLLM timeout and retry configuration](https://docs.litellm.ai/docs/proxy/configs)

## Failure taxonomy and observability

Normalize at least these failure modes without exposing secrets:

- proxy missing, crash, startup timeout, or version mismatch;
- readiness failure or unavailable database;
- configured model missing, unhealthy, loading, or evicted;
- Ollama/upstream runtime unreachable;
- authentication or authorization failure;
- provider rate limit, quota, or budget rejection;
- invalid request or unsupported model feature;
- context-window or content-policy rejection;
- gateway timeout, upstream timeout, or ProjectForge outer timeout;
- malformed or truncated response;
- connection loss with unknown upstream completion state;
- exhausted LiteLLM retry/fallback chain; and
- configuration or secret-resolution failure.

Persist the gateway version, logical model name, opaque deployment/model ID,
local/cloud classification, request latency, token usage when supplied, retry
count when safely available, and normalized failure code. Never persist raw
keys. Treat upstream URLs and exception bodies as sensitive unless sanitized.

## License and consent

The repository's general source is MIT-licensed, while content under its
`enterprise/` directory is governed separately. ProjectForge should use only
the open-source proxy features required by the adapter, preserve required
notices when redistributing, and perform a release-time dependency/license
review rather than assuming every bundled component shares the top-level
license.

Downloading a signed open-source runtime into an application-owned cache can be
covered by explicit first-use disclosure and project policy. Installing a
container engine, enabling cloud routing, transmitting source or prompts to a
hosted provider, accepting a model license, or enabling paid APIs requires
specific user consent.

Source: [LiteLLM license](https://github.com/BerriAI/litellm/blob/main/LICENSE)

## Recommendation for M3

Do not select LiteLLM as ProjectForge's first local provider.

Choose direct Ollama first because it has fewer moving parts, makes local
execution classification clearer, avoids a Python/container gateway between
ProjectForge and the model runtime, and exercises the provider and provisioning
contracts without also introducing multi-provider routing and secret
management.

Retain LiteLLM as the second provider boundary when one of these needs becomes
real:

- consistent access to several model vendors;
- centrally managed routing, fallback, budgets, or virtual keys;
- an operator already runs a trusted LiteLLM gateway; or
- ProjectForge needs one OpenAI-compatible adapter for both Ollama and approved
  hosted providers.

Before implementation, require a threat-model review, select and record one
verified immutable container digest, define local/cloud classification for
fallback routes, and add tests for readiness versus model health, secret
redaction, timeout layering, cancellation with ambiguous completion, retry
exhaustion, missing models, compromised/unsigned artifact rejection,
concurrent provisioning, and restart recovery.
