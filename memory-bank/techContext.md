# RaceHunter Technical Context

## Approved Stack

- **Backend and worker:** C# with .NET 10
- **Web:** React with TypeScript
- **System of record:** PostgreSQL locally and Cloud SQL for PostgreSQL in Google Cloud
- **ORM:** EF Core with Npgsql and versioned migrations
- **AI:** Gemini 3.5 Flash or newer through Vertex AI
- **Google agent framework:** official Google Gen AI SDK for .NET (`Google.GenAI`)
- **Asynchronous boundary:** Google Cloud Pub/Sub
- **Compute:** separate API and worker Cloud Run processes built from one modular-monolith solution; reference target also deployable to Cloud Run
- **Secrets:** Google Secret Manager with persisted references only
- **Observability:** OpenTelemetry-compatible tracing, structured Cloud Logging, and Cloud Trace
- **Local infrastructure:** Dockerized PostgreSQL; Pub/Sub emulator or controlled adapter double
- **Tests:** xUnit-based unit, architecture, integration, concurrency, and acceptance suites; Testcontainers for PostgreSQL

Gemini 3.5 Flash model ID is `gemini-3.5-flash`. Model responses that affect execution must use a versioned structured-output schema and be validated before use.

## Deployable Components

### RaceHunter Web

React UI for objective entry, generated-plan review, live campaign progress, finding evidence, actor-lane trace, replay, and verify-fix comparison. It communicates only with the API.

### RaceHunter API

ASP.NET Core API that owns HTTP validation, authorization boundaries, commands and queries, persistence-facing application operations, run dispatch, cancellation, and progress streaming. It never runs a long campaign inside an HTTP request.

### RaceHunter Worker

.NET Worker Service that consumes idempotent run messages, owns run leases or heartbeats, invokes Gemini through application interfaces, executes schedules, evaluates invariants, minimizes failures, and persists progress incrementally.

### Reference Target API

Small ASP.NET Core inventory/order API with deliberately vulnerable and transactionally fixed modes, correlation IDs, and demo-only reset/seed controls. It has an independent PostgreSQL schema or database.

## Proposed Solution Structure

```text
src/
  RaceHunter.Domain/
  RaceHunter.Application/
  RaceHunter.Contracts/
  RaceHunter.Infrastructure/
  RaceHunter.Concurrency/
  RaceHunter.Gemini/
  RaceHunter.Api/
  RaceHunter.Worker/
  RaceHunter.ReferenceTarget/
  RaceHunter.Web/
tests/
  RaceHunter.Domain.Tests/
  RaceHunter.Application.Tests/
  RaceHunter.Concurrency.Tests/
  RaceHunter.Infrastructure.IntegrationTests/
  RaceHunter.Api.IntegrationTests/
  RaceHunter.ReferenceTarget.Tests/
  RaceHunter.Architecture.Tests/
  RaceHunter.AcceptanceTests/
deploy/
  cloud-run/
  database/
docs/
  architecture/
  decisions/
  demo/
```

## Core Domain and Durable State

Primary aggregates and records are Project, TargetSystem, Experiment, ScenarioDefinition, InvariantDefinition, ExperimentRun, RunAttempt, TraceEvent, Finding, ReplayArtifact, and AgentIteration. Inbox/idempotency records protect at-least-once delivery; an outbox is used only where a state transition and event publication must be atomic.

PostgreSQL is authoritative for lifecycle state and evidence. Flexible, sanitized payloads and versioned model output may use `jsonb`; relationships and query-critical fields remain normalized.

## Agent Interfaces

- `IScenarioPlanner.PlanAsync`
- `IExperimentStrategist.SelectNextAsync`
- `IFailureAnalyst.AnalyzeAsync`
- `IReproductionMinimizer.MinimizeAsync`

Gemini plans, prioritizes, adapts, and explains. Deterministic application code controls synchronization, budgets, validation, finding status, reduction acceptance, persistence, and replay.

## Concurrency Runtime

