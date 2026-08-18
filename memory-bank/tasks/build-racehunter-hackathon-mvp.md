---
slug: build-racehunter-hackathon-mvp
feature: racehunter-autonomous-concurrency-campaign
status: PLANNING_COMPLETE
---

# build-racehunter-hackathon-mvp: Build the RaceHunter Hackathon MVP

**Complexity**: Level 4
**Status**: PLANNING_COMPLETE
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
- [ ] `global.json` — pin .NET SDK 10.0.400 with roll-forward policy
- [ ] `RaceHunter.slnx` — .NET solution
- [ ] `Directory.Build.props` — nullable, warnings-as-errors, analyzers, deterministic build
- [ ] `Directory.Packages.props` — central NuGet versions
- [ ] `.editorconfig` — shared formatting and analyzer rules
- [ ] `.dockerignore` — exclude secrets, Memory Bank, tests, and development outputs from runtime images
- [ ] `src/RaceHunter.Domain/RaceHunter.Domain.csproj` — domain project
- [ ] `src/RaceHunter.Domain/Common/DomainException.cs` — domain failure base
- [ ] `src/RaceHunter.Domain/Projects/Project.cs` — project aggregate
- [ ] `src/RaceHunter.Domain/Targets/TargetSystem.cs` — authorized target aggregate
- [ ] `src/RaceHunter.Domain/Experiments/Experiment.cs` — experiment aggregate
- [ ] `src/RaceHunter.Domain/Scenarios/ScenarioDefinition.cs` — versioned actors and steps
- [ ] `src/RaceHunter.Domain/Invariants/InvariantDefinition.cs` — versioned invariant contract
- [ ] `src/RaceHunter.Domain/Runs/ExperimentRun.cs` — campaign lifecycle aggregate
- [ ] `src/RaceHunter.Domain/Runs/RunAttempt.cs` — schedule attempt entity
- [ ] `src/RaceHunter.Domain/Tracing/TraceEvent.cs` — append-only evidence entity
- [ ] `src/RaceHunter.Domain/Findings/Finding.cs` — verified finding aggregate
- [ ] `src/RaceHunter.Domain/Replays/ReplayArtifact.cs` — immutable replay aggregate
- [ ] `src/RaceHunter.Domain/Agents/AgentIteration.cs` — persisted agent decision record
- [ ] `src/RaceHunter.Domain/Budgets/ExperimentBudget.cs` — bounded campaign value object
- [ ] `src/RaceHunter.Application/RaceHunter.Application.csproj` — use-case project
- [ ] `src/RaceHunter.Application/Abstractions/Persistence.cs` — aggregate repositories and unit of work
- [ ] `src/RaceHunter.Application/Abstractions/Agents.cs` — planner, strategist, analyst, and minimizer interfaces
- [ ] `src/RaceHunter.Application/Abstractions/Execution.cs` — scheduler, target client, queue, clock, and randomness interfaces
- [ ] `src/RaceHunter.Application/Hunts/CreateHunt.cs` — create hunt use case
- [ ] `src/RaceHunter.Application/Hunts/GeneratePlan.cs` — asynchronous planning use case
- [ ] `src/RaceHunter.Application/Hunts/ApproveAndRun.cs` — one-time approval use case
- [ ] `src/RaceHunter.Application/Runs/GetRun.cs` — durable run projection
- [ ] `src/RaceHunter.Application/Runs/CancelRun.cs` — idempotent cancellation
- [ ] `src/RaceHunter.Application/Findings/GetFinding.cs` — evidence projection
- [ ] `src/RaceHunter.Application/Replays/VerifyFix.cs` — replay comparison use case
- [ ] `src/RaceHunter.Contracts/RaceHunter.Contracts.csproj` — boundary contracts
- [ ] `src/RaceHunter.Contracts/HuntContracts.cs` — hunt and plan DTOs
- [ ] `src/RaceHunter.Contracts/RunContracts.cs` — run and progress DTOs
- [ ] `src/RaceHunter.Contracts/FindingContracts.cs` — finding and timeline DTOs
- [ ] `src/RaceHunter.Contracts/ReplayContracts.cs` — replay DTOs
- [ ] `src/RaceHunter.Contracts/WorkMessage.cs` — versioned Pub/Sub envelope
- [ ] `src/RaceHunter.Infrastructure/RaceHunter.Infrastructure.csproj` — persistence and cloud adapters
- [ ] `src/RaceHunter.Infrastructure/Persistence/RaceHunterDbContext.cs` — EF Core context
- [ ] `src/RaceHunter.Infrastructure/Persistence/EntityConfigurations.cs` — strongly typed mappings
- [ ] `src/RaceHunter.Infrastructure/Persistence/Repositories.cs` — aggregate-specific repositories
- [ ] `src/RaceHunter.Infrastructure/Persistence/Migrations/InitialCreate.cs` — initial migration
- [ ] `src/RaceHunter.Infrastructure/Persistence/Migrations/InitialCreate.Designer.cs` — migration metadata
- [ ] `src/RaceHunter.Infrastructure/Persistence/Migrations/RaceHunterDbContextModelSnapshot.cs` — EF model snapshot
- [ ] `src/RaceHunter.Infrastructure/Messaging/PubSubWorkPublisher.cs` — dispatch adapter
- [ ] `src/RaceHunter.Infrastructure/Messaging/InboxStore.cs` — duplicate-delivery guard
- [ ] `src/RaceHunter.Infrastructure/Targets/SafeTargetClientFactory.cs` — allowlisted HTTP clients
- [ ] `src/RaceHunter.Infrastructure/Targets/TargetDestinationValidator.cs` — SSRF and redirect defense
- [ ] `src/RaceHunter.Infrastructure/Secrets/GoogleSecretProvider.cs` — Secret Manager adapter
- [ ] `src/RaceHunter.Infrastructure/Observability/TelemetryRegistration.cs` — logs, metrics, and traces
- [ ] `src/RaceHunter.Concurrency/RaceHunter.Concurrency.csproj` — deterministic execution project
- [ ] `src/RaceHunter.Concurrency/Scheduling/ConcurrencyScheduler.cs` — bounded actor runtime
- [ ] `src/RaceHunter.Concurrency/Scheduling/SchedulePlan.cs` — immutable schedule model
- [ ] `src/RaceHunter.Concurrency/Scheduling/SimultaneousStartStrategy.cs` — barrier strategy
- [ ] `src/RaceHunter.Concurrency/Scheduling/SeededJitterStrategy.cs` — seeded offsets
- [ ] `src/RaceHunter.Concurrency/Scheduling/CheckpointStrategy.cs` — controlled interleaving
- [ ] `src/RaceHunter.Concurrency/Tracing/TraceCollector.cs` — ordered evidence collection
- [ ] `src/RaceHunter.Concurrency/Invariants/InvariantEvaluatorRegistry.cs` — evaluator dispatch
- [ ] `src/RaceHunter.Concurrency/Invariants/NumericBoundaryEvaluator.cs` — numeric boundary evaluator
- [ ] `src/RaceHunter.Concurrency/Invariants/CardinalityEvaluator.cs` — uniqueness/cardinality evaluator
- [ ] `src/RaceHunter.Concurrency/Invariants/CrossObservationEvaluator.cs` — response/state relationship evaluator
- [ ] `src/RaceHunter.Concurrency/Minimization/FailureMinimizer.cs` — verified delta reduction
- [ ] `src/RaceHunter.Concurrency/Replay/ReplayExecutor.cs` — immutable replay execution
- [ ] `src/RaceHunter.Gemini/RaceHunter.Gemini.csproj` — Gemini adapter project
- [ ] `src/RaceHunter.Gemini/GeminiClient.cs` — Google Gen AI SDK wrapper
- [ ] `src/RaceHunter.Gemini/ScenarioPlanner.cs` — structured initial planner
- [ ] `src/RaceHunter.Gemini/ExperimentStrategist.cs` — allowlisted next-action selector
- [ ] `src/RaceHunter.Gemini/FailureAnalyst.cs` — evidence-grounded explanation
- [ ] `src/RaceHunter.Gemini/Schemas/AgentSchemas.cs` — versioned structured-output types
- [ ] `src/RaceHunter.Gemini/Prompts/plan-v1.txt` — planning prompt resource
- [ ] `src/RaceHunter.Gemini/Prompts/strategy-v1.txt` — strategy prompt resource
- [ ] `src/RaceHunter.Gemini/Prompts/explain-v1.txt` — finding explanation resource
- [ ] `src/RaceHunter.Api/RaceHunter.Api.csproj` — public API composition root
- [ ] `src/RaceHunter.Api/Program.cs` — API startup and DI
- [ ] `src/RaceHunter.Api/Endpoints/HuntEndpoints.cs` — hunt endpoints
- [ ] `src/RaceHunter.Api/Endpoints/RunEndpoints.cs` — lifecycle and SSE endpoints
- [ ] `src/RaceHunter.Api/Endpoints/FindingEndpoints.cs` — evidence endpoints
- [ ] `src/RaceHunter.Api/Endpoints/ReplayEndpoints.cs` — verify-fix endpoints
- [ ] `src/RaceHunter.Api/Sandbox/SandboxSessionMiddleware.cs` — signed judge sessions and quotas
- [ ] `src/RaceHunter.Api/Dockerfile` — multi-stage React and API image
- [ ] `src/RaceHunter.Worker/RaceHunter.Worker.csproj` — private HTTP worker composition root
- [ ] `src/RaceHunter.Worker/Program.cs` — authenticated Pub/Sub push host
- [ ] `src/RaceHunter.Worker/Endpoints/PubSubPushEndpoint.cs` — message validation and acknowledgement
- [ ] `src/RaceHunter.Worker/Execution/WorkDispatcher.cs` — message-type dispatch
- [ ] `src/RaceHunter.Worker/Execution/CampaignRunner.cs` — bounded autonomous loop
- [ ] `src/RaceHunter.Worker/Execution/RunLease.cs` — lease renewal and recovery
- [ ] `src/RaceHunter.Worker/Dockerfile` — worker image
- [ ] `src/RaceHunter.ReferenceTarget/RaceHunter.ReferenceTarget.csproj` — demo target project
- [ ] `src/RaceHunter.ReferenceTarget/Program.cs` — target host
- [ ] `src/RaceHunter.ReferenceTarget/Inventory/InventoryDbContext.cs` — target persistence
- [ ] `src/RaceHunter.ReferenceTarget/Inventory/OrderService.cs` — vulnerable and fixed order paths
- [ ] `src/RaceHunter.ReferenceTarget/Inventory/DemoControlEndpoints.cs` — private reset and mode controls
- [ ] `src/RaceHunter.ReferenceTarget/Inventory/OrderEndpoints.cs` — target operations and observations
- [ ] `src/RaceHunter.ReferenceTarget/Dockerfile` — target image
- [ ] `src/RaceHunter.Web/package.json` — React toolchain and scripts
- [ ] `src/RaceHunter.Web/tsconfig.json` — TypeScript configuration
- [ ] `src/RaceHunter.Web/vite.config.ts` — Vite build configuration
- [ ] `src/RaceHunter.Web/src/main.tsx` — React entry point
- [ ] `src/RaceHunter.Web/src/App.tsx` — routes and application shell
- [ ] `src/RaceHunter.Web/src/api/client.ts` — typed HTTP/SSE client
- [ ] `src/RaceHunter.Web/src/api/contracts.ts` — UI boundary types
- [ ] `src/RaceHunter.Web/src/pages/DashboardPage.tsx` — New Hunt entry
- [ ] `src/RaceHunter.Web/src/pages/NewHuntPage.tsx` — target, rule, and budget input
- [ ] `src/RaceHunter.Web/src/pages/PlanReviewPage.tsx` — one-time plan approval
- [ ] `src/RaceHunter.Web/src/pages/LiveCampaignPage.tsx` — autonomous progress and decisions
- [ ] `src/RaceHunter.Web/src/pages/FindingPage.tsx` — evidence, minimization, and comparison
- [ ] `src/RaceHunter.Web/src/components/AgentActivity.tsx` — decision history
- [ ] `src/RaceHunter.Web/src/components/ActorTimeline.tsx` — causal actor lanes
- [ ] `src/RaceHunter.Web/src/components/BudgetStatus.tsx` — visible bounded autonomy
- [ ] `src/RaceHunter.Web/src/components/CloudProof.tsx` — model and deployment proof
- [ ] `src/RaceHunter.Web/src/styles/app.css` — responsive accessible styling
- [ ] `tests/RaceHunter.Domain.Tests/RaceHunter.Domain.Tests.csproj` — domain test project
- [ ] `tests/RaceHunter.Domain.Tests/ExperimentRunTests.cs` — lifecycle and budget tests
- [ ] `tests/RaceHunter.Application.Tests/RaceHunter.Application.Tests.csproj` — use-case tests
- [ ] `tests/RaceHunter.Application.Tests/HuntWorkflowTests.cs` — create, approve, cancel behavior
- [ ] `tests/RaceHunter.Concurrency.Tests/RaceHunter.Concurrency.Tests.csproj` — concurrency test project
- [ ] `tests/RaceHunter.Concurrency.Tests/SchedulerTests.cs` — barriers, seeds, limits, cancellation
- [ ] `tests/RaceHunter.Concurrency.Tests/InvariantEvaluatorTests.cs` — evaluator families
- [ ] `tests/RaceHunter.Concurrency.Tests/MinimizerReplayTests.cs` — reduction and replay
- [ ] `tests/RaceHunter.Infrastructure.IntegrationTests/RaceHunter.Infrastructure.IntegrationTests.csproj` — PostgreSQL and adapter tests
- [ ] `tests/RaceHunter.Infrastructure.IntegrationTests/PersistenceMessagingTests.cs` — migrations, inbox, repositories
- [ ] `tests/RaceHunter.Api.IntegrationTests/RaceHunter.Api.IntegrationTests.csproj` — API integration tests
- [ ] `tests/RaceHunter.Api.IntegrationTests/HuntApiTests.cs` — contracts, SSE, sandbox quotas
- [ ] `tests/RaceHunter.ReferenceTarget.Tests/RaceHunter.ReferenceTarget.Tests.csproj` — target test project
- [ ] `tests/RaceHunter.ReferenceTarget.Tests/InventoryRaceTests.cs` — vulnerable/fixed behavior
- [ ] `tests/RaceHunter.Architecture.Tests/RaceHunter.Architecture.Tests.csproj` — dependency enforcement
- [ ] `tests/RaceHunter.Architecture.Tests/ArchitectureRulesTests.cs` — Clean Architecture constraints
- [ ] `tests/RaceHunter.AcceptanceTests/package.json` — Playwright project
- [ ] `tests/RaceHunter.AcceptanceTests/playwright.config.ts` — E2E configuration
- [ ] `tests/RaceHunter.AcceptanceTests/golden-path.spec.ts` — complete vulnerable/fixed journey
- [ ] `tests/RaceHunter.AcceptanceTests/recovery.spec.ts` — refresh, cancellation, and failure states
- [ ] `docker-compose.yml` — local API, worker, target, PostgreSQL, and Pub/Sub emulator
- [ ] `deploy/terraform/providers.tf` — Google provider and state requirements
- [ ] `deploy/terraform/variables.tf` — project, region, quotas, and image inputs
- [ ] `deploy/terraform/main.tf` — APIs, Artifact Registry, Cloud Run, Pub/Sub, Cloud SQL, IAM, Secret Manager, and budgets
- [ ] `deploy/terraform/outputs.tf` — service URLs and evidence outputs
- [ ] `deploy/scripts/deploy.ps1` — build, push, migrate, and apply orchestration
- [ ] `deploy/scripts/smoke.ps1` — deployed golden-path smoke verification
- [ ] `docs/architecture/system-context.md` — judge-facing architecture source
- [ ] `docs/demo/demo-script.md` — timed unedited demo plan
- [ ] `README.md` — reproducible local and Google Cloud instructions

