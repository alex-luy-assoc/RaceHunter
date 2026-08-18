---
slug: build-racehunter-hackathon-mvp
feature: racehunter-autonomous-concurrency-campaign
status: COMPLETE
---

# build-racehunter-hackathon-mvp: Build the RaceHunter Hackathon MVP

**Complexity**: Level 4
**Status**: COMPLETE
**Roadmap**: racehunter-autonomous-concurrency-campaign
**Branch**: feature/build-racehunter-hackathon-mvp
**Worktree**: C:\Users\alexa\source\repos\RaceHunter

## Task Description

Build RaceHunter as a complete Taskmaster workflow for the All Things Agentic Hackathon. A backend developer starts a New Hunt from a quota-limited inventory sandbox or an authenticated manually configured HTTP/JSON target, states a transactional correctness rule, reviews a Gemini-generated structured plan once, and selects **Approve & Run**. A private .NET execution worker then completes an unattended bounded campaign through authenticated Pub/Sub delivery: it executes controlled schedules, evaluates machine-readable invariants in deterministic code, asks Gemini 3.5 Flash to choose the next allowlisted strategy from summarized evidence, persists every decision and checkpoint, minimizes any verified violation, and creates an immutable replay artifact.

The golden path must prove overselling against a deliberately vulnerable reference target, reproduce it three out of three times, minimize it to two actors, and replay the same artifact successfully against the vulnerable mode and without violation against the transactionally fixed mode. The application must expose causal evidence and Google Cloud execution proof clearly enough to complete an unedited judge demo in less than four minutes.

The approved design uses a modular monolith with Clean Architecture boundaries and three Docker images: a public ASP.NET Core API that serves the React application, a private ASP.NET Core worker invoked by Pub/Sub, and a private reference target. PostgreSQL is the system of record locally and through Cloud SQL in staging. Gemini is accessed through the official Google Gen AI .NET SDK and Vertex AI with workload identity. Docker Compose supplies local portability, while Terraform deploys the same images to Cloud Run with Pub/Sub, Cloud SQL, Secret Manager, IAM, logging, tracing, and cost controls.

Required principles are evidence before explanation, bounded autonomy, safe targets, measured reproducibility, model-independent deterministic execution, schema-constrained model output, durable progress, explicit failure outcomes, and demo reliability as a product requirement. The user's lived connection is recurring transactional-risk work rather than a fabricated incident.

## Specification

**Feature Type**: End-User Feature

**Primary Persona**: Backend/platform engineer responsible for correctness of concurrent transactional APIs

### Invocation Method

- **UI entry**: Open `/hunts/new` from the dashboard, choose **Inventory Sandbox** or an authorized manual HTTP/JSON target, enter the business rule and bounded budgets, select **Generate Plan**, review the versioned Gemini proposal, and select **Approve & Run** once.
- **API entry**: `POST /api/hunts` creates the draft; `POST /api/hunts/{huntId}/plan` requests asynchronous planning; `GET /api/hunts/{huntId}/plan` reads the validated proposal; `POST /api/hunts/{huntId}/runs` approves a specific plan version and queues execution. Run state is read with `GET /api/runs/{runId}` and streamed with `GET /api/runs/{runId}/events?after={cursor}`. `POST /api/runs/{runId}/cancel` requests cancellation. Findings are read at `GET /api/findings/{findingId}` and replayed with `POST /api/findings/{findingId}/replays`.
- **Availability**: The public judge sandbox exposes only the included inventory target. Manual target setup is available only to authenticated local/admin users and requires explicit authorization acknowledgement, an allowlisted host, and a Secret Manager reference rather than a secret value.
- **Execution boundary**: Planning and campaigns return `202 Accepted`; the API persists intent and publishes idempotent Pub/Sub work. The private .NET worker performs Gemini calls, target calls, invariant evaluation, reproduction, minimization, and replay without an open browser or long-lived API request.

### Success Criteria

- The reference journey reaches `/findings/{findingId}` and displays exactly `Race condition verified — reproduced 3/3 and minimized to 2 actors.`
- The Finding page distinguishes deterministic invariant evidence from Gemini interpretation and exposes the invariant result, sanitized request/response references, actor-lane timeline, schedule seed and offsets, three reproduction outcomes, minimized actor/step set, immutable replay ID, and Agent Activity decisions.
- **Verify Fix** executes the same immutable replay artifact against fixed target mode and displays vulnerable **failed invariant** versus fixed **passed invariant** results without rewriting the original finding or artifact.
- PostgreSQL is authoritative. The workflow persists `projects`, `target_systems`, `experiments`, `scenario_definitions`, `invariant_definitions`, `experiment_runs`, `run_attempts`, `run_events`, `trace_events`, `agent_iterations`, `findings`, `replay_artifacts`, `replay_attempts`, `work_inbox`, and `outbox_messages`. Every scenario, invariant, finding, and replay references immutable version identifiers; evidence rows carry UTC timestamps and run, attempt, actor, step, request, and model-invocation correlations where applicable.
- Refreshing or navigating away reloads the latest lifecycle state and paged evidence from PostgreSQL, then resumes SSE after the last acknowledged event cursor. Completion does not depend on a connected UI.
- The public inventory campaign completes inside its enforced limits of 10 actors, 40 target requests, 5 Gemini calls, and 90 seconds. The authenticated engine demonstrates support for 100 logical actors while respecting configured global, target, and experiment concurrency ceilings.
- The same application images complete the golden path in Docker Compose and on Google Cloud. The judge-facing Cloud Proof panel identifies the running Cloud Run revision, worker-dispatched Pub/Sub run, Gemini model/schema version, and persisted Cloud SQL-backed run without exposing credentials.

### Acceptance Criteria

#### AC-ENTRY-1: Public sandbox New Hunt is discoverable

**Priority**: MUST

**Given** a visitor opens the RaceHunter dashboard in the public judge sandbox  
**When** they select **New Hunt**  
**Then** `/hunts/new` presents the Inventory Sandbox, the oversell business-rule input, visible actor/request/model/duration budgets, and **Generate Plan**, while manual target configuration is absent.

#### AC-ENTRY-2: Manual target entry is restricted and secret-safe

**Priority**: MUST

**Given** an authenticated local/admin user has acknowledged ownership of a target  
**When** they configure a manual HTTP/JSON target  
**Then** RaceHunter accepts only an HTTPS allowlisted host, endpoint/request templates, observation paths, and a credential reference, and never accepts or persists a raw credential value.

#### AC-HAPPY-1: Gemini generates a validated plan asynchronously

**Priority**: MUST

**Given** a valid New Hunt draft for the inventory sandbox  
**When** the user selects **Generate Plan**  
**Then** `POST /api/hunts/{huntId}/plan` returns `202 Accepted`, Pub/Sub dispatches one `PlanRequested` work item, and the private worker invokes the configured real `gemini-3.5-flash` Vertex AI model through `Google.GenAI`, validates the versioned structured output against the allowed target operations, strategies, budgets, and invariant types, persists the proposal, and emits a `PlanReady` event.

#### AC-HAPPY-2: One approval queues one autonomous run

**Priority**: MUST

**Given** the validated proposal is visible on Plan Review  
**When** the user selects **Approve & Run** for its exact plan version  
**Then** `POST /api/hunts/{huntId}/runs` creates one `Queued` run with an idempotency key, records the approval, publishes `RunRequested`, and requires no further user decision for that campaign.

#### AC-HAPPY-3: The worker executes a bounded evidence-driven campaign

**Priority**: MUST