The runtime materializes actor plans, uses bounded channels for backpressure, composes global/target/experiment limiters, coordinates barriers and checkpoints, applies seeded strategies, and appends ordered evidence. Initial strategies are simultaneous start, fixed stagger, seeded jitter, bounded burst, and checkpoint interleaving.

External-target replay is best effort because server scheduling is outside RaceHunter. A replay records scenario/invariant versions, target snapshot, schedule strategy and seed, actor count, offsets, request templates, and setup. The reference target may expose controlled checkpoints to make the demo reliable.

## Initial HTTP Surface

The source brief proposes project, target, experiment, plan generation, scenario/invariant editing, run, cancellation, event/trace, finding, and replay resources under `/api`. Contracts are versioned, return RFC 9457 Problem Details for errors, use idempotency keys for run/replay creation, and cursor pagination for events and traces.

## Test Execution Strategy

- **Unit:** state transitions, budgets, seeded scheduling, evaluators, minimization, target validation, redaction, idempotency.
- **Integration:** repositories/migrations in Testcontainers, inbox/outbox, API boundaries, Pub/Sub adapter, Gemini serialization fixtures, target modes.
- **Concurrency:** limiter ceilings, backpressure, cancellation cleanup, barrier failure, trace ordering, seeded reproducibility, duplicate dispatch.
- **Architecture:** dependency directions, controller boundaries, provider-type leakage, external-client construction, and inline-SQL prohibition.
- **Acceptance:** reset one inventory unit, autonomously detect oversell, minimize to two actors, replay failure, switch to fixed mode, replay pass.
- **Cloud smoke:** deployed request, persistence, Pub/Sub dispatch, worker progress, Gemini evidence, Cloud Run health, and secret-safe logs.

Verified Phase 1 commands:

- `dotnet restore RaceHunter.slnx`
- `dotnet build RaceHunter.slnx --no-restore -c Release`
- `dotnet test RaceHunter.slnx --no-build -c Release`
- `npm ci --prefix src/RaceHunter.Web`
- `npm run lint --prefix src/RaceHunter.Web`
- `npm run build --prefix src/RaceHunter.Web`
- `docker compose config --quiet`
- `docker compose build`
- `docker compose up -d` followed by API, worker, and reference-target health plus API persistence and target reset smoke requests

The main and reference-target EF Core migrations apply automatically at host startup. Terraform is verified through the pinned `hashicorp/terraform:1.14.4` container without credentials or backends; Google Cloud apply and smoke remain separately approval-gated.

Verified Phase 3 capabilities and commands:

- `Google.GenAI` uses its Vertex AI client mode with `gemini-3.5-flash`; planning and strategy calls require versioned JSON schemas, one bounded repair, sanitized failures, and explicit model-call accounting.
- The API transactionally records plan/run intent in an outbox; Pub/Sub push work uses a versioned envelope, a database inbox, renewable leases, persisted attempt/decision checkpoints, classified retry delay, and durable dead-letter outcomes.
- Run-event JSON pagination and `text/event-stream` share the PostgreSQL cursor. The React client stores the last acknowledged event ID and reconnects after refresh without treating the browser as execution state.
- `dotnet test RaceHunter.slnx -c Release --no-restore` — 84 tests passed, including 25 focused Phase 3 tests.
- `npm run lint --prefix src/RaceHunter.Web` and `npm run build --prefix src/RaceHunter.Web`.
- `docker compose config --quiet`, three image builds, and a fresh-volume Pub/Sub-emulator plan/approve/run smoke journey.

Phase 3 compliance remediation raised the verified suite to 95 .NET tests plus 2 Vitest UI tests. `npm test --prefix src/RaceHunter.Web` verifies that Live Campaign loads current run state and every persisted 100-row event page before opening SSE after the latest database cursor. Lease takeover uses conditional PostgreSQL updates, retry ceilings come from persisted subject budgets, and duration recovery subtracts elapsed time from the original run start.

