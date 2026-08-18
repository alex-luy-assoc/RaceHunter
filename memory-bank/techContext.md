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

Exact local build, test, migration, and deployment commands are unknown until the solution skeleton is created. Record them here when verified; do not invent commands.

## Open Technical Decisions

Choose the smallest option that preserves the golden-path demo and approved boundaries:

1. Standard Vertex AI endpoint supported by `Google.GenAI` versus an Enterprise Agent Platform endpoint.
2. Pub/Sub push delivery to the Cloud Run worker versus another supported consumption pattern.
3. Server-Sent Events versus SignalR for progress.
4. Web assets hosted by the API versus a separate supported host.
5. Low-friction authentication for the hosted judging demo.
6. Exact reference-target observation JSON paths.
7. Minimum replay success rate for a reproducible finding.
8. Whether any OpenAPI subset is worth MVP risk.