**Given** an approved reference-target run with one inventory unit  
**When** the worker executes the bounded plan-execute-observe-adapt loop  
**Then** deterministic code schedules target calls, appends sanitized evidence, evaluates numeric-boundary, uniqueness/cardinality, or cross-observation invariants as pass, fail, or inconclusive, and allows Gemini to select only actor-count changes, allowlisted strategies, bounded timing adjustments, repetition, minimization, or stop; server-side validation and budgets override every model choice.

#### AC-HAPPY-4: Deterministic evidence verifies and minimizes the race

**Priority**: MUST

**Given** an attempt produces evidence that successful orders exceed available inventory  
**When** the numeric-boundary evaluator returns fail  
**Then** RaceHunter creates a verified finding from the evaluator result and trace references only, reproduces the same invariant violation three of three times on the reference target, and accepts each actor, step, or timing reduction only when deterministic replay preserves that same violation.

#### AC-HAPPY-5: The finding presents exact reproducible proof

**Priority**: MUST

**Given** the reference finding reproduces three of three times and verified delta reduction reaches two actors  
**When** minimization completes  
**Then** `/findings/{findingId}` displays exactly `Race condition verified — reproduced 3/3 and minimized to 2 actors.` with the original evidence, minimized schedule, immutable replay artifact, replay outcomes, and causal actor-lane timeline.

#### AC-HAPPY-6: Verify Fix replays the immutable artifact

**Priority**: MUST

**Given** a completed reference finding and its immutable replay artifact  
**When** the user selects **Verify Fix**  
**Then** RaceHunter runs that artifact unchanged against fixed target mode, persists a separate replay attempt, and shows a side-by-side vulnerable invariant failure and fixed invariant pass; it does not alter the finding, schedule seed, actor steps, request templates, or original evidence.

#### AC-HAPPY-7: Identical images prove local and cloud operation

**Priority**: SHOULD

**Given** the documented local and staging prerequisites are present  
**When** the golden-path smoke journey runs first through Docker Compose and then against the Terraform-provisioned staging environment  
**Then** the API, worker, reference target, PostgreSQL/Pub/Sub dependencies, Gemini planning, finding, and fix replay complete with the same application images, and the Cloud Proof panel displays verifiable Cloud Run, Pub/Sub, Vertex AI, and Cloud SQL identifiers with secrets redacted.

#### AC-ASYNC-1: Persisted progress survives navigation and reconnects

**Priority**: MUST

**Given** planning, running, reproducing, minimizing, or replaying is in progress  
**When** the user watches Live Campaign, refreshes the page, closes it, or reconnects with an `after` cursor  
**Then** the worker continues independently, each legal lifecycle transition and Agent Activity decision is persisted before publication, and the UI reconstructs ordered progress without duplicating or losing acknowledged events.

#### AC-ASYNC-2: Duplicate delivery and lease recovery remain idempotent

**Priority**: MUST

**Given** Pub/Sub delivers the same work message more than once or a worker loses its lease  
**When** another worker receives the message  
**Then** the database-backed inbox and uniqueness constraints preserve one logical execution, an active lease prevents concurrent ownership, and an expired lease resumes only from the latest persisted attempt boundary without changing an immutable terminal state.

#### AC-ASYNC-3: Cancellation begins promptly and preserves evidence

**Priority**: MUST

**Given** a non-terminal run is active  
**When** the user requests cancellation through the UI or `POST /api/runs/{runId}/cancel`  
**Then** the request is persisted and visible immediately, worker cancellation begins within two seconds, no new target work starts after cancellation is observed, in-flight evidence is retained, and the run reaches `Cancelled` rather than a verified finding.

#### AC-ERROR-1: Invalid Gemini output cannot determine findings

**Priority**: MUST

**Given** Gemini returns output that fails schema or allowlist validation  
**When** one constrained repair attempt also fails  
**Then** planning terminates in an explicit model failure with RFC 9457 Problem Details and sanitized diagnostics; during a campaign, a documented deterministic fallback may continue within budget, but model output or fallback behavior can never create, remove, or change a finding.

#### AC-ERROR-2: Unsafe targets and sensitive data are blocked

**Priority**: MUST

**Given** a target destination, redirect, request, or response violates authorization, SSRF, redaction, or public-sandbox policy  
**When** RaceHunter validates or executes it  
**Then** the unsafe network call is blocked, a categorized safety/authorization failure is persisted and shown, credentials and configured sensitive JSON paths are absent from logs, traces, evidence, and Gemini input, and the event is auditable.

#### AC-ERROR-3: Server-side budgets stop excess work

**Priority**: MUST

**Given** a campaign reaches an actor, concurrency, request, duration, retry, Gemini-call, or host budget  
**When** additional work is proposed by the model or scheduler  
**Then** RaceHunter rejects the work server-side, stops or completes with an explicit budget-exhausted outcome, preserves collected evidence, and never labels an inconclusive result as a defect.

#### AC-ERROR-4: Retries and poison work fail safely

**Priority**: SHOULD

**Given** a classified transient worker, target, model, or persistence failure occurs  
**When** the retry policy evaluates it  
**Then** only safe/idempotent operations receive bounded backoff with jitter, unsafe target mutations are not automatically retried, exhausted poison work reaches a dead-letter subscription, and the failed state plus recovery guidance remains visible after refresh.

### Scope Boundaries

- **Included**: React New Hunt, Plan Review, Live Campaign, and Finding & Replay experiences; inventory reference target in vulnerable/fixed modes; authenticated manual HTTP/JSON target configuration; numeric-boundary, uniqueness/cardinality, and cross-observation invariants; simultaneous-start, seeded-jitter, and checkpoint-interleaving strategies; Gemini structured planning and bounded strategy selection; deterministic evidence, reproduction, minimization, replay, cancellation, recovery, Docker Compose, Terraform staging, and judge-facing cloud proof.
- **Public sandbox**: Reference target only, with hard ceilings of 10 actors, 40 target requests, 5 Gemini calls, and 90 seconds per campaign.
- **Authenticated engine**: Up to 100 logical actors, still subject to configured global, target, experiment, request, duration, retry, model, and host ceilings.
- **Excluded from the critical path**: OpenAPI import, arbitrary public targets, browser concurrency, source-code race detection, packet capture, direct target-database access, exhaustive formal verification, non-HTTP protocols, Kubernetes/multi-region deployment, multi-tenancy/billing, CI pull-request comments, a scenario marketplace, additional invariant families, and additional Google AI models.
- **Truth boundary**: Only deterministic invariant evaluation can verify a finding. Reproducibility is measured, not promised; external targets receive a reproducible label after at least two failures in three equivalent replay attempts, while the reference demo must reproduce three of three before final minimization.

### Creative Exploration Needed

No. Architecture, UI/UX, algorithm, safety, and end-to-end journey decisions were approved during brainstorm and are recorded in `memory-bank/creative/build-racehunter-hackathon-mvp-design.md`.

## User Journey Definition

**Feature Type**: End-User Feature
**Creative Phase Required**: Yes - Architecture, User Journey, UI/UX, and Algorithm decisions completed through the approved brainstorm

### Invocation Method (End-User Features)
- **Location**: `/hunts/new`, the New Hunt screen reached from the RaceHunter dashboard
- **Element**: the primary **New Hunt** entry point, followed by the **Generate Plan** and **Approve & Run** controls
- **Visibility**: New Hunt is always visible; manual external-target configuration is visible only in authenticated local/admin mode
- **Navigation**: Dashboard → New Hunt → choose Inventory Sandbox or authorized manual target → enter business rule and budgets → Generate Plan → Plan Review → Approve & Run