The second Phase 3 compliance remediation raised the verified suite to 97 .NET tests plus 2 Vitest UI tests. Agent decision/event/checkpoint persistence now conditionally updates the active lease owner, status, and expiry inside the same transaction before inserting decision evidence. Planning model-call usage, completed plans, and terminal planning outcomes are checkpointed across delivery attempts; each recovered `PlanningContext` receives only the remaining model-call budget.

The live Vertex invocation and deployed Pub/Sub/Cloud Run smoke remain approval-gated. Ordinary tests and local smoke use deterministic model fakes and the Pub/Sub emulator and require no Google credentials.

Verified Phase 4 capabilities and local gates:

- A reference finding is eligible only after the same deterministic invariant fails in exactly three of three measured reproductions. The reducer replays the recorded candidate schedule, tries actor removal deterministically down to the two-actor floor, then removes steps while accepting only reductions that preserve `Fail`.
- Replay artifacts contain version identifiers, target snapshot, strategy, seed, ordered actor steps, offsets, and the request template. JSON is canonicalized and UTC timestamps are normalized to PostgreSQL microsecond precision before the SHA-256 fingerprint is computed; every database rehydration and replay verifies that fingerprint.
- PostgreSQL stores findings, reproductions, replay artifacts, ordered steps, vulnerable/fixed attempts, and replay-execution claims in normalized records. Finding, artifact, and initial vulnerable attempt are committed atomically.
- Concurrent Verify Fix requests coordinate through a per-artifact database claim so only one fixed-target execution wins. The claim is committed before the private-worker HTTP call, no database transaction spans that call, the fixed attempt is committed afterward, and failed or stale claims can be recovered without rewriting the finding or artifact.
- `GET /api/findings/{findingId}` projects only trace rows referenced by the finding's deterministic evidence, preserves attempt IDs, orders events causally by UTC time and sequence into actor lanes, and returns Agent Activity and replay attempts separately. `POST /api/findings/{findingId}/replays` requires an idempotency key and returns the vulnerable/fixed comparison with RFC Problem Details on errors. The checked-in `docs/openapi.json` describes this Phase 4 API subset.
- The React `/findings/{findingId}` route renders the exact verified headline only for measured 3/3 failure and a two-actor artifact. Verify Fix retains the original evidence when the replay service is unavailable and displays vulnerable `Fail` versus fixed `Pass` only when both attempts carry the immutable artifact fingerprint.
- Playwright 1.62.1 covers the UI golden path from New Hunt through plan approval, live progress, the finding, and Verify Fix; it also covers API-backed refresh rehydration and recoverable replay failure. The fresh-volume Docker Compose golden path exercises the same plan/approve/run/finding/fixed-replay capability through PostgreSQL and the Pub/Sub emulator with the three approved application images.
- Recovery checkpoints persist every reproduction, minimization candidate, and proof outcome under a stable run/probe key. Reference-target reset/order results are idempotent across crash recovery, while an authenticated operation-status endpoint returns receiver correlations so the worker can reserve only trace-ledger-missing logical work before mutation.
- Target operation keys include artifact or candidate scope, actor, and stable step position/identity. Controlled-checkpoint recovery reuses receiver-side idempotent results with the same stable keys; vulnerable scheduling always reads live transactional state before the concurrent checkpoint and never fabricates state from actor identity or a reset snapshot.
- `playwright.real.config.ts` and `scripts/run-real-playwright.ps1` provide an automated non-mocked browser journey against isolated Compose PostgreSQL, Pub/Sub emulator, API, worker, and reference-target services.
- Phase 4 lifecycle remediation persists `Reproducing` and `Minimizing` status plus ordered `reproduction-started` and `minimization-started` events before their target work. Rehydrated runs treat those transitions monotonically, active-phase failure/cancellation remains legal, terminal state remains immutable, and Live Campaign projects the vocabulary from SSE while refresh reads the authoritative phase and ordered cursor history from PostgreSQL.
- The remediation verification baseline is 152 .NET tests, 8 Vitest tests, 4 mocked Playwright tests, and 1 fresh-volume real Docker-backed Playwright test. Formatting, TypeScript lint/build, Compose/OpenAPI validation, three image builds, and NuGet/npm vulnerability audits also pass.

