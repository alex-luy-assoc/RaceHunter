# Archive: RaceHunter Hackathon MVP

## Metadata

- Task: `build-racehunter-hackathon-mvp`
- Roadmap feature: `racehunter-autonomous-concurrency-campaign`
- Complexity: Level 4
- Started: 2026-08-18
- Completed: 2026-08-18
- Duration: one build day; observable implementation commits span 11:52–19:09 EDT, followed by reflection and archive
- Branch: `feature/build-racehunter-hackathon-mvp`
- Integration target: `main`
- Integration status: local archive complete; configured push/PR pending because no `origin` remote exists

## Executive Summary

RaceHunter is a locally verified hackathon MVP for finding transactional race conditions with bounded autonomous campaigns. A developer states a correctness rule, reviews one structured Gemini plan, approves once, and observes a durable asynchronous workflow that schedules concurrent target calls, evaluates deterministic invariants, reproduces a violation, minimizes it, and creates an immutable replay artifact.

The branch delivers a .NET 10 modular monolith, React UI, PostgreSQL system of record, private worker boundary, vulnerable/fixed reference target, Pub/Sub-compatible recovery, a `Google.GenAI` Vertex adapter, safe authenticated HTTP/JSON targets, OpenTelemetry, Docker Compose, Terraform staging infrastructure, and judge-facing demo material. The final local record reports 233 passing checks: 219 .NET, 8 Vitest, 4 mocked Playwright, and 2 real Docker-backed Playwright journeys. Independent review reached SPEC COMPLIANCE PASS.

Environment proof remains explicitly qualified. Terraform was validated but not applied; no live Vertex call, Cloud Run deployment, deployed Pub/Sub/Cloud SQL journey, or staging-timed demo was authorized or executed. Those are pre-submission follow-ups, not claims made by this archive.

## System Overview

### Purpose

RaceHunter turns a backend correctness rule into reproducible causal evidence. Its central product boundary is that Gemini may plan and propose bounded adaptations, while deterministic code owns safety, budgets, execution, invariant evaluation, finding promotion, minimization acceptance, and replay verification.

### Included Scope

- Public inventory sandbox with hard actor, request, model-call, retry, and duration ceilings.
- Authenticated, owner-bound manual HTTP/JSON targets with allowlisted destinations, secret references, redaction, typed observations, and receiver-aware recovery.
- Simultaneous-start, seeded-jitter, and controlled-checkpoint schedules.
- Numeric-boundary, cardinality, and cross-observation invariants.
- Durable planning, approval, campaign, reproduction, minimization, cancellation, failure, and replay state.
- Exact reference proof: 3/3 reproduction, two-actor minimization, and vulnerable `Fail` versus fixed `Pass` for one immutable artifact.
- Local Docker portability and reproducible Google Cloud staging definitions.

### Excluded or Deferred Scope

- OpenAPI import, arbitrary public targets, browser concurrency, formal verification, non-HTTP protocols, Kubernetes, multi-region deployment, billing, and additional model/invariant families.
- Live Vertex AI and deployed Google Cloud evidence until an owner explicitly authorizes credentials, billable resources, and external execution.
- Fully asynchronous Verify Fix dispatch; the MVP uses a bounded private-worker HTTP call with a 30-second boundary.

## Architecture

### Overview

The system is a modular monolith packaged as three application images:

1. `RaceHunter.Api` serves the React application, public contracts, commands, queries, SSE, authorization, and outbox publication.
2. `RaceHunter.Worker` receives authenticated Pub/Sub-compatible work and performs planning, target execution, deterministic evaluation, recovery, reproduction, minimization, and replay.
3. `RaceHunter.ReferenceTarget` exposes deliberately vulnerable and fixed inventory modes plus protected control operations.

PostgreSQL is authoritative for workflow and evidence state. The same application images run under Docker Compose and are referenced by Terraform for Cloud Run. See `docs/architecture/system-context.md` for the maintained system-context view.

### Component Relationships

```text
React -> RaceHunter.Api -> PostgreSQL
                     \-> Outbox/PubSub -> RaceHunter.Worker
Gemini/Vertex AI <----------------------/
Target APIs <---------------------------/
ReferenceTarget -> isolated PostgreSQL database
```