### Success Criteria (End-User Features)
- **User sees**: `Race condition verified — reproduced 3/3 and minimized to 2 actors.`
- **User can verify at**: `/findings/{findingId}`, through invariant evidence, an actor-lane timeline, replay ID, agent decision history, and vulnerable-versus-fixed comparison
- **Data persisted**: projects, targets, experiments, scenario and invariant versions, runs, attempts, trace events, agent iterations, findings, dispatch/inbox records, and replay artifacts in normalized PostgreSQL tables
- **Observable within**: plan readiness through SSE after asynchronous Gemini planning; campaign completion within the public sandbox's 90-second limit

### NFR Verification (Infrastructure Features)
- **Test method**: `dotnet test`, Playwright end-to-end tests, Docker Compose smoke journey, Terraform validation, and deployed Google Cloud smoke tests
- **Success metrics**: 100 logical actors supported with configured ceilings never exceeded; cancellation begins within two seconds; duplicate delivery creates one logical execution; reference replay succeeds 3/3; demo completes in under four minutes
- **Observable at**: the Finding page, Agent Activity panel, Cloud Run revisions/logs, Vertex AI invocation evidence, Cloud SQL state, and OpenTelemetry traces

### Acceptance Criteria
- AC-ENTRY-1: New Hunt is discoverable from the dashboard
- AC-HAPPY-1: the complete inventory oversell hunt reaches the exact verified-finding message
- AC-ERROR-1: invalid model output, target failures, safety rejection, and exhausted budgets become explicit recoverable or terminal states
- AC-ASYNC-1: planning, running, reproducing, minimizing, replaying, completed, failed, and cancelled states remain observable after navigation or refresh

## Test Strategy

### Approach
- **Emphasis**: balanced unit, integration, concurrency, architecture, E2E, Docker, and deployed-cloud verification
- **Target test count**: approximately 100 focused tests across five phases; the count exceeds the multi-component guideline because the feature includes multiple deployables, deterministic concurrency guarantees, security boundaries, asynchronous recovery, and a judge-critical end-to-end journey
- Use fixed clocks, IDs, seeds, controlled target checkpoints, and deterministic Gemini fakes for ordinary tests; keep a separate opt-in live Vertex AI integration smoke test.
- Every acceptance criterion must map to at least one automated test, with the complete golden path exercised locally in Docker and against staging.

### File Organization
- **New test files**: the pinned test projects and files listed under New Source Files, organized by domain, application, concurrency, infrastructure, API, reference target, architecture, and acceptance responsibility
- **Extend existing**: none; this is a greenfield solution

### What NOT to Test
- .NET, React, EF Core, Npgsql, Docker, Pub/Sub, or Cloud Run framework internals — verify RaceHunter's adapter contracts and observed integration behavior instead
- Gemini's hidden reasoning or general intelligence — verify schema validity, input specificity, allowlisted actions, fallback behavior, and real invocation evidence
- Exhaustive server-side interleavings — RaceHunter explores bounded schedules and reports replay confidence rather than claiming formal verification
- Arbitrary public targets — security policy restricts automated acceptance tests to the reference target and controlled fixtures
- Pixel-perfect visual styling in unit tests — cover accessibility, state, navigation, and critical layouts with Playwright and visual review

### Per-Phase Test Guidance
- Phase 1: 15–20 tests for architecture boundaries, target vulnerable/fixed behavior, migrations, health checks, Docker startup, and Cloud Run smoke deployment
- Phase 2: 25–30 tests for budgets, barriers, limiters, schedule seeds, trace ordering, three invariant families, cancellation, and manual hunt execution
- Phase 3: 20–25 tests for Gemini schemas, input specificity, one-time approval, agent action validation, Pub/Sub idempotency, leases, checkpoints, retries, dead-letter behavior, and SSE refresh recovery
- Phase 4: 20–25 tests for reproduction thresholds, deterministic reduction, immutable replay, causal timeline projections, vulnerable/fixed comparison, and the full Playwright golden path
- Phase 5: 10–15 tests/checks for SSRF, redaction, IAM denial, secret scanning, 100-actor ceilings, Docker images, Terraform, deployed smoke behavior, and demo timing

## Implementation Roadmap