### Phases
- [ ] Phase 1: Walking skeleton, Docker portability, reference target, PostgreSQL foundation, and first Google Cloud smoke deployment
- [ ] Phase 2: Manual deterministic hunt with bounded scheduling, three invariant families, trace evidence, cancellation, and live progress
- [ ] Phase 3: Gemini planning and adaptive strategy loop with one-time approval, Pub/Sub dispatch, idempotency, leases, checkpoints, and explicit failure outcomes
- [ ] Phase 4: Failure reproduction, deterministic minimization, immutable replay, causal timeline, judge evidence, and vulnerable-versus-fixed Playwright golden path
- [ ] Phase 5: Security, observability, 100-actor limits, Docker/Terraform staging verification, documentation, architecture diagram, and four-minute submission package

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

**Build Status**: RUNNING
**Current Phase**: 1 of 5
**Auto-Build Mode**: YES
**Last Completed**: Build-ready specification and validation
**PLAN BACKEND**: anthropic — configured
**BRAINSTORM CRITIQUE**: skipped — codex unavailable (`unresolved:no-companion`, glob=∅)

### Resumption Notes
**Can Resume**: YES
**Resume From**: Phase 1
**Notes**: Local Git initialized; feature branch created; cloud resource creation and deployed smoke testing require explicit approval.

### Halt State
**Halt Trigger**:
**Halted At Phase**:
**Halted At Step**:
**Resumption Point**:
**Halt Timestamp**:

### Deviations
- Phase 1/5 | Google Cloud smoke verification | Deferred because the user explicitly prohibited deploying billable Google Cloud resources without approval; Docker/Terraform implementation and local validation remain in scope.

### Active Sub-Agents
- None

### Completed Steps
- Product and hackathon discovery: COMPLETE (2026-08-18)
- Full conversational design approval: COMPLETE (2026-08-18)
- Spec Writer specification: COMPLETE (2026-08-18)
- Taxonomy and concreteness validation: COMPLETE (2026-08-18)
- Critique backend resolution: COMPLETE; review skipped because no Codex companion was installed (2026-08-18)