Clean Architecture boundaries separate Domain, Application, Contracts, Concurrency, Gemini, Infrastructure, API, Worker, ReferenceTarget, and Web responsibilities. Architecture tests enforce the dependency direction.

### Durable Data Flow

1. The API persists a hunt draft and a planning outbox message.
2. The worker validates a structured plan and persists the versioned proposal.
3. Exact-version approval creates one queued run and idempotent work item.
4. The worker acquires a lease, executes bounded schedules, appends trace evidence, evaluates invariants, and checkpoints decisions before continuing.
5. A deterministic failure enters measured reproduction and verified minimization.
6. A content-addressed artifact binds target, scenario, invariant, actor steps, seed, and offsets.
7. Verify Fix reuses the unchanged artifact and stores a separate replay attempt.
8. The UI rehydrates PostgreSQL state and resumes SSE after the last acknowledged cursor.

### Integration Points

- Vertex AI through the official `Google.GenAI` .NET SDK and workload identity.
- Pub/Sub push with versioned envelopes, inbox leases, heartbeats, checkpoints, retry classification, and dead-letter projection.
- Cloud SQL PostgreSQL for authoritative workflow state and a separate reference-target database.
- Secret Manager references for manual target credentials; raw values are not accepted or persisted.
- Cloud Run identity tokens with exact service audiences for private service calls.
- OpenTelemetry logs, metrics, and traces with W3C context propagation.

## Design Decisions

### Bounded Campaign Rather Than Demo-Only or Broad Fuzzer

- Decision: combine a reliable reference journey with a deliberately constrained authenticated HTTP/JSON target contract.
- Rationale: preserves hackathon demo reliability while demonstrating operational utility beyond one hard-coded scenario.
- Trade-off: broad API ingestion and arbitrary public execution remain out of scope.
- Reference: `memory-bank/creative/build-racehunter-hackathon-mvp-design.md`.

### Deterministic Truth With Advisory Model Agency

- Decision: Gemini produces schema-constrained plans and allowlisted strategy actions but cannot create, remove, or alter a finding.
- Rationale: correctness evidence must be replayable and machine-checkable.
- Trade-off: model-authored failure analysis was deliberately omitted; grounded summaries remain deterministic and trace-linked.

### Receiver-Aware Recovery

- Decision: reserve physical work before transport and retry ambiguous mutation only when the receiver declares keyed idempotency.
- Rationale: a stable sender header does not prove receiver outcome after a crash.
- Trade-off: non-idempotent ambiguity stops with `manual_recovery_required` instead of automatic progress.

### Content-Addressed Replay

- Decision: canonicalize and fingerprint immutable target, scenario, invariant, and schedule inputs, then revalidate the fingerprint around execution.
- Rationale: vulnerable/fixed comparison is meaningful only if it reuses the same artifact.
- Trade-off: stricter versioning and key-scope constraints increase persistence and validation complexity.

### PostgreSQL as Recovery Boundary

- Decision: keep runs, events, traces, leases, inbox/outbox state, agent iterations, findings, probes, artifacts, replay attempts, manual setup claims, and audits in PostgreSQL.
- Rationale: campaigns must survive navigation, redelivery, worker loss, and process restart.
- Trade-off: some phase-oriented persistence modules are large and should be split by durable responsibility during maintenance.

## Implementation

### Phases

| Phase | Outcome |
|---|---|
| 1. Walking skeleton and portability | Clean Architecture solution, PostgreSQL foundation, vulnerable/fixed target, three images, Compose, and Terraform base |
| 2. Deterministic manual hunt | Bounded scheduler, three invariant families, ordered evidence, cancellation, and durable run progress |
| 3. Gemini and autonomous campaign | Structured planning, one-time approval, Pub/Sub-compatible dispatch, leases, checkpoints, retries, dead letters, and SSE recovery |
| 4. Findings, minimization, and replay | Measured 3/3 reproduction, verified two-actor reduction, immutable artifacts, causal timeline, and vulnerable/fixed browser proof |
| 5. Security, observability, and submission | Owner-bound manual targets, SSRF/redaction/secret safety, receiver-aware recovery, telemetry, IAM/Terraform, and demo package |

### Key Components