### New Source Files (pin path + extension)
- [x] `global.json` — pin .NET SDK 10.0.400 with roll-forward policy
- [x] `RaceHunter.slnx` — .NET solution
- [x] `Directory.Build.props` — nullable, warnings-as-errors, analyzers, deterministic build
- [x] `Directory.Packages.props` — central NuGet versions
- [x] `.editorconfig` — shared formatting and analyzer rules
- [x] `.dockerignore` — exclude secrets, Memory Bank, tests, and development outputs from runtime images
- [x] `src/RaceHunter.Domain/RaceHunter.Domain.csproj` — domain project
- [x] `src/RaceHunter.Domain/Common/DomainException.cs` — domain failure base
- [x] `src/RaceHunter.Domain/Projects/Project.cs` — project aggregate
- [ ] `src/RaceHunter.Domain/Targets/TargetSystem.cs` — authorized target aggregate
- [ ] `src/RaceHunter.Domain/Experiments/Experiment.cs` — experiment aggregate
- [ ] `src/RaceHunter.Domain/Scenarios/ScenarioDefinition.cs` — versioned actors and steps
- [x] `src/RaceHunter.Domain/Invariants/InvariantDefinition.cs` — versioned invariant contract
- [x] `src/RaceHunter.Domain/Runs/ExperimentRun.cs` — campaign lifecycle aggregate
- [x] `src/RaceHunter.Domain/Runs/RunAttempt.cs` — schedule attempt entity
- [x] `src/RaceHunter.Domain/Tracing/TraceEvent.cs` — append-only evidence entity
- [x] `src/RaceHunter.Domain/Findings/Finding.cs` — verified finding aggregate
- [x] `src/RaceHunter.Domain/Replays/ReplayArtifact.cs` — immutable replay aggregate
- [ ] `src/RaceHunter.Domain/Agents/AgentIteration.cs` — persisted agent decision record
- [x] `src/RaceHunter.Domain/Budgets/ExperimentBudget.cs` — bounded campaign value object
- [x] `src/RaceHunter.Application/RaceHunter.Application.csproj` — use-case project
- [x] `src/RaceHunter.Application/Abstractions/Persistence.cs` — aggregate repositories and unit of work
- [x] `src/RaceHunter.Application/Abstractions/Agents.cs` — planner, strategist, analyst, and minimizer interfaces
- [ ] `src/RaceHunter.Application/Abstractions/Execution.cs` — scheduler, target client, queue, clock, and randomness interfaces
- [ ] `src/RaceHunter.Application/Hunts/CreateHunt.cs` — create hunt use case
- [ ] `src/RaceHunter.Application/Hunts/GeneratePlan.cs` — asynchronous planning use case
- [x] `src/RaceHunter.Application/Hunts/ApproveAndRun.cs` — one-time approval use case
- [x] `src/RaceHunter.Application/Runs/GetRun.cs` — durable run projection
- [x] `src/RaceHunter.Application/Runs/CancelRun.cs` — idempotent cancellation
- [x] `src/RaceHunter.Application/Findings/GetFinding.cs` — evidence projection
- [x] `src/RaceHunter.Application/Replays/VerifyFix.cs` — replay comparison use case
- [x] `src/RaceHunter.Contracts/RaceHunter.Contracts.csproj` — boundary contracts
- [x] `src/RaceHunter.Contracts/HuntContracts.cs` — hunt and plan DTOs
- [x] `src/RaceHunter.Contracts/RunContracts.cs` — run and progress DTOs
- [x] `src/RaceHunter.Contracts/FindingContracts.cs` — finding and timeline DTOs
- [x] `src/RaceHunter.Contracts/ReplayContracts.cs` — replay DTOs
- [x] `src/RaceHunter.Contracts/WorkMessage.cs` — versioned Pub/Sub envelope
- [x] `src/RaceHunter.Infrastructure/RaceHunter.Infrastructure.csproj` — persistence and cloud adapters
- [x] `src/RaceHunter.Infrastructure/Persistence/RaceHunterDbContext.cs` — EF Core context
- [x] `src/RaceHunter.Infrastructure/Persistence/EntityConfigurations.cs` — strongly typed mappings
- [x] `src/RaceHunter.Infrastructure/Persistence/Repositories.cs` — aggregate-specific repositories
- [x] `src/RaceHunter.Infrastructure/Persistence/Migrations/InitialCreate.cs` — initial migration
- [ ] `src/RaceHunter.Infrastructure/Persistence/Migrations/InitialCreate.Designer.cs` — migration metadata
- [x] `src/RaceHunter.Infrastructure/Persistence/Migrations/RaceHunterDbContextModelSnapshot.cs` — EF model snapshot
- [x] `src/RaceHunter.Infrastructure/Messaging/PubSubWorkPublisher.cs` — dispatch adapter
- [ ] `src/RaceHunter.Infrastructure/Messaging/InboxStore.cs` — duplicate-delivery guard
- [ ] `src/RaceHunter.Infrastructure/Targets/SafeTargetClientFactory.cs` — allowlisted HTTP clients
- [ ] `src/RaceHunter.Infrastructure/Targets/TargetDestinationValidator.cs` — SSRF and redirect defense
- [ ] `src/RaceHunter.Infrastructure/Secrets/GoogleSecretProvider.cs` — Secret Manager adapter
- [ ] `src/RaceHunter.Infrastructure/Observability/TelemetryRegistration.cs` — logs, metrics, and traces
- [x] `src/RaceHunter.Concurrency/RaceHunter.Concurrency.csproj` — deterministic execution project
- [x] `src/RaceHunter.Concurrency/Scheduling/ConcurrencyScheduler.cs` — bounded actor runtime
- [x] `src/RaceHunter.Concurrency/Scheduling/SchedulePlan.cs` — immutable schedule model
- [x] `src/RaceHunter.Concurrency/Scheduling/SimultaneousStartStrategy.cs` — barrier strategy
- [x] `src/RaceHunter.Concurrency/Scheduling/SeededJitterStrategy.cs` — seeded offsets
- [x] `src/RaceHunter.Concurrency/Scheduling/CheckpointStrategy.cs` — controlled interleaving
- [x] `src/RaceHunter.Concurrency/Tracing/TraceCollector.cs` — ordered evidence collection
- [x] `src/RaceHunter.Concurrency/Invariants/InvariantEvaluatorRegistry.cs` — evaluator dispatch
- [x] `src/RaceHunter.Concurrency/Invariants/NumericBoundaryEvaluator.cs` — numeric boundary evaluator
- [x] `src/RaceHunter.Concurrency/Invariants/CardinalityEvaluator.cs` — uniqueness/cardinality evaluator
- [x] `src/RaceHunter.Concurrency/Invariants/CrossObservationEvaluator.cs` — response/state relationship evaluator
- [x] `src/RaceHunter.Concurrency/Minimization/FailureMinimizer.cs` — verified delta reduction
- [x] `src/RaceHunter.Concurrency/Replay/ReplayExecutor.cs` — immutable replay execution
- [x] `src/RaceHunter.Gemini/RaceHunter.Gemini.csproj` — Gemini adapter project
- [x] `src/RaceHunter.Gemini/GeminiClient.cs` — Google Gen AI SDK wrapper
- [x] `src/RaceHunter.Gemini/ScenarioPlanner.cs` — structured initial planner
- [x] `src/RaceHunter.Gemini/ExperimentStrategist.cs` — allowlisted next-action selector
- [ ] `src/RaceHunter.Gemini/FailureAnalyst.cs` — evidence-grounded explanation
- [x] `src/RaceHunter.Gemini/Schemas/AgentSchemas.cs` — versioned structured-output types
- [x] `src/RaceHunter.Gemini/Prompts/plan-v1.txt` — planning prompt resource
- [x] `src/RaceHunter.Gemini/Prompts/strategy-v1.txt` — strategy prompt resource
- [ ] `src/RaceHunter.Gemini/Prompts/explain-v1.txt` — finding explanation resource
- [x] `src/RaceHunter.Api/RaceHunter.Api.csproj` — public API composition root
- [x] `src/RaceHunter.Api/Program.cs` — API startup and DI
- [x] `src/RaceHunter.Api/Endpoints/HuntEndpoints.cs` — hunt endpoints
- [x] `src/RaceHunter.Api/Endpoints/RunEndpoints.cs` — lifecycle and durable cursor-based SSE progress endpoints
- [x] `src/RaceHunter.Api/Endpoints/FindingEndpoints.cs` — evidence endpoints and bounded verify-fix route
- [ ] `src/RaceHunter.Api/Endpoints/ReplayEndpoints.cs` — verify-fix endpoints
- [ ] `src/RaceHunter.Api/Sandbox/SandboxSessionMiddleware.cs` — signed judge sessions and quotas
- [x] `src/RaceHunter.Api/Dockerfile` — multi-stage React and API image
- [x] `src/RaceHunter.Worker/RaceHunter.Worker.csproj` — private HTTP worker composition root
- [x] `src/RaceHunter.Worker/Program.cs` — private worker host with manual execution, Vertex model composition, and authenticated Pub/Sub push
- [x] `src/RaceHunter.Worker/Endpoints/PubSubPushEndpoint.cs` — message validation and acknowledgement
- [x] `src/RaceHunter.Worker/Execution/WorkDispatcher.cs` — message-type dispatch
- [x] `src/RaceHunter.Worker/Execution/CampaignRunner.cs` — bounded autonomous loop
- [ ] `src/RaceHunter.Worker/Execution/RunLease.cs` — lease renewal and recovery
- [x] `src/RaceHunter.Worker/Dockerfile` — worker image
- [x] `src/RaceHunter.ReferenceTarget/RaceHunter.ReferenceTarget.csproj` — demo target project
- [x] `src/RaceHunter.ReferenceTarget/Program.cs` — target host
- [x] `src/RaceHunter.ReferenceTarget/Inventory/InventoryDbContext.cs` — target persistence
- [x] `src/RaceHunter.ReferenceTarget/Inventory/OrderService.cs` — vulnerable and fixed order paths
- [ ] `src/RaceHunter.ReferenceTarget/Inventory/DemoControlEndpoints.cs` — private reset and mode controls
- [ ] `src/RaceHunter.ReferenceTarget/Inventory/OrderEndpoints.cs` — target operations and observations
- [x] `src/RaceHunter.ReferenceTarget/Dockerfile` — target image
- [x] `src/RaceHunter.Web/package.json` — React toolchain and scripts
- [x] `src/RaceHunter.Web/tsconfig.json` — TypeScript configuration
- [x] `src/RaceHunter.Web/vite.config.ts` — Vite build configuration
- [x] `src/RaceHunter.Web/src/main.tsx` — React entry point
- [x] `src/RaceHunter.Web/src/App.tsx` — routes and application shell
- [x] `src/RaceHunter.Web/src/api/client.ts` — typed HTTP/SSE client
- [x] `src/RaceHunter.Web/src/api/contracts.ts` — UI boundary types
- [ ] `src/RaceHunter.Web/src/pages/DashboardPage.tsx` — New Hunt entry
- [x] `src/RaceHunter.Web/src/pages/NewHuntPage.tsx` — target, rule, and budget input
- [x] `src/RaceHunter.Web/src/pages/PlanReviewPage.tsx` — one-time plan approval
- [x] `src/RaceHunter.Web/src/pages/LiveCampaignPage.tsx` — autonomous progress and decisions
- [x] `src/RaceHunter.Web/src/pages/FindingPage.tsx` — evidence, minimization, and comparison
- [x] `src/RaceHunter.Web/src/components/AgentActivity.tsx` — decision history
- [x] `src/RaceHunter.Web/src/components/ActorTimeline.tsx` — causal actor lanes
- [ ] `src/RaceHunter.Web/src/components/BudgetStatus.tsx` — visible bounded autonomy
- [x] `src/RaceHunter.Web/src/components/CloudProof.tsx` — model and deployment proof
- [x] `src/RaceHunter.Web/src/styles/app.css` — responsive accessible styling
- [x] `tests/RaceHunter.Domain.Tests/RaceHunter.Domain.Tests.csproj` — domain test project
- [x] `tests/RaceHunter.Domain.Tests/ExperimentRunTests.cs` — lifecycle and budget tests
- [x] `tests/RaceHunter.Application.Tests/RaceHunter.Application.Tests.csproj` — use-case tests
- [ ] `tests/RaceHunter.Application.Tests/HuntWorkflowTests.cs` — create, approve, cancel behavior
- [x] `tests/RaceHunter.Concurrency.Tests/RaceHunter.Concurrency.Tests.csproj` — concurrency test project
- [x] `tests/RaceHunter.Concurrency.Tests/SchedulerTests.cs` — barriers, seeds, limits, cancellation
- [x] `tests/RaceHunter.Concurrency.Tests/InvariantEvaluatorTests.cs` — evaluator families
- [x] `tests/RaceHunter.Concurrency.Tests/MinimizerReplayTests.cs` — reduction and replay
- [x] `tests/RaceHunter.Infrastructure.IntegrationTests/RaceHunter.Infrastructure.IntegrationTests.csproj` — PostgreSQL and adapter tests
- [ ] `tests/RaceHunter.Infrastructure.IntegrationTests/PersistenceMessagingTests.cs` — migrations, inbox, repositories
- [x] `tests/RaceHunter.Api.IntegrationTests/RaceHunter.Api.IntegrationTests.csproj` — API integration tests
- [ ] `tests/RaceHunter.Api.IntegrationTests/HuntApiTests.cs` — contracts, SSE, sandbox quotas
- [x] `tests/RaceHunter.Worker.Tests/RaceHunter.Worker.Tests.csproj` — lease-loss, retry-budget, planning-recovery, and reference-observation tests
- [x] `tests/RaceHunter.ReferenceTarget.Tests/RaceHunter.ReferenceTarget.Tests.csproj` — target test project
- [x] `tests/RaceHunter.ReferenceTarget.Tests/InventoryRaceTests.cs` — vulnerable/fixed behavior
- [x] `tests/RaceHunter.Architecture.Tests/RaceHunter.Architecture.Tests.csproj` — dependency enforcement
- [x] `tests/RaceHunter.Architecture.Tests/ArchitectureRulesTests.cs` — Clean Architecture constraints
- [x] `tests/RaceHunter.AcceptanceTests/package.json` — Playwright project
- [x] `tests/RaceHunter.AcceptanceTests/playwright.config.ts` — E2E configuration
- [x] `tests/RaceHunter.AcceptanceTests/golden-path.spec.ts` — complete vulnerable/fixed journey
- [x] `tests/RaceHunter.AcceptanceTests/recovery.spec.ts` — refresh and replay-failure recovery states
- [x] `docker-compose.yml` — local API, worker, target, PostgreSQL, and Pub/Sub emulator
- [x] `deploy/terraform/providers.tf` — Google provider and state requirements
- [x] `deploy/terraform/variables.tf` — project, region, quotas, and image inputs
- [x] `deploy/terraform/main.tf` — APIs, Artifact Registry, Cloud Run, Pub/Sub, Cloud SQL, IAM, Secret Manager, and budgets
- [x] `deploy/terraform/outputs.tf` — service URLs and evidence outputs
- [x] `deploy/scripts/deploy.ps1` — build, push, migrate, and apply orchestration
- [x] `deploy/scripts/smoke.ps1` — deployed golden-path smoke verification
- [x] `docs/architecture/system-context.md` — judge-facing architecture source
- [x] `docs/demo/demo-script.md` — timed unedited demo plan
- [x] `README.md` — reproducible local and Google Cloud instructions