Phase 5 retains Cloud Run private-service IAM and API-to-worker ID-token authentication, OpenTelemetry and judge-facing Cloud proof, hardening/performance checks, staging/deployment, and live Google Cloud smoke. No Phase 4 local gate requires Google credentials or billable resources.

Verified Phase 5 capabilities and local gates:

- Public hunt creation enforces the 10 actor / 10 concurrent / 40 request / 5 Gemini / 90 second / 1 retry sandbox ceiling at the API boundary. A fixed-time local/admin bearer check unlocks the bounded 100-logical-actor engine and the otherwise hidden manual-target configuration route.
- Manual targets persist only an authorization acknowledgement, exact HTTPS host and operations, Secret Manager version reference, and redaction paths. DNS validation rejects metadata, loopback, private, link-local, multicast, and mixed answers; the safe client disables automatic redirects and pins connections to a freshly validated public address. Authorization, cookies, API keys, demo-control keys, and configured JSON paths are redactable before evidence or model use.
- API, worker, and reference target emit JSON logs plus OpenTelemetry HTTP/runtime traces and metrics. Pub/Sub carries W3C `traceparent`; durable correlation IDs and spans cover work, run, attempt, actor, step, request, model invocation, finding, and replay. Metrics include queue delay, duplicate work, limiter occupancy/wait, target latency, model calls, invariant outcomes, findings, replays, and cancellation persistence latency. OTLP export is disabled unless an explicit endpoint is configured.
- Cloud Run IAM exposes only the API publicly. Pub/Sub and API invoke the private worker with exact-audience OIDC tokens; the worker invokes the private reference target the same way. Metadata identity tokens bypass proxies, are audience-bound, cached only until near expiry, and never attach to a different destination.
- Reference-target HTTP calls use a single 30-second bound so a private Cloud Run cold start fits within the 90-second campaign budget. A run-time `TaskCanceledException` with the `HttpClient` timeout shape and no caller cancellation is classified as retryable, idempotent target transport failure; the active run receives a retry event rather than `Failed`. Planning cancellation retains its own taxonomy, while genuine caller cancellation bypasses failure recording.
- Terraform keeps the approved three application images, Pub/Sub/DLQ, Cloud SQL, Secret Manager, workload identities, observability APIs, deletion protection, hard Cloud Run instance ceilings, and mandatory 50/90/100% budget alerts. `hashicorp/terraform:1.14.4` passed `fmt -check`, `init -backend=false`, and `validate` with no credentials or state backend.
- The final local baseline after Phase 5 compliance hardening is 211 .NET tests, 8 Vitest tests, 4 mocked Playwright tests, and two consecutive 2-test fresh-volume Docker-backed Playwright runs. The real journeys render owner-protected manual evidence from shared persisted state, reference 3/3 reproduction, two-actor minimization, vulnerable/fixed replay, refresh recovery, and local Cloud Proof.
- The deploy and staging-smoke scripts require separate explicit approval switches. Image inputs must be immutable digests, and deployed smoke enforces the under-four-minute finding/fix proof. No Terraform apply, Google Cloud API contact, credential use, remote, push, PR, or deployment occurred during Phase 5.

Verified staged-release Phase 1 capabilities and local gates:

