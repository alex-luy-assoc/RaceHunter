# RaceHunter Hackathon MVP Design

## Decision

**Chosen**: Build a bounded autonomous concurrency campaign platform with a reliable reference inventory target plus authenticated manual HTTP/JSON target configuration. The developer reviews one Gemini-generated plan, approves it once, and then observes an unattended asynchronous campaign that produces deterministic evidence, a minimized reproduction, and a vulnerable-versus-fixed replay comparison.

This approach maximizes the hackathon's 40% operational-utility score without sacrificing the 30% architecture and 30% production-readiness dimensions. It is a real extensible tool rather than a scripted demo, while excluding broad OpenAPI ingestion and arbitrary public-target support from the critical path.

## Context and Constraints

- Capacity: full-time solo builder or two-person team.
- Deadline: August 31, 2026 at 5:00 PM Pacific.
- Track: Taskmaster.
- Honest origin: recurring transactional-system risk rather than a fabricated personal incident.
- Public access: one-click sandbox restricted to the included reference target with hard quotas.
- Manual targets: authenticated local/admin use only.
- Stack: React, .NET 10, PostgreSQL, Google Gen AI .NET SDK, Gemini 3.5 Flash, Vertex AI, Cloud Run, Pub/Sub, Cloud SQL, Secret Manager, OpenTelemetry.
- Portability: three Docker images and Docker Compose locally; identical images on Google Cloud via Terraform.
- Development control plane: Codex and the ALA Memory Bank guide implementation but are never part of the deployed runtime.

## Approaches Considered

### Demo-only race detector

Hard-code the reference target and golden scenario. This is fastest and highly reliable, but judges could reasonably see it as a narrow showcase rather than a product that removes real-world developer friction.

### Bounded autonomous campaign platform

Combine the polished reference journey with a deliberately small manual HTTP/JSON target contract. Restrict Gemini to structured planning and allowlisted strategy selection while deterministic code owns execution and evidence. This balances credible generality, bounded autonomy, architecture quality, and demo reliability.

### Broad API concurrency fuzzer

Add OpenAPI import, a large invariant catalog, arbitrary targets, and broad schedule generation. This provides breadth but creates unacceptable parser, safety, UX, and demo risk within the contest period.

## Runtime Architecture

The public `RaceHunter.Api` Cloud Run service serves the compiled React application and owns HTTP contracts, sandbox sessions, commands, queries, SSE, and Pub/Sub publication. `RaceHunter.Worker` is a private authenticated Cloud Run HTTP service that receives Pub/Sub push messages and executes campaigns. `RaceHunter.ReferenceTarget` is a private Cloud Run service with vulnerable/fixed order paths and protected demo-control endpoints.

Cloud SQL PostgreSQL is the system of record. One small instance hosts separate RaceHunter and reference-target databases. Gemini 3.5 Flash is invoked from the worker through Vertex AI's global endpoint and the worker's workload identity. Secret Manager supplies external target credentials by reference. Structured logs, metrics, and traces flow to Google Cloud observability.

React → ASP.NET Core API → PostgreSQL; API → Pub/Sub → .NET Execution Worker; Worker → Gemini via Vertex AI and Worker → Target API. Docker supplies the packaging boundary for API, worker, and reference target.

## Components

The judge-facing UI has four primary screens: Launch/New Hunt, Plan Review, Live Campaign, and Finding & Replay. A built-in Agent Activity panel exposes validated decisions, model/schema version, budgets, trace correlations, and Cloud Run revision without displaying hidden chain-of-thought.

Backend modules separate campaign orchestration, Gemini planning/strategy, deterministic concurrency, invariant evaluation, minimization, safe target access, persistence/dispatch, and judge evidence. The MVP supports numeric boundary, uniqueness/cardinality, and cross-observation response/state invariants. State transition and exactly-once evaluators are stretch work.

The Gemini action vocabulary includes actor-count changes, scheduling-strategy selection, bounded timing adjustment, confidence repetition, minimization start, and stopping. Unknown actions or references are rejected before execution.

## Data Flow and User Journey