### Phases
- [x] Phase 1: Walking skeleton, Docker portability, reference target, PostgreSQL foundation, and first Google Cloud smoke deployment
- [x] Phase 2: Manual deterministic hunt with bounded scheduling, three invariant families, trace evidence, cancellation, and live progress
- [x] Phase 3: Gemini planning and adaptive strategy loop with one-time approval, Pub/Sub dispatch, idempotency, leases, checkpoints, and explicit failure outcomes
- [x] Phase 4: Failure reproduction, deterministic minimization, immutable replay, causal timeline, judge evidence, and vulnerable-versus-fixed Playwright golden path
- [x] Phase 5: Security, observability, 100-actor limits, Docker/Terraform staging verification, documentation, architecture diagram, and four-minute submission package

**Phase 4 Test Results**: 165/165 checks passing across 14 suites (152 .NET, 8 Vitest, 4 mocked Playwright, 1 real Compose-backed Playwright); remediation coverage includes restart boundaries, receiver idempotency, trace-aware budgets, replay-key scope, accessible minimized steps, API validation, and persisted refresh-reconstructable reproduction/minimization lifecycle transitions.
**Phase 4 Code Review**: Compliance remediation attempt 2 completed after adversarial lifecycle, recovery, cursor-ordering, refresh, key-scope, and budget iterations; security and dependency audits passed with no remaining upgrades.
**Phase 5 Test Results**: 233/233 current-tree automated checks passed in the final local gate (219 .NET, 8 Vitest, 4 mocked Playwright, and 2 real Docker-backed Playwright journeys), plus warning-free builds, TypeScript lint, fresh-volume three-image Compose build/health, Terraform 1.14.4 containerized `fmt -check`/`init -backend=false`/`validate`, NuGet/npm vulnerability audits, and diff/credential-pattern scans.
**Phase 5 Code Review**: The initial independent review and outer compliance remediation attempt 1 reached SPEC COMPLIANCE PASS. Outer compliance remediation attempt 2 then closed unsafe manual-setup crash recovery with durable physical reservations, explicit receiver idempotency, fail-closed ambiguous outcomes, and bounded derived receiver keys. Its final independent review reached SPEC COMPLIANCE PASS with no remaining Critical or Important blockers. Live Google Cloud apply/deployed smoke remains approval-gated and explicitly unverified.

## Creative Phases

- [x] Architecture design → approved in brainstorm; recorded in `memory-bank/creative/build-racehunter-hackathon-mvp-design.md`
- [x] User Journey design → approved in brainstorm; recorded in `memory-bank/creative/build-racehunter-hackathon-mvp-design.md`
- [x] UI/UX design → approved in brainstorm; recorded in `memory-bank/creative/build-racehunter-hackathon-mvp-design.md`
- [x] Algorithm design → approved in brainstorm; recorded in `memory-bank/creative/build-racehunter-hackathon-mvp-design.md`