- `deploy/scripts/StagingRelease.psm1` provides a provider-independent PowerShell release contract with exact stage and release-identity approvals, canonical binding hashes, monotonic durable local state, downstream invalidation on drift, and a fail-closed ambiguous-mutation recovery interlock.
- `deploy/scripts/staging-release.ps1` exposes local initialization, status, failure recording, reconciliation, and approval validation. External stage execution remains intentionally unavailable until its later implementation phase; the Phase 1 entry point performs no credential access or cloud operation.
- `deploy/scripts/staging-evidence.schema.json` and the module enforce the versioned environment-qualified evidence contract, exact properties and types, structured expected/observed summaries, secret-material rejection, and atomic manifest promotion. Raw state remains under the gitignored `memory-bank/.local/staging-release/` boundary.
- After deployment validation, one exact `ReleaseCompletion` approval may cover one 210-second application smoke and, only after it succeeds, one separate fresh browser demo below 240 seconds. `deploy/scripts/release-completion.ps1` preserves distinct smoke/demo evidence and durable hunt/run/finding IDs; interruption may resume only that same application run, while an ambiguous ID-less mutation forbids a second new run. This consolidation grants no Terraform, image, IAM/API, token, secret-read, cleanup, deletion, or destruction authority.
- If a completion attempt durably captures a hunt before failure, `RecoveryCompletion` may reset the bounded deadline only when its fresh exact request binds the source progress bytes, existing hunt ID, and existing plan version. Recovery cannot call `POST /api/hunts`; optional HTTP response properties are normalized before status inspection.
- Verification passed 14 focused staging-release contract tests and 241 tests across the full .NET suite, plus .NET and web builds and web lint. No package or technology dependency was added, and no Google API call, billable mutation, image publication, Terraform apply, deployment, smoke, demo, cleanup, or destruction occurred.

Verified staged-release Phase 2 capabilities and local gates:

- Terraform now has two independently initialized roots. `deploy/terraform/bootstrap` enables the exact application, IAM, telemetry, registry, budget, storage, and database APIs and owns a private GCS state bucket with uniform access, public-access prevention, versioning, retention, and destroy prevention, plus an Artifact Registry repository with immutable tags and destroy prevention. `deploy/terraform` uses an empty GCS backend configured only after the approved foundation boundary.
- `New-StagingBackendMigrationPlan` returns a credential-free descriptor whose initial backend is local, whose remote backend is GCS, and whose actions are explicitly non-executing. It requires the operator to copy `backend.gcs.tf.example` to the gitignored `backend.gcs.tf` before using its exact bootstrap `init -migrate-state` and application `init -reconfigure` arguments.
- Foundation approval binds the exact required API set, state protections, immutable registry, billing account, mandatory USD budget, API/worker/target scale ceilings, and deletion protection. Application planning accepts exactly the reviewed Terraform input schema and three repository-qualified application digests, materializes a gitignored canonical `.tfvars.json`, verifies its SHA-256 against the binding, and records the saved-plan hash. Deployment accepts only the same saved-plan bytes and never regenerates a plan.
- The primary service and reference target use separate Cloud SQL instances, databases, users, random passwords, and Secret Manager connection references. Every generated-secret mount and environment reference is pinned to the concrete `google_secret_manager_secret_version` output, which makes secret creation an explicit template input and forces a fresh Cloud Run revision when the version changes. Four keyless service accounts receive only their required Cloud SQL, Vertex AI, telemetry, per-secret accessor, Pub/Sub, and Cloud Run invoker roles. API→worker and Pub/Sub→worker bind to the exact worker URL audience; worker→target binds to the exact target URL audience; only the API has `allUsers` invocation.
- Cloud Run retains internal `/healthz` startup probes, but external validation and smoke avoid paths ending in `z` because Cloud Run reserves some such paths at its external edge. External reachability uses API `GET /api/capabilities`; private-service IAM denial uses worker `GET /internal/replays` and reference-target `GET /api/inventory`.
- Staging polling treats fields from eventually consistent JSON responses as optional under PowerShell strict mode. `Get-StagingPropertyValue` normalizes missing dictionary and object properties so an early hunt-plan response without `planVersion` remains a not-ready observation rather than terminating the approved application run.
- Reference inventory attempts now finish with one budgeted `GET /api/inventory` observation. The deterministic evaluator removes transaction-local successful-order/capacity pairs and evaluates the single global successful-orders versus known capacity pair, so concurrent responses that each observed `1 <= 1` cannot hide a final global oversell. Campaign and replay/minimization admission reserve this physical snapshot request, while manual targets retain their configured observation behavior.
- Once a deterministic attempt has failed, later request/model/iteration exhaustion returns `VerifiedViolation` with the exact failed settings and schedule instead of discarding verified evidence as `BudgetExhausted`.
- Verification passed 12/12 focused Phase 2 tests, 38/38 architecture tests, 245/245 tests across the full .NET suite, 8/8 web tests, .NET/web builds, web lint, and pinned `hashicorp/terraform:1.14.4` formatting, bootstrap/application initialization without backends, and validation. This is local contract evidence, not deployed proof. The phase used no credentials and made no Google API calls, image publications, state migrations, Terraform plans or applies, deployments, staging smoke or demo runs, cleanup, or destruction.
- Collector-image digest resolution remains deferred until explicit network authorization. Before Phase 4 materializes the ignored `deploy/terraform/bootstrap/backend.gcs.tf`, make the contract test for its absence independent of the real worktree so a legitimate generated operator file cannot fail the local architecture suite.