- `ConcurrencyScheduler` and schedule strategies provide deterministic bounded actor execution.
- `InvariantEvaluatorRegistry` dispatches numeric, cardinality, and cross-observation evaluators.
- `CampaignRunner` owns the bounded plan-execute-observe-adapt lifecycle and deterministic finding workflow.
- `WorkInboxStore`, guarded checkpoints, and `WorkDispatcher` implement lease-safe redelivery.
- Replay artifacts and finding probe checkpoints preserve restartable evidence identity.
- The target safety stack enforces owner authorization, destination validation and pinning, redirect denial, secret references, redaction, typed observations, budgets, and recovery policy.
- React pages expose New Hunt, Plan Review, Live Campaign, Finding & Replay, Agent Activity, actor lanes, and Cloud Proof.

### Planned-File Reconciliation

The original plan contained 25 stale unchecked file entries. Archive reconciled every one in the task file: responsibilities implemented under consolidated or renamed boundaries are marked complete with their final location, `TelemetryRegistration.cs` is marked at its exact path, and deliberately omitted files explain the design reason. No compatibility shell files were created merely to satisfy an obsolete filename plan.

## Testing and Verification

### Strategy

The build used test-first phase gates with fixed clocks, IDs, seeds, controlled checkpoints, deterministic model fakes, PostgreSQL integration tests, architecture tests, API tests, browser journeys, Docker smoke tests, and infrastructure-contract validation. Adversarial reviews followed green gates and drove recovery, authorization, lifecycle, semantic-validation, collision, and evidence-projection remediations.

### Final Recorded Results

| Verification class | Result | Environment |
|---|---:|---|
| .NET tests | 219/219 passing | local/test containers |
| Vitest | 8/8 passing | local |
| Mocked Playwright | 4/4 passing | local browser |
| Real Docker-backed Playwright | 2/2 passing | fresh local Compose volumes |
| .NET and Vite builds | warning-free/passing | local |
| Format and TypeScript lint | passing | local |
| Docker images and health | three images passing | local Compose |
| Terraform 1.14.4 | fmt/init-without-backend/validate passing | official local container; no apply |
| Dependency and credential scans | passing | local |
| Independent final review | SPEC COMPLIANCE PASS | repository evidence |

No coverage percentage was recorded, so this archive does not invent one. During reflection, the branch independently reran all 219 .NET tests and all 8 Vitest tests successfully.

### Environment-Qualified Gaps

- Live `gemini-3.5-flash` plan and strategy calls: not run.
- Cloud Run, Pub/Sub, Cloud SQL, IAM, and workload-identity smoke: not run.
- Unedited deployed demo under four minutes: not run.
- Staging load test for the bounded synchronous Verify Fix worker call: not run.

## Deployment and Operations

### Local Procedure

Follow `README.md` to provide explicit development configuration, start Docker Compose, apply local migrations through application startup, and run the golden journey. The reference target uses isolated storage and protected demo controls. Test scripts use scoped resources and remove temporary containers and volumes after verification.

### Staging Procedure

`deploy/scripts/deploy.ps1` and `deploy/terraform/` define build/push/apply orchestration, three Cloud Run services, Cloud SQL, Pub/Sub, Secret Manager, IAM, logging/tracing, instance ceilings, and cost controls. Deployment requires explicit owner approval, authenticated Google Cloud tooling, immutable image digests, and reviewed Terraform output. `deploy/scripts/smoke.ps1` validates service URLs, private-service denial, the full golden path, and the demo deadline.

### Configuration and Secrets

- Use workload identity in staging; do not store service-account keys.
- Supply credentials only through Secret Manager references.
- Keep public sandbox ceilings at or below 10 actors, 10 concurrent actors, 40 requests, 5 model calls, 90 seconds, and one retry.
- Treat target templates, responses, redirects, DNS answers, and configured JSON paths as untrusted input.

### Rollback

- Roll Cloud Run services back to the last known immutable image revisions.
- Do not rewrite findings or replay artifacts; preserve evidence and create new attempts.
- Keep database migrations forward-compatible and back up Cloud SQL before staging schema changes.
- Pause Pub/Sub delivery or scale the private worker to zero before recovery when a deployment is unsafe.
- Re-run the vulnerable/fixed replay smoke after restoring service revisions.