---

## Plan Critique

- **Requested review**: challenge the task, roadmap, and approved design for risks, alternatives, feasibility, one-way doors, missing acceptance criteria, and contradictions.
- **Backend resolution**: `codex`, inherited from the project default.
- **Outcome**: skipped — Codex companion unavailable (`unresolved:no-companion`, companion glob returned no candidates).
- **Findings applied**: none; deterministic taxonomy and concreteness validation remained mandatory and passed.

## Build Execution State

**Build Status**: COMPLETE
**Current Phase**: 5 of 5
**Auto-Build Mode**: YES
**Final Review Backend**: AUTO FINAL REVIEW BACKEND: codex:gpt-5.6-sol (high) — completed
**Last Completed**: Phase 5 — security, observability, authorized manual targets, staging definition, repeatable Docker proof, and submission package (2026-08-18)
**PLAN BACKEND**: anthropic — configured
**BRAINSTORM CRITIQUE**: skipped — codex unavailable (`unresolved:no-companion`, glob=∅)

### Resumption Notes
**Can Resume**: YES
**Resume From**: `/ala:reflect build-racehunter-hackathon-mvp`
**Notes**: All five phases and both outer compliance remediations are implemented, independently reviewed, and verified locally. Cloud resource creation and deployed smoke testing still require explicit approval; no credentials or live Google Cloud calls were used. Terraform was validated with the official 1.14.4 container without applying it.

### Halt State
**Halt Trigger**:
**Halted At Phase**:
**Halted At Step**:
**Resumption Point**:
**Halt Timestamp**:

### Deviations
- Phase 1/5 | Google Cloud smoke verification | Deferred because the user explicitly prohibited deploying billable Google Cloud resources without approval; Docker/Terraform implementation and local validation remain in scope.
- Phase 5/5 | Live Google Cloud apply and smoke | Deferred because the user explicitly prohibited credentials, Terraform apply, billable resource creation, and deployment without approval. The official Terraform 1.14.4 container passed `fmt -check`, `init -backend=false`, and `validate`; the authored smoke script includes IAM denial and exact-audience checks but was not run against cloud resources.
- Phase 1/5 | Local Pub/Sub emulator startup | Deferred to Phase 3 when the Pub/Sub message contract and authenticated push endpoint are implemented; Phase 1 Compose proves the API, worker, reference target, and isolated PostgreSQL services.
- Phase 2/5 | `ManualHuntExecutor` trace/progress persistence | Trace evidence commits before its progress event in a separate transaction; a crash can leave an unreferenced but queryable trace. The compliance reviewer accepted this as a recoverable Minor because acknowledged evidence cannot be lost.
- Phase 3/5 | Live Vertex AI invocation | Deferred because the user prohibited credential use and Google Cloud contact without explicit approval. The official `Google.GenAI` Vertex adapter and versioned response schemas compile; deterministic model fakes cover ordinary tests and the local smoke journey.
- Phase 3/5 | Deployed Pub/Sub and Cloud Run smoke | Deferred with the existing approval gate. The pinned local Pub/Sub emulator exercised the identical push envelope, inbox, lease, checkpoint, retry, and acknowledgement path.
- Phase 3/5 | Attempt evidence/checkpoint crash gap | The worker persists deterministic trace evidence before its attempt checkpoint in separate transactions, preserving the accepted Phase 2 recoverable Minor: a crash in that narrow gap can leave an unreferenced but queryable trace, while a persisted attempt checkpoint prevents target re-execution after lease recovery.
- Phase 4/5 | Replay dispatch latency | Verify Fix executes through the private worker boundary with a 30-second bounded HTTP request and durable per-artifact claim rather than a Pub/Sub replay message; the API never executes target work, and Phase 5 retains Cloud Run identity-token wiring and any asynchronous replay hardening.
- Phase 4/5 | Crash-gap transport | A process loss after the reference target commits but before RaceHunter records the result can repeat the HTTP transport on recovery. Stable receiver-side idempotency reuses the committed result without repeating the mutation; authenticated status preflight and trace correlation preserve the logical request budget.

### Active Sub-Agents
- None