Verified staged-release Phase 3 capabilities and local gates:

- `QualifyLocal` is the executable credential-free release-candidate boundary. It rejects a dirty checkout or a `HEAD` mismatch before expensive work, then runs the fixed .NET, Vitest, lint, web-build, fresh-volume real Playwright, three-image build, Compose, dependency-audit, repository-secret-scan, and pinned Terraform gate set in deterministic order.
- Qualification subprocesses are started with `ProcessStartInfo.ArgumentList` under a minimal allowlisted environment. Google and Cloud SDK credential/token variables are absent, while `HOME`, `USERPROFILE`, Cloud SDK, Docker, NuGet, and npm discovery/cache roots are isolated under the gitignored release-state directory so ambient workstation credentials cannot cross the local-only boundary.
- Successful qualification stores only schema-validated `local` and `local-emulated` evidence plus an exact qualification hash. State and evidence are replaced atomically, and saving the same qualification again resumes without duplicating evidence or transitions.
- The generated default-deny `Preflight` request binds schema, commit, project, region, binding hash, qualification hash, allowed read-only checks, request time, and its canonical request hash. Approval creation and validation recompute that hash and reject tampering, pre-qualification issuance, mismatches, more than two minutes of future skew, or approval age beyond 15 minutes.
- After the Phase 3 candidate is committed and the checkout is clean, the operator sequence is `$commitSha = git rev-parse HEAD` followed by `pwsh -NoLogo -NoProfile -NonInteractive -File .\deploy\scripts\staging-release.ps1 -Stage QualifyLocal -ProjectId '<non-production-project>' -Region '<region>' -CommitSha $commitSha`. The operator stops at the emitted request; it does not authorize credential use or any external stage.
- Verification passed 7/7 focused Phase 3 contracts, 45/45 architecture tests, and 260/260 tests across 11 suites, with .NET/web builds and web lint passing. No dependency changed, and no credentials, authenticated Google APIs, billable mutation, image publication, state migration, Terraform plan/apply, deployment, staging smoke/demo, cleanup, or destruction were used.

## Open Technical Decisions

Choose the smallest option that preserves the golden-path demo and approved boundaries:

1. **Resolved:** standard Vertex AI endpoint through `Google.GenAI`; no Enterprise Agent Platform dependency in the MVP.
2. **Resolved:** authenticated Pub/Sub push delivery to the private Cloud Run worker, with emulator mode locally.
3. **Resolved:** Server-Sent Events with a durable PostgreSQL cursor and reconnect support.
4. **Resolved:** React assets are hosted by the public API image.
5. Low-friction authentication for the hosted judging demo.
6. Exact reference-target observation JSON paths.
7. **Resolved by target class:** the reference target requires exactly three failures in three attempts; an authorized external target requires at least two failures in three equivalent attempts before two-actor minimization and immutable-snapshot replay.
8. **Resolved:** maintain a small checked-in OpenAPI 3.1 subset for durable run status, finding evidence, and Verify Fix.
