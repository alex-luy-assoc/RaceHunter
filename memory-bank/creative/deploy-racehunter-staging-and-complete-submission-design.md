# Deploy RaceHunter Staging and Complete Submission — Approved Design

## Context and Objective

The archived RaceHunter MVP is locally and emulation-verified, including Terraform validation, three immutable-image-ready Dockerfiles, private-service IAM contracts, a deployed smoke script, a 3:55 demo script, and a submission checklist. It has not used Google credentials, applied Terraform, called live Gemini, deployed Cloud Run, exercised deployed Pub/Sub/Cloud SQL, or recorded the staging demo. This feature closes those explicit gaps without weakening the project's evidence, safety, cost, or identity boundaries.

## Approaches Considered

### Selected: Evidence-first staged release

Build a resumable release state machine with independent approvals for credential use, billable-resource creation, and deployment. Qualify locally first, perform read-only cloud preflight after credential approval, prepare the billable foundation and immutable images after billable approval, review a bound saved plan, deploy only after deployment approval, and then run live validation and submission evidence capture.

**Why selected**: it gives every risky transition a narrow, auditable boundary; makes failures resumable; preserves environment qualification; and produces stronger judge-facing proof than a monolithic command.

### Rejected: Single guarded deployment command

Collect all approvals and run build, push, provision, deploy, and smoke in one command.

**Trade-off**: fewer commands, but approvals are less legible, failure recovery is coarse, plan review is weaker, and it is harder to prove which inputs were authorized.

### Rejected: Manual console and CLI deployment

Perform each cloud operation interactively and assemble evidence afterward.

**Trade-off**: flexible during troubleshooting, but weakly reproducible, difficult to test, prone to evidence drift, and less credible for an architecture-focused submission.

## Architecture Decision

The release is a staged state machine around existing repository assets. It records commit SHA, project, region, image digests, Terraform input and plan hashes, stage transitions, UTC timestamps, sanitized observations, and evidence references. Raw cloud material remains gitignored and access-restricted. A changed commit, digest, project, region, Terraform variable set, or plan hash invalidates later approval and forces regeneration.

The Terraform foundation is separated from the application release so Artifact Registry and protected remote state can exist before images are published. The bootstrap state contains no application secrets and migrates to a private, versioned Google Cloud Storage backend. Application state and saved plans may contain sensitive generated values and therefore never enter Git; only redacted summaries and hashes are durable project evidence.

The deployed topology remains the approved three-image modular monolith: public API/React, IAM-private worker, and IAM-private reference target; Cloud SQL is authoritative; Pub/Sub push uses a dedicated OIDC identity; the API and worker use workload identities and exact audiences; Gemini runs through Vertex AI under the worker identity. No service-account keys are created or accepted.

## Approval Gates

1. **Credential-use gate** — before any command reads local Google authentication, obtains tokens, or contacts authenticated Google Cloud APIs. Approval is bound to the exact principal-intent, project, region, and read-only preflight stage. It authorizes no mutation.
2. **Billable-resource gate** — before enabling APIs, creating the state bucket or Artifact Registry, pushing images, or creating any chargeable infrastructure. The operator receives the scoped resource/cost-control summary first.
3. **Deployment gate** — after immutable digests exist and a sanitized Terraform plan summary plus plan hash have been reviewed. Approval is bound to that plan and authorizes its application only; input drift invalidates it.

Brainstorm or planning approval never satisfies an execution gate. Gates default denied, are single-purpose and non-transitive, and are recorded without storing credentials. Staging smoke and the live demo also require explicit external-execution confirmation at their point of use. Destruction and cleanup are separate future approvals because deletion protection is intentionally enabled.

## Data and Control Flow

1. Run all credential-free local gates and create the release-candidate commit identity.
2. Build the three images locally and retain local image identity without publishing.
3. Stop and request credential-use approval.
4. Perform read-only identity, project, billing-link, quota, region, permission, and existing-resource inspection; emit a sanitized preflight report and stop.
5. Request billable-resource approval bound to the project, region, foundation summary, scale ceilings, and budget settings.
6. Create/reconcile required APIs, protected state, and Artifact Registry; publish the three images; resolve immutable digests.
7. Generate the application Terraform plan from exact digests; produce a sanitized change/exposure/cost summary and cryptographic plan/input hashes; stop.
8. Request deployment approval bound to the saved plan.
9. Apply the reviewed plan once, capture non-secret outputs and revisions, and verify health before any golden-path traffic.
10. Prove live Gemini, Cloud Run, Pub/Sub, Cloud SQL, IAM, workload identity, logs/traces, and secret-safe evidence.
11. Run the automated golden-path smoke below four minutes.
12. Run a separate fresh, unedited browser demo below four minutes; reconcile evidence and complete the submission checklist.

## Failure and Recovery

- Stop at the current boundary on any unexpected identity, project, permission, quota, billing, plan, deployment, IAM, integration, timing, or redaction result.
- Never broaden IAM, create credentials, regenerate an approved plan, retry ambiguous mutations, destroy resources, or deploy different inputs automatically.
- Inspect and reconcile actual state before resuming. Idempotent stages may reuse verified outputs; changed scope requires a new plan and renewed approval.
- Preserve deployed resources after validation failure for diagnosis unless separately authorized to change or remove them.
- Keep deterministic RaceHunter evidence distinct from Gemini interpretation and retain existing receiver-aware retry and replay-integrity rules during cloud execution.

## Verification and Evidence

Evidence classes are `local`, `local-emulated`, `cloud-read-only`, `deployed-staging`, `live-gemini`, and `timed-staging-demo`. Every claim records the exact method or journey, UTC time, environment, expected and observed result, commit and image identity, safe resource/revision/run identifiers, and artifact reference.

Required deployed proof includes:

- real `gemini-3.5-flash` planning output tied to the submitted objective and persisted model/schema evidence;
- three healthy Cloud Run revisions with unauthenticated worker/target denial and public API success;
- authenticated Pub/Sub push with one logical execution despite at-least-once delivery;
- Cloud SQL persistence across async processing and browser refresh;
- exact least-privilege IAM bindings and keyless workload identities for API, worker, target, and Pub/Sub push;
- exact-audience API-to-worker, Pub/Sub-to-worker, and worker-to-target authentication;
- secret-safe correlated logs/traces and RaceHunter Cloud Proof identifiers;
- 3/3 deterministic finding, two-actor immutable artifact, vulnerable `Fail`, fixed `Pass`, and unchanged fingerprint;
- automated smoke and an independent unedited staging demo, each below four minutes;
- every submission checklist item linked to qualified evidence, with unchecked items left honest.

## Scope Boundaries

In scope: non-production staging only, release automation, minimal Terraform restructuring, protected state, immutable image publication, live integration verification, timed demo, and submission checklist closure.

Out of scope: production deployment, arbitrary external targets, service-account keys, automatic teardown, broad IAM remediation, architecture expansion, feature redesign, fabricated or promoted evidence, editing the demo recording, and cleanup without separate approval.

## Approval Record

The user selected the evidence-first staged approach and explicitly approved the architecture, component/data-flow, approval/failure/evidence, and testing/acceptance sections on 2026-08-19. This approval authorizes production of build-ready planning artifacts only; it does not authorize any external execution gate.