## Maintenance Guide

### Monitor

- Work inbox lease expiry, retry, and dead-letter rates.
- Run lifecycle latency and stuck Planning/Running/Reproducing/Minimizing states.
- Target request budgets, cancellation latency, model-call budgets, and global concurrency ceilings.
- Safety-authorization failures, SSRF blocks, redaction events, and `manual_recovery_required` outcomes.
- Replay fingerprint mismatches and reproduction/minimization probe recovery.
- Cloud Run revisions, authenticated Pub/Sub delivery, Vertex model/schema version, and Cloud SQL connectivity.

### Common Issues

| Issue | Resolution |
|---|---|
| Worker loses a lease | Cancel stale work; allow conditional takeover only after expiry and resume from the latest persisted boundary |
| Duplicate delivery | Return the persisted terminal result or observe the active lease; never start a second logical execution |
| Ambiguous manual mutation | Retry only with declared receiver-keyed idempotency; otherwise stop with manual recovery guidance |
| Unsafe destination or redirect | Block before transport, persist a categorized audit record, and correct the authorized target definition |
| Invalid Gemini output | Use one constrained repair attempt, then persist explicit model failure or bounded deterministic fallback where allowed |
| Replay fingerprint mismatch | Reject execution and investigate version/snapshot mutation; do not rewrite the artifact |
| UI reconnect | Reload PostgreSQL state, then resume SSE from the last acknowledged cursor |
| Cloud smoke failure | Classify IAM/audience, Pub/Sub, Cloud SQL, model, or target failure separately; retain sanitized evidence |

### Accepted Technical Debt

- Trace evidence and its progress/checkpoint reference may commit separately, leaving queryable but temporarily unreferenced evidence after a narrow crash.
- Verify Fix uses bounded private HTTP dispatch rather than durable asynchronous replay work.
- Phase-oriented persistence files should be split by responsibility when the system enters sustained maintenance.

## Lessons Learned

- Persisted deterministic evidence must precede finding promotion; model output remains advisory.
- Physical request budgets and durable operation claims must be reserved before transport.
- Retry safety depends on receiver guarantees, not sender intent.
- Canonical, versioned, content-addressed evidence makes replay comparisons trustworthy.
- Verification claims must name their environment: local, emulated, contract-validated, or deployed.
- Planned-file inventories need reconciliation states for exact, consolidated, renamed, and deliberately omitted work.

Four additive learned rules were created under `memory-bank/agent-rules/_learned/`. No existing rules were merged, retired, expired, or pruned.

## Future Considerations

1. After explicit approval, deploy immutable image digests and run the full Cloud Run/Pub/Sub/Cloud SQL/IAM smoke.
2. Execute and record real Vertex planning and strategy calls, including repair and budget behavior.
3. Run the unedited sub-four-minute staging demo and finish `docs/demo/submission-checklist.md`.
4. Decide whether to make trace plus progress/checkpoint persistence atomic.
5. Load-test Verify Fix in staging and move it to durable asynchronous dispatch if the 30-second boundary is tight.
6. Generate an acceptance-evidence matrix that keeps local, emulated, contract, and deployed proof distinct.

## References

- Task: `memory-bank/tasks/build-racehunter-hackathon-mvp.md`
- Reflection: `memory-bank/reflection/build-racehunter-hackathon-mvp-reflection.md`
- Creative design: `memory-bank/creative/build-racehunter-hackathon-mvp-design.md`
- System context: `docs/architecture/system-context.md`
- Runtime and deployment guidance: `README.md`
- Demo script: `docs/demo/demo-script.md`
- Submission checklist: `docs/demo/submission-checklist.md`
- API contract: `docs/openapi.json`
- Implementation timeline: `git log main..feature/build-racehunter-hackathon-mvp`

## Level 4 Archive Checkpoint

- System documentation: complete
- Architecture and data flow: complete
- Design decisions and trade-offs: complete
- Implementation phases and components: complete
- Testing results and environment qualifiers: complete
- Deployment, configuration, and rollback: complete
- Maintenance and troubleshooting guidance: complete
- Planned-file inventory reconciliation: complete
- Task and roadmap completion markers: complete locally
- Push and PR to protected `main`: pending because no `origin` remote is configured