From `/hunts/new`, the developer selects Inventory Sandbox or an authorized manual target, enters a business rule and budgets, and selects **Generate Plan**. The API persists the draft and publishes `PlanRequested`. The worker asks Gemini for a schema-constrained plan, validates and persists it, and the Plan Review page becomes available through SSE.

The developer selects **Approve & Run** once. A `RunRequested` message starts the worker's bounded loop: reset/setup, execute a schedule, append trace evidence, evaluate invariants deterministically, summarize evidence, ask Gemini for one allowed next action, persist the decision/checkpoint, and continue or stop.

A deterministic failure creates a verified finding; reproducibility is a separate measured property. External findings receive a reproducible label at two of three successful replays. The reference demo must achieve three of three before final minimization. The exact success message is `Race condition verified — reproduced 3/3 and minimized to 2 actors.` on `/findings/{findingId}`.

Selecting **Verify Fix** replays the immutable artifact against fixed target mode and presents the vulnerable/fixed comparison on the same Finding page. Navigation and refresh rehydrate PostgreSQL state and resume SSE from an event cursor.

## Concurrency and Minimization Algorithms

The scheduler uses bounded channels plus global, target, and experiment concurrency limiters. Initial strategies are simultaneous start, seeded jitter, and checkpoint interleaving. Fixed seeds make client-side scheduling reproducible; the product reports replay rates because external server scheduling remains outside its control.

Failure minimization is verified delta reduction. It attempts to remove actors, scenario steps, and timing complexity in a deterministic priority order. Gemini may rank candidates, but a candidate is accepted only after deterministic replay preserves the same invariant violation. The reference target exposes controlled checkpoints for reliable demo reproduction without weakening the external-target truth model.

## Failure Handling and Safety

The run lifecycle is Draft → Planning → AwaitingApproval → Queued → Running → Reproducing → Minimizing → Completed, with explicit Failed and Cancelled outcomes. Database constraints guard message idempotency. Workers use renewable leases and persisted attempt boundaries; duplicate delivery observes the lease or resumes only after expiry. Poison messages reach a dead-letter subscription and appear in UI operations state.

Gemini schema failure receives one constrained repair attempt. During execution, a transient model failure may choose a documented deterministic fallback strategy, but it may never create or change a finding. Unsafe target operations are not retried without an idempotency mechanism.

Target responses are untrusted data. The model receives only sanitized evidence and cannot invoke tools directly. HTTPS, authorization acknowledgement, hostname allowlists, destination revalidation, redirect restrictions, metadata/private-range blocking, quotas, Secret Manager references, and redaction protect manual targets. The public sandbox is limited to 10 actors, 40 target requests, 5 Gemini calls, and 90 seconds; the authenticated engine supports 100 logical actors.

## Testing and Delivery

Implementation is divided into five vertical phases: walking skeleton/cloud smoke; deterministic manual hunt; Gemini/Pub/Sub autonomous campaign; minimization/replay/product experience; and hardening/submission. Tests cover domain rules, Testcontainers PostgreSQL, API integration, Pub/Sub emulation, Gemini schemas, concurrency invariants, architecture boundaries, Playwright journeys, Docker Compose, SSRF/redaction, and deployed staging behavior.

The automated golden path resets one inventory unit, generates and approves a plan, finds overselling, minimizes to two actors, replays the violation three of three times, enables fixed mode, and replays without violation. The live unedited demo targets 3:55: problem and plan by 0:55; autonomous execution by 1:50; evidence/minimization by 2:35; vulnerable/fixed replay by 3:15; architecture and Cloud proof by 3:45; close by 3:55.

## Scope Boundaries

OpenAPI import, browser concurrency, arbitrary public targets, source-code race detection, exhaustive formal verification, non-HTTP protocols, multi-tenancy, CI pull-request comments, additional invariant families, and additional Google AI models are excluded from the critical path. Bonus content and social posts follow only after the core product, documentation, and live demo are stable.

## Rationale

The chosen design makes the agent consequential but not authoritative over correctness. It visibly performs a difficult multi-step workflow in the background, persists and recovers long-running state, isolates tools and credentials, and produces evidence a judge can understand immediately. Docker and Terraform make local, conventional staging, and Google Cloud deployments reproducible without changing application images.
