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

The main and reference-target EF Core migrations apply automatically at host startup. Terraform commands remain unverified because Terraform is not installed, and Google Cloud apply/smoke remains approval-gated.

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
- Target operation keys include artifact or candidate scope, actor, and stable step position/identity. Controlled-checkpoint recovery uses the reset snapshot only for checkpointed vulnerable operations; ordinary sequential and jittered schedules continue to read live state.
- `playwright.real.config.ts` and `scripts/run-real-playwright.ps1` provide an automated non-mocked browser journey against isolated Compose PostgreSQL, Pub/Sub emulator, API, worker, and reference-target services.

Phase 5 retains Cloud Run private-service IAM and API-to-worker ID-token authentication, OpenTelemetry and judge-facing Cloud proof, hardening/performance checks, staging/deployment, and live Google Cloud smoke. No Phase 4 local gate requires Google credentials or billable resources.

## Open Technical Decisions

Choose the smallest option that preserves the golden-path demo and approved boundaries:

1. **Resolved:** standard Vertex AI endpoint through `Google.GenAI`; no Enterprise Agent Platform dependency in the MVP.
2. **Resolved:** authenticated Pub/Sub push delivery to the private Cloud Run worker, with emulator mode locally.
3. **Resolved:** Server-Sent Events with a durable PostgreSQL cursor and reconnect support.
4. **Resolved:** React assets are hosted by the public API image.
5. Low-friction authentication for the hosted judging demo.
6. Exact reference-target observation JSON paths.
7. **Resolved for the reference target:** exactly three failures in three equivalent attempts are required before minimization; external-target confidence remains governed by the product brief.
8. **Resolved:** maintain a small checked-in OpenAPI 3.1 subset for durable run status, finding evidence, and Verify Fix.