### Completed Steps
- Product and hackathon discovery: COMPLETE (2026-08-18)
- Full conversational design approval: COMPLETE (2026-08-18)
- Spec Writer specification: COMPLETE (2026-08-18)
- Taxonomy and concreteness validation: COMPLETE (2026-08-18)
- Critique backend resolution: COMPLETE; review skipped because no Codex companion was installed (2026-08-18)
- Phase 1 RED gate: COMPLETE — architecture test compile failed because API/Worker entry points were absent; persistence tests failed because project and EF persistence types were absent; reference-target tests failed because its host was absent (2026-08-18)
- Phase 1 GREEN gate: COMPLETE — 17 tests passed (5 architecture, 4 PostgreSQL migration/repository, 8 vulnerable/fixed reference target) using .NET 10 and Testcontainers PostgreSQL (2026-08-18)
- Phase 1 portability gate: COMPLETE — warning-free Release build, TypeScript lint and Vite production build, `docker compose config`, three Docker image builds, and local container smoke for API/worker/target health, API persistence, and target reset (2026-08-18)
- Phase 1 review: COMPLETE — Docker base images pinned by digest; test data and Compose volumes removed after smoke; no Google Cloud calls, deployment, remote, push, or PR performed (2026-08-18)
- Phase 1 compliance remediation: COMPLETE — removed the repository-known reference-target key fallback, proved absent configuration disables demo control, retained the explicit local Compose key, and changed staging to a generated Secret Manager value exposed only to the target and worker service accounts (2026-08-18)
- Phase 2 RED gate: COMPLETE — new domain and concurrency test projects failed to compile on the absent budget, run lifecycle, schedule, trace, evaluator, cancellation, and manual-execution contracts (2026-08-18)
- Phase 2 GREEN gate: COMPLETE — 37 focused domain/concurrency tests passed for public/authenticated budgets, request/duration stops, run lifecycle, simultaneous barrier, limiter ceilings, deterministic jitter, run-scoped controlled checkpoints, ordered traces, complete correlated three-family invariant evaluation, durable target/probe-failure outcomes, manual execution, and cancellation observed inside two seconds (2026-08-18)
- Phase 2 persistence gate: COMPLETE — PostgreSQL migration and repository verification passed for durable experiment runs, run attempts, cursor-ordered live events, sequence-ordered trace evidence, and cancellation intent (2026-08-18)
- Phase 2 portability gate: COMPLETE — warning-free Release build, 59 total tests, formatting, Compose validation, three Docker image builds, a fresh-volume two-actor checkpoint hunt that deterministically failed the numeric boundary while exposing four progress events, two ordered target-correlated traces, and a completed persisted attempt, plus API-to-worker cancellation measured at 201 ms with its original request timestamp preserved (2026-08-18)
- Phase 2 review: COMPLETE — independent adversarial review reached PROCEED after remediation of run-scoped checkpoint cleanup, lossless trace ordering, independent cancellation polling/timestamp preservation, terminal target/probe failures, complete correlation-key validation, and trace referential integrity; implementation stays inside the deterministic/manual boundary; no Gemini, Pub/Sub, remote, push, PR, deployment, credentials, or Google Cloud contact occurred; temporary Compose containers and volumes were removed (2026-08-18)
- Phase 3 RED gate: COMPLETE — the new focused suites failed at compile time on the absent agent, hunt workflow, work-message, messaging-recovery, and SSE contracts while all existing Phase 1–2 tests remained green (2026-08-18)
- Phase 3 GREEN gate: COMPLETE — 25 focused tests passed for Gemini prompt/input specificity, schema validation, constrained repair and model-call budgets; exact-version one-time approval; action and deterministic-truth validation; Pub/Sub envelopes and inbox idempotency; leases, heartbeats, attempt/decision checkpoints, classified retries and dead-lettering; and PostgreSQL-backed SSE cursor recovery (2026-08-18)
- Phase 3 persistence gate: COMPLETE — migration-backed integration tests verified inbox ownership/recovery, duplicate completion, retry-later behavior, dead-letter outcome, and atomic agent-decision plus work-checkpoint persistence; the attempt checkpoint recovers without repeating already-completed deterministic target work (2026-08-18)
- Phase 3 portability gate: COMPLETE — 84 total Release tests, TypeScript lint and Vite build, Compose validation, three Docker image builds, and a fresh-volume Pub/Sub-emulator journey completed planning, same-key idempotent approval, two persisted agent iterations, and a deterministic violation with no dead letters (2026-08-18)
- Phase 3 review: COMPLETE — provider types remain behind application contracts; model output is schema- and allowlist-constrained and cannot determine finding truth; model calls, requests, actors, iterations, duration, retries, and timing remain server-bounded; poison work and model/worker/budget outcomes are explicit; no credentials, live model calls, Google Cloud contact, remote, push, PR, or deployment occurred (2026-08-18)
- Phase 3 compliance remediation RED gate: COMPLETE — 13 adversarial checks were added for concurrent expired-lease takeover, lost-heartbeat cancellation, busy-delivery recovery, cumulative retry/duration budgets, transient planning retry, executable cross-observation evidence, durable dead-letter subject guidance, terminal immutability, and UI refresh reconstruction; the first compile failed on the absent recovery contracts and cross-observation compiler (2026-08-18)
- Phase 3 compliance remediation GREEN gate: COMPLETE — 95 .NET tests and 2 Vitest UI tests passed. PostgreSQL conditional updates now grant exactly one expired-lease takeover; an unrenewable lease cancels handler work and prevents stale completion/failure/checkpoint writes; busy and retry-scheduled pushes are negatively acknowledged; persisted subject retry ceilings and original run start time govern all recoveries; transient planning remains `Planning` until retry exhaustion; cross-observation plans require allowlisted metrics/relation and compile through the deterministic evaluator; and dead-letter redelivery reconciles visible subject failure plus recovery guidance without mutating terminal runs (2026-08-18)
- Phase 3 compliance remediation portability/review: COMPLETE — formatting, architecture rules, TypeScript lint, Vite production build, Vitest, Compose validation, three Docker image builds, and a fresh-volume Pub/Sub plan/approve/run smoke passed; same-key approval converged and the run persisted four ordered events through deterministic violation. Containers and volumes were removed; no credentials, live model calls, Google Cloud contact, remote, push, PR, or deployment occurred (2026-08-18)
- Phase 3 compliance remediation attempt 2 RED gate: COMPLETE — two focused adversarial tests were added at the remaining compliance boundaries. The planning test failed to compile against the prior handler because delivery identity and durable checkpoint state were absent; the checkpoint race test now also inspects the generated SQL and requires lease owner, status, and expiry in the atomic update predicate at the commit boundary (2026-08-18)
- Phase 3 compliance remediation attempt 2 GREEN gate: COMPLETE — 97 .NET tests and 2 Vitest UI tests passed. The agent-decision/event/checkpoint transaction conditionally updates the active lease row before staging inserts and requires one affected row, holding the database row lock through commit so takeover and stale persistence cannot both win. Planning checkpoints now carry cumulative provider calls, completed plans, and terminal outcomes; recovered planning receives only its remaining model-call allowance, and a one-call budget with a five-retry ceiling makes exactly one provider invocation before the explicit `BudgetExhausted` outcome (2026-08-18)
- Phase 3 compliance remediation attempt 2 portability/review: COMPLETE — Release tests, formatting verification, architecture checks, TypeScript lint, Vitest, Vite production build, Compose validation, and the worker image build passed. The prior fresh-volume Pub/Sub end-to-end smoke remains current because this remediation changes only guarded persistence and recovery accounting and both paths have deterministic database/worker coverage. The accepted trace/checkpoint crash-gap Minor remains visible; no credentials, live model calls, Google Cloud contact, remote, push, PR, or deployment occurred (2026-08-18)
- Phase 4 RED gates: COMPLETE — missing finding/replay application contracts, minimizer behavior, immutable persistence, UI projection, and browser journey failed at compile/module resolution; later adversarial RED tests reproduced PostgreSQL timestamp fingerprint drift, delimiter-collision risk, mutable replay steps, incomplete minimizer candidate exploration, mixed-attempt timelines, async finding-link staleness, concurrent Verify Fix duplication, and decision-checkpoint recovery loss (2026-08-18)
- Phase 4 GREEN gate: COMPLETE — 34 focused checks passed for exact 3/3 reference reproduction, deterministic actor/step reduction, exact seeded-offset propagation and recovery, immutable structured fingerprints, PostgreSQL round trips, atomic finding evidence, one server-owned fixed replay under synchronized concurrency, attempt-filtered actor lanes, Agent Activity, API Problem Details, React refresh/recovery, and the full Playwright journey (2026-08-18)
- Phase 4 integration verification: COMPLETE — 133/133 tests passed across 13 suites (125 .NET, 5 Vitest, 3 Playwright); warning-free Release and Vite builds, .NET formatting, TypeScript lint, Compose configuration, Redocly OpenAPI validation, NuGet/npm vulnerability audits, and three fresh image builds passed (2026-08-18)
- Phase 4 Docker golden path: COMPLETE — a fresh-volume Compose plan/approve/run produced the exact verified message, three failed reproductions, a two-actor/two-step artifact, one-attempt causal timeline, persisted Agent Activity, vulnerable `Fail`, fixed `Pass`, one fixed attempt across distinct keys, and an unchanged artifact; containers and both test volumes were removed afterward (2026-08-18)
- Phase 4 review/documentation: COMPLETE — four adversarial review iterations closed exact-schedule, recovery, claim-race, no-transaction-across-HTTP, no-inline-SQL, API-contract, dependency, and evidence-projection findings; README, technical context, system patterns, and OpenAPI 3.1 were updated. Cloud Run IAM/ID tokens, observability, staging, and deployment remain Phase 5; no credentials, Google Cloud contact, remote, push, PR, or deployment occurred (2026-08-18)
- Phase 4 compliance remediation RED gate: COMPLETE — adversarial tests first exposed absent per-probe restart state, receiver crash-gap idempotency, accessible minimized schedule rendering, oversized/padded replay-key validation, and a real browser/backend journey; further RED cases covered actor-step and cross-artifact key collisions, partial checkpoint recovery, and pre-call request-budget reservation (2026-08-18)
- Phase 4 compliance remediation GREEN gate: COMPLETE — PostgreSQL now stores keyed reproduction/minimization/proof checkpoints; the reference target durably reuses scoped reset/order outcomes and reports completed correlations for trace-aware preflight; deterministic final IDs/fingerprints survive restart; the UI renders actor/operation/step/offset evidence; normalized 1..160 replay keys fail with RFC 9457 before target work; and real Compose-backed Playwright proves vulnerable/fixed plus refresh without route mocks (2026-08-18)
- Phase 4 compliance remediation integration: COMPLETE — 152/152 checks passed across 14 suites, including 143 .NET, 5 Vitest, 3 mocked Playwright, and 1 real Docker-backed Playwright; Release/Vite builds, formatting, lint, Compose/OpenAPI validation, security audits, and diff checks passed, and isolated test resources were removed (2026-08-18)
- Phase 4 compliance remediation attempt 2 RED gate: COMPLETE — focused domain/API suites failed to compile without `Reproducing` and `Minimizing`; the worker suite failed on the absent persist-before-work coordinator; Vitest lacked lifecycle projection; and Playwright remained at generic `Running` after a persisted `reproduction-started` SSE event (2026-08-18)
- Phase 4 compliance remediation attempt 2 GREEN gate: COMPLETE — the legal monotonic run lifecycle now persists `Reproducing` and `Minimizing` plus ordered run events before probe work; recovery does not duplicate or regress phases; active phases can fail, cancel, or complete while terminal state remains immutable; API refresh reconstructs both phases from PostgreSQL; and SSE projects the same vocabulary without regressing on replayed older cursors (2026-08-18)
- Phase 4 compliance remediation attempt 2 integration: COMPLETE — 165/165 checks passed across 14 suites: 152 .NET, 8 Vitest, 4 mocked Playwright, and 1 fresh-volume real Docker-backed Playwright. Release/Vite builds, formatting, TypeScript lint, Compose/OpenAPI validation, three Docker image builds, and NuGet/npm vulnerability audits passed; isolated containers and volumes were removed. No credentials, live model calls, Google Cloud contact, remote, push, PR, or deployment occurred (2026-08-18)
- Phase 5 RED gates: COMPLETE — focused tests first exposed missing manual-target authorization/execution, cancellation monitoring, reserved-address/port defenses, deployment ingress/IAM and global ceilings, W3C outbox propagation, persistent cloud proof, setup/probe recovery budgets, stable POST keys, custom operation/invariant replay identity, sensitive-path binding, and a nondeterministic controlled external fixture (2026-08-18)
- Phase 5 GREEN gate: COMPLETE — authenticated Development admin configuration compiles immutable HTTP/JSON target snapshots with safe templates and typed observations; the deferred secret provider, DNS-pinned SSRF-safe client, redactor, cancellation monitor, exact operation schedule, >=2/3 external reproduction, immutable custom invariant replay, persisted probe accounting, structured OTel signals, Cloud Proof, Cloud Run identity tokens, least-privilege IAM, worker instance ceiling, cost controls, scripts, and submission docs are implemented (2026-08-18)
- Phase 5 integration and portability: COMPLETE — 211/211 .NET tests, 8/8 Vitest, 4/4 mocked Playwright, and two consecutive 2/2 real Docker-backed Playwright runs passed; warning-free build, TypeScript lint, Vite build, three-image Compose build/health, Terraform container validation, NuGet/npm audits, Compose validation, and diff/credential scans passed. Fresh scoped Docker volumes proved both the configurable `reserve-seat` manual journey and reference vulnerable/fixed journey repeatably (2026-08-18)
- Phase 5 outer compliance remediation: COMPLETE — manual targets now persist a one-way owner-key identity and enforce `401`/`403` authorization across configuration, hunt creation/planning/approval, every run read/action, Cloud Proof, finding read, replay, and Verify Fix. The React session carries the admin credential throughout the manual journey while the public reference sandbox remains open. The controlled fixture no longer derives truth from `actorOrdinal`: concurrent requests rendezvous before mutation, then observe shared persisted reservation count/capacity, and two equivalent Docker runs proved external reproduction and immutable replay. The UI exposes host/port, setup and operation method/path/template, bounded actor/run/checkpoint substitutions, typed observation metrics/paths, all three compatible invariant families, relation/threshold, sensitive paths, and secret reference with safe preview. Configuration/execution/replay safety failures persist sanitized categorized audit records, manual target latency/outcome metrics cover all exits, and staging smoke accepts only authoritative `401`/`403` IAM denial from known health routes with all three service URLs required (2026-08-18)
- Phase 5 initial independent review: COMPLETE — SPEC COMPLIANCE PASS before the outer compliance audit. No credentials, Google Cloud API contact, Terraform apply, billable resources, remote, push, PR, or deployment occurred; live cloud smoke remains explicitly pending approval (2026-08-18)
- Phase 5 outer compliance review attempt 1: FAIL — 0 Critical and 2 Important findings: typed per-operation invariant compatibility was not enforced at the server/model trust boundary, and durable totals/review state were stale. RED tests now reject text-as-numeric, number-as-cardinality, and cross metrics split across operations; the planning contract carries typed per-operation capabilities, the validator requires selected/co-produced compatible evidence, and this authoritative record is reconciled (2026-08-18)
- Phase 5 outer compliance review attempt 2: COMPLETE — independent SPEC COMPLIANCE PASS with no remaining Critical or Important blockers. The reviewer verified typed selected-operation compatibility, SafetyAuthorization taxonomy, owner-bound authorization, shared-state external truth, configurable UI, persistent safety audits, IAM smoke behavior, metrics, recovery, and durable status/evidence consistency (2026-08-18)
- Phase 5 outer compliance remediation attempt 2 RED gate: COMPLETE — review demonstrated that a stable header alone could not distinguish completed from ambiguous setup after a crash, physical setup deliveries could escape durable budget accounting, unsafe setup could be resent, and composed replay/minimization keys could exceed receiver bounds. Focused tests were added for the real ManualHttpTargetClient crash window, non-idempotent ambiguity, durable PostgreSQL claims, receiver replay, maximum caller keys, 64-character operation IDs, and worst-case actor/scope composition (2026-08-18)
- Phase 5 outer compliance remediation attempt 2 GREEN gate: COMPLETE — manual setup now declares `receiver-keyed` or `none`; PostgreSQL reserves every physical setup request before transport and persists reserved/completed/ambiguous outcomes; controlled reset replay is transactional; receiver-keyed recovery reuses a fixed 67-character derived operation key without resetting twice; unsafe ambiguity immediately emits `manual_recovery_required`; actor work cannot start beyond the durable run ceiling; immutable replay binds the originating run; and maximum Verify Fix/minimization scopes stay bounded (2026-08-18)
- Phase 5 outer compliance remediation attempt 2 integration: COMPLETE — 219/219 .NET, 8/8 Vitest, 4/4 mocked Playwright, and 2/2 final fresh-volume real Docker Playwright journeys passed. Warning-free build, TypeScript lint/Vite build, three-image Compose build and health, Terraform 1.14.4 container validation, NuGet/npm audits, Compose/diff/credential scans passed; isolated containers, network, and volumes were removed afterward (2026-08-18)
- Phase 5 outer compliance remediation attempt 2 review: COMPLETE — independent SPEC COMPLIANCE PASS with no remaining Critical or Important blockers after two review iterations closed first-failure safety taxonomy and all receiver/store key-length compositions. No credentials, Google Cloud contact, apply, billable resources, remote, push, PR, or deployment occurred; live IAM/deployed smoke remains approval-gated (2026-08-18)

### Current Build Step
**Step**: Step 11 - Phase Git Completion
**Status**: COMPLETE
**Completed**: 2026-08-18
**Output**: All five build phases and both Phase 5 outer compliance remediations are complete locally with durable RED/GREEN evidence, crash-safe manual setup claims, physical request accounting, fail-closed unsafe recovery, bounded receiver keys, repeatable Docker journeys, validated Terraform staging definition, and final independent SPEC COMPLIANCE PASS. The next ALA action is reflection. No remote, push, PR, deployment, credentials, or Google Cloud resource creation occurred.
