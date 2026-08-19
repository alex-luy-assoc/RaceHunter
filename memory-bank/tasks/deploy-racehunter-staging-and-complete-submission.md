---
slug: deploy-racehunter-staging-and-complete-submission
feature: racehunter-google-cloud-staging-validation
status: PLANNING_COMPLETE
---

# deploy-racehunter-staging-and-complete-submission: Deploy RaceHunter Staging and Complete Submission

**Complexity**: Level 4
**Status**: PLANNING_COMPLETE
**Roadmap**: racehunter-google-cloud-staging-validation
**Branch**: feature/deploy-racehunter-staging-and-complete-submission
**Worktree**: C:/Users/alexa/source/repos/RaceHunter

## Task Description

Promote the archived, locally verified RaceHunter MVP into a reproducible non-production Google Cloud staging environment and close the remaining environment-qualified submission proof. Build a resumable evidence-first release workflow around the existing Terraform, three Dockerfiles, deployment script, deployed smoke script, demo script, and submission checklist. The workflow must prove live Gemini planning, public/private Cloud Run boundaries, authenticated Pub/Sub delivery, Cloud SQL persistence, least-privilege IAM, exact-audience workload identity, the complete golden-path smoke, and a separate unedited sub-four-minute staging demo.

This task is approval-gated by design. Approval of this plan is not authorization to use credentials, create billable resources, publish images, provision infrastructure, deploy revisions, run staging smoke, or destroy resources. During build, the operator must obtain fresh, explicit approval immediately before each applicable stage, bound to the exact Google Cloud project, region, inputs, and action. Credential approval, billable-resource approval, and deployment approval are independent and non-transitive.

The archived MVP is the implementation baseline. Local, emulated, contract-validated, cloud-read-only, deployed-staging, live-Gemini, and timed-demo evidence remain distinct. Raw Terraform state, saved plans, credentials, secret values, database connection strings, demo-control keys, tokens, and sensitive cloud output must remain outside Git; committed evidence is sanitized and traceable to UTC timestamps, commit and image identity, environment, observed outcomes, and non-secret resource/run/revision identifiers.

## Specification

**Feature Type**: NFR/Infrastructure
**Primary Persona**: A backend developer responsible for a transactional HTTP API

### Verification Method

**Test method**: Implement and exercise the staged release contract through `deploy/scripts/staging-release.ps1` and `deploy/scripts/StagingRelease.psm1`; run focused gate and evidence tests in `tests/RaceHunter.Architecture.Tests/StagingReleaseContractTests.cs` plus the existing topology contracts in `tests/RaceHunter.Architecture.Tests/PhaseFiveDeploymentContractTests.cs`; run the archived credential-free release baseline with `dotnet test RaceHunter.slnx -c Release --no-restore`, `npm test --prefix src/RaceHunter.Web`, `npm run lint --prefix src/RaceHunter.Web`, `npm run build --prefix src/RaceHunter.Web`, `scripts/run-real-playwright.ps1`, and the pinned `hashicorp/terraform:1.14.4` `fmt -check -diff`, `init -backend=false`, and `validate` commands documented in `README.md`; after fresh stage-specific approvals, run read-only Google Cloud preflight, publish the three immutable images, review a saved Terraform plan, apply that exact plan, run `deploy/scripts/smoke.ps1` against staging, and execute a separate fresh browser run following `docs/demo/demo-script.md`.

**Success metrics**: Zero authenticated Google API calls before the credential-use gate; zero API enablements, bucket/repository creation, image pushes, or other billable mutations before the billable-resource gate; zero Terraform applies or Cloud Run revisions before the deployment gate; zero accepted stale approvals after commit, project, region, image digest, Terraform input, or saved-plan hash drift; zero credentials or secret-bearing values in committed artifacts; three immutable `@sha256:` application images; three healthy Cloud Run revisions with only the API invocable by `allUsers`; real `gemini-3.5-flash` output specific to the oversell objective with persisted `plan-v1` model/schema evidence; one logical worker execution despite duplicate Pub/Sub delivery; Cloud SQL-backed run/finding/replay state intact after asynchronous processing and browser refresh; exact least-privilege IAM and keyless, exact-audience API-to-worker, Pub/Sub-to-worker, and worker-to-target authentication; 3/3 failed reproductions, a two-actor artifact, vulnerable `Fail`, fixed `Pass`, and one unchanged artifact fingerprint; automated staging smoke below 240 seconds; a separate unedited staging demo below 240 seconds; and every checked item in `docs/demo/submission-checklist.md` linked to evidence from the environment it claims.

**Observable at**: The gitignored release state and raw artifacts under `memory-bank/.local/staging-release/`; the sanitized committed report at `docs/demo/staging-evidence.md`; the saved-plan hash and non-secret Terraform outputs from `deploy/terraform/outputs.tf`; Cloud Run revision and IAM policy descriptions, Pub/Sub subscription delivery evidence, Cloud SQL identifiers, Cloud Logging and Cloud Trace correlations; `GET /api/cloud-proof?runId=<run-id>` and the Finding page Cloud proof, Agent Activity, minimized schedule, causal timeline, and Replay comparison regions; the terminal result from `deploy/scripts/smoke.ps1`; the timed recording metadata; and evidence links beside each item in `docs/demo/submission-checklist.md`.

**Verification frequency**: Run the credential-free baseline once for every release-candidate commit; revalidate the approval binding before every external stage and every resume; generate and review a new saved plan whenever any bound input changes; verify deployed topology and integrations after every approved apply; and run the automated smoke plus the independent unedited demo once against the final staging revisions selected for submission.

### Acceptance Criteria

#### AC-VERIFY-1: Credentialed preflight is default-denied and read-only
**Priority**: MUST

**Given** a locally qualified release candidate with no credential-use approval bound to its exact staging project, region, and `Preflight` stage
**When** the operator invokes the preflight stage of `deploy/scripts/staging-release.ps1`
**Then** the command exits before reading Google authentication, obtaining a token, or contacting an authenticated Google API; after a fresh matching approval it may inspect only the active principal, project, billing link, quotas, permissions, region availability, and existing resources, writes a `cloud-read-only` sanitized result, performs no mutation, and stops before the billable-resource gate.

#### AC-VERIFY-2: Billable preparation requires its own bounded approval
**Priority**: MUST

**Given** a successful credential-approved read-only preflight and no billable-resource approval for the exact project, region, foundation inputs, cost ceilings, and release-candidate commit
**When** the operator requests API enablement, the protected state bucket, Artifact Registry, or image publication through the staging release workflow
**Then** the workflow exits before the first mutation; credential approval is not accepted as billable approval, and only a fresh matching billable-resource approval permits the declared APIs, a private versioned public-access-prevented state bucket, and Artifact Registry to be reconciled and the API, worker, and reference-target images to be published and resolved to immutable `@sha256:` digests.

#### AC-VERIFY-3: Deployment applies only the reviewed saved plan
**Priority**: MUST

**Given** three published immutable image digests and a saved Terraform plan whose sanitized summary, exact project, region, variables, commit SHA, input hash, and plan hash have been recorded, but no deployment approval exists for that binding
**When** the operator invokes the deployment stage
**Then** the sanitized review shows resource additions, changes, replacements, and deletions plus public/private exposure, scale and budget controls, and deletion protection; no `terraform apply` or Cloud Run revision is created until a fresh deployment approval matches the recorded binding; billable approval is not accepted in its place; and the approved stage applies that saved plan exactly once without regenerating it.

#### AC-ERROR-1: Input drift invalidates downstream approval
**Priority**: MUST

**Given** an approval exists for a release stage and its recorded commit SHA, Google Cloud project, region, image digests, Terraform variables, input hash, and saved-plan hash
**When** any bound value differs from the current invocation or the saved plan cannot be verified byte-for-byte
**Then** the workflow rejects the stage before external mutation, marks downstream approvals invalid in the release state, preserves prior evidence, and requires regeneration, review, and fresh approval rather than silently updating the binding.

#### AC-VERIFY-4: Local qualification remains credential-free and reproducible
**Priority**: MUST

**Given** a clean checkout of the release-candidate commit with no Google credentials made available to the workflow
**When** the local qualification stage runs the .NET, Vitest, lint, web build, fresh-volume real Playwright, image-build, dependency-audit, repository-secret-scan, Compose, and pinned Terraform validation gates
**Then** every configured gate passes using only local or emulated dependencies, all three application images are buildable, results are labeled `local` or `local-emulated`, and no result is represented as cloud-read-only or deployed proof.

#### AC-VERIFY-5: Committed evidence is environment-qualified and secret-safe
**Priority**: MUST

**Given** raw state, plan, cloud, smoke, log, trace, and demo artifacts may contain sensitive generated values
**When** the workflow writes or promotes an evidence record
**Then** raw material remains under gitignored, access-restricted `memory-bank/.local/staging-release/`; committed evidence identifies its class as `local`, `local-emulated`, `cloud-read-only`, `deployed-staging`, `live-gemini`, or `timed-staging-demo` and records UTC time, environment, method, expected and observed result, commit/image identity, safe resource or run identifiers, and artifact reference; and redaction checks reject credentials, tokens, database URLs or passwords, demo-control keys, secret payloads, authorization headers, cookies, and sensitive responses before `docs/demo/staging-evidence.md` or checklist changes can be committed.

#### AC-INTEGRATION-1: Staging uses live Gemini planning
**Priority**: MUST

**Given** the approved staging worker runs under its workload identity with Vertex AI access and the objective `Successful orders must not exceed available inventory.`
**When** the API requests a plan and the worker completes planning
**Then** Vertex AI is actually invoked with model ID `gemini-3.5-flash`; the returned validated plan is specific to the supplied oversell objective rather than a development fake or placeholder; and the persisted plan, Agent Activity, and `GET /api/cloud-proof?runId=<run-id>` expose the non-empty model invocation ID, `plan-v1` schema version, plan version, and run correlation as `live-gemini` evidence without persisting raw sensitive model content.

#### AC-INTEGRATION-2: Cloud Run exposure and service health match the approved topology
**Priority**: MUST

**Given** the reviewed Terraform plan has been approved and applied in the named non-production staging environment
**When** the workflow inspects the deployed `racehunter-api`, `racehunter-worker`, and `racehunter-reference-target` services and calls their known `/healthz` routes
**Then** all three selected revisions are healthy and use the approved immutable digests; the API returns HTTP 200 without caller credentials; unauthenticated worker and reference-target calls return authoritative HTTP 401 or 403; and IAM contains exactly one `allUsers` `roles/run.invoker` grant, on the API only.

#### AC-INTEGRATION-3: IAM and workload identities are least-privilege and exact-audience
**Priority**: MUST

**Given** the deployed service accounts, Pub/Sub push identity, IAM policies, Secret Manager bindings, and configured worker and target audiences
**When** topology assertions inspect IAM and authenticated API-to-worker, Pub/Sub-to-worker, and worker-to-target calls are correlated in Cloud Logging or Cloud Trace
**Then** API, worker, target, and Pub/Sub use their dedicated service accounts without user-managed service-account keys; only the API and Pub/Sub push identities can invoke the worker; only the worker can invoke the reference target; the worker alone has Vertex AI use; each secret accessor binding is resource-scoped as declared by Terraform; every ID token uses the exact destination Cloud Run URI as audience; and wrong-audience or unauthenticated requests are denied.

#### AC-ASYNC-1: Pub/Sub dispatch produces one durable logical execution
**Priority**: MUST

**Given** an approved hunt whose run intent and `RunRequested` outbox record are persisted in Cloud SQL
**When** Pub/Sub pushes the authenticated work message to the private worker and an equivalent delivery is observed more than once
**Then** the worker authenticates the Pub/Sub OIDC identity, processes one logical execution through the durable inbox and lease boundary, surfaces any duplicate delivery as correlated non-secret operational evidence, and produces no second logical run, model budget spend, finding, or replay artifact for the same message identity.

#### AC-INTEGRATION-4: Cloud SQL persists the asynchronous journey across refresh
**Priority**: MUST

**Given** the public API has accepted a hunt and the private worker is processing it asynchronously in staging
**When** the browser is closed or refreshed after planning, during campaign progress, and after Verify Fix
**Then** `GET /api/runs/<run-id>`, paged run events, finding evidence, Agent Activity, trace references, the immutable replay artifact, and vulnerable/fixed replay attempts rehydrate from Cloud SQL with the same identifiers and fingerprint, and the browser is not required for execution to continue.

#### AC-INTEGRATION-5: Automated staging smoke proves the full golden path
**Priority**: MUST

**Given** the final deployed revisions are healthy and the operator has separately approved staging smoke for their exact API, worker, and reference-target URLs
**When** `deploy/scripts/smoke.ps1 -ApiBaseUrl <api-url> -WorkerUrl <worker-url> -ReferenceTargetUrl <target-url> -ApproveStagingSmoke` runs with a timeout below 240 seconds
**Then** it starts from the plain-language oversell rule, obtains a live plan, submits one run approval, observes a verified 3/3 finding minimized to two actors, executes vulnerable `Fail` and fixed `Pass` replays with the same fingerprint, validates private-service denial and Cloud Proof, finishes below 240 seconds, and records safe run, finding, revision, trace, model-invocation, and elapsed-time identifiers as `deployed-staging` and `live-gemini` evidence.

#### AC-VERIFY-6: The final demo is fresh, unedited, independent, and under four minutes
**Priority**: MUST

**Given** the automated smoke has passed but its run and timing are not accepted as demo evidence, and the operator has separately confirmed execution of the staging demo
**When** a fresh browser session follows `docs/demo/demo-script.md` from New Hunt through Generate Plan, one Approve & Run action, Live Campaign, Finding, Verify Fix, and Cloud proof while an independent timer records the run
**Then** the captured demonstration is one unedited take below 240 seconds, uses a fresh run rather than the smoke result, visibly shows persisted live Gemini model/schema evidence, 3/3 reproduction, two actors, the causal timeline, one unchanged fingerprint, vulnerable `Fail`, fixed `Pass`, and non-secret Cloud Run, Pub/Sub, Cloud SQL, OIDC, run, revision, and trace proof, and is recorded as `timed-staging-demo` evidence.

#### AC-VERIFY-7: Submission checklist closes only from qualified evidence
**Priority**: MUST

**Given** local qualification, deployed validation, automated smoke, and timed-demo evidence have been collected
**When** `docs/demo/submission-checklist.md` is reconciled with `docs/demo/staging-evidence.md`
**Then** every checked line links to the exact environment-qualified evidence that proves it, local or emulated observations never satisfy deployed claims, smoke evidence never substitutes for the independent demo, unsupported items remain unchecked with their blocker stated, and the final checklist reports zero committed secrets or unsupported reproducibility claims.

#### AC-ERROR-2: Failure recovery stops safely at the current boundary
**Priority**: MUST

**Given** any preflight, foundation, image publication, plan, apply, topology, integration, smoke, timing, or redaction check fails or returns an ambiguous mutation result
**When** the release workflow records the failure and the operator later requests a resume
**Then** it performs read-only inspection before reconciliation, resumes only from verified durable state, and does not automatically broaden IAM, create credentials, retry an ambiguous mutation, regenerate or apply a different plan, destroy resources, disable deletion protection, run smoke, run the demo, or clean up; any changed scope or destructive action requires its own fresh explicit approval.

### Scope Boundaries

In scope are the non-production Google Cloud staging environment; a resumable, approval-gated release state machine; credential-free local qualification; credential-approved read-only cloud preflight; billable-approved API enablement, protected versioned state bucket, Artifact Registry, and immutable image publication; deployment-approved saved-plan apply; the existing three-service Cloud Run topology; Cloud SQL, Pub/Sub, Vertex AI Gemini, Secret Manager, IAM, workload identity, logging/tracing, deployed golden-path smoke, a separate timed demo, secret-safe evidence, and submission-checklist reconciliation.

Out of scope are production or multi-region deployment, Kubernetes, arbitrary external targets, service-account key creation, storing credential or secret values, automatic IAM expansion, automatic ambiguous-mutation retries, automatic plan regeneration, automatic teardown, disabling deletion protection, cleanup without separate approval, editing the demo recording, fabricating or promoting evidence, changing RaceHunter's archived domain/concurrency design, and claiming control of external server scheduling.

Dependencies are Docker, PowerShell, the pinned Terraform container workflow, the existing three Dockerfiles and `deploy/terraform/` module, an operator-supplied non-production Google Cloud project and region, separately approved Google authentication and billing, sufficient quota and permissions discovered by read-only preflight, Vertex AI access to `gemini-3.5-flash`, and the existing `deploy/scripts/smoke.ps1`, `scripts/run-real-playwright.ps1`, Cloud Proof surface, `docs/demo/demo-script.md`, and `docs/demo/submission-checklist.md`.

### Creative Exploration Needed

Completed by the approved `/ala:brainstorm` design in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`. The evidence-first staged release, exact approval boundaries, foundation/application split, immutable plan binding, failure/recovery behavior, evidence classes, live verification journey, and separate smoke/demo proofs are approved with no unresolved LOW-confidence design gate. Build work may refine implementation details without weakening or bypassing those decisions.

### Implementation Guide Required

Yes. Extend the current immutable-digest and explicit-approval patterns from `deploy/scripts/deploy.ps1` and `deploy/scripts/smoke.ps1`; centralize gate evaluation, binding hashes, resumable state, and redaction in `deploy/scripts/StagingRelease.psm1`; expose the staged operator flow through `deploy/scripts/staging-release.ps1`; validate the sanitized manifest with `deploy/scripts/staging-evidence.schema.json`; separate foundation resources under `deploy/terraform/bootstrap/`; extend `deploy/terraform/` without changing the public/private or least-privilege topology; add executable contracts in `tests/RaceHunter.Architecture.Tests/StagingReleaseContractTests.cs` and `tests/RaceHunter.Architecture.Tests/PhaseFiveDeploymentContractTests.cs`; and commit observed, sanitized results only to `docs/demo/staging-evidence.md` and `docs/demo/submission-checklist.md`. Planning approval is documentation approval only and grants no runtime credential use, billable mutation, deployment, staging smoke, demo execution, cleanup, or destruction authority.

## User Journey Definition

**Feature Type**: NFR/Infrastructure
**Creative Phase Required**: Yes - Architecture and User Journey, completed by `/ala:brainstorm`

### NFR Verification (Infrastructure Features)
- **Test method**: run the approval-gate contract tests and local release-candidate suite; after stage-specific approval, run credentialed read-only preflight, foundation preparation, saved-plan review, deployment, `deploy/scripts/smoke.ps1`, and the checked-in unedited browser demo against the named staging project.
- **Success metrics**: zero gate bypasses; zero committed secrets; immutable digests for all three images; only the API is public; authenticated Pub/Sub and service-to-service calls succeed; live `gemini-3.5-flash` evidence is persisted; the automated golden path and a separate unedited demo each complete in under four minutes; every submission checklist item has linked environment-qualified evidence.
- **Observable at**: sanitized staging evidence manifest and report, Terraform outputs, Cloud Run revisions, Cloud Logging/Trace correlations, RaceHunter Cloud Proof and Finding pages, smoke output, timed demo artifact metadata, and `docs/demo/submission-checklist.md`.

## Test Strategy

### Approach
- **Emphasis**: balanced contract, integration, negative-gate, infrastructure validation, deployed smoke, and browser E2E testing. Use deterministic local doubles until the exact cloud stage is approved.
- **Target test count**: 24 focused additions across the five phases, justified by three independent approval boundaries, tamper detection, secret-safe evidence, staged Terraform behavior, and six live integration claims; retain the archived full regression baseline.

### File Organization
- **New test files**: `tests/RaceHunter.Architecture.Tests/StagingReleaseContractTests.cs` for gate, manifest, plan-binding, state-security, and evidence-redaction contracts.
- **Extend existing**: `tests/RaceHunter.Architecture.Tests/PhaseFiveDeploymentContractTests.cs` for Terraform/IAM/Cloud Run/Pub/Sub staging topology; `tests/RaceHunter.AcceptanceTests/tests/golden-path.spec.ts` for environment-qualified deployed assertions only when configured; `deploy/scripts/smoke.ps1` self-validating assertions for live integration evidence.

### What NOT to Test
- Google provider implementation details — validate generated plans, deployed resources, IAM policy, and observed behavior instead.
- Secret values or token contents — prove references, audience, access denial, redaction, and absence from artifacts without exposing material.
- External scheduler determinism — verify measured 3/3 reference-target outcomes and immutable replay identity without claiming control of Cloud Run scheduling.
- Destruction or production rollout — cleanup is a separate approval and production is out of scope.
- The archived domain/concurrency behavior from scratch — retain and run its existing regression and golden-path suites.

### Per-Phase Test Guidance
- Phase 1: 8 tests for default-deny stage gates, non-transitive approvals, project/region binding, manifest transitions, plan/input tampering, and secret redaction.
- Phase 2: 6 tests for staged Terraform foundation/application separation, protected remote state, immutable digests, cost ceilings, deletion protection, and least-privilege topology.
- Phase 3: 4 tests for local qualification orchestration, evidence classification, clean-checkout reproducibility, and credential-free behavior.
- Phase 4: 4 deployed assertions for public/private Cloud Run access, Pub/Sub OIDC, Cloud SQL durability, and exact-audience workload identity/IAM.
- Phase 5: 2 end-to-end journeys for the automated golden-path smoke and the separate unedited timed demo, plus checklist/evidence reconciliation.

## Implementation Roadmap

### New Source Files (pin path + extension)
- [ ] `deploy/scripts/StagingRelease.psm1` — approval, state-machine, plan binding, redaction, and evidence helpers.
- [ ] `deploy/scripts/staging-release.ps1` — resumable local/preflight/foundation/plan/deploy/validate/demo orchestration entry point.
- [ ] `deploy/scripts/staging-evidence.schema.json` — versioned machine-readable sanitized evidence contract.
- [ ] `deploy/terraform/bootstrap/providers.tf` — minimal provider and local-to-remote state bootstrap contract.
- [ ] `deploy/terraform/bootstrap/variables.tf` — project, region, repository, and protected state-bucket inputs.
- [ ] `deploy/terraform/bootstrap/main.tf` — required APIs, Artifact Registry, and private versioned Terraform state bucket.
- [ ] `deploy/terraform/bootstrap/outputs.tf` — non-secret foundation identifiers consumed by the release orchestrator.
- [ ] `tests/RaceHunter.Architecture.Tests/StagingReleaseContractTests.cs` — executable approval, evidence, plan, and infrastructure contracts.
- [ ] `docs/demo/staging-evidence.md` — sanitized environment-qualified submission evidence report populated only from observed results.

### Phases
- [ ] Phase 1: Build the approval-gated release state machine and sanitized evidence contract.
- [ ] Phase 2: Separate and harden foundation, protected Terraform state, immutable image publication, deployment planning, and IAM/topology validation.
- [ ] Phase 3: Qualify the immutable release candidate locally and stop at the credential-use gate with a complete preflight request.
- [ ] Phase 4: After fresh credential, billable-resource, and deployment approvals, prepare and deploy staging and prove live Gemini plus Google Cloud integrations.
- [ ] Phase 5: Run the golden-path smoke, record a separate unedited sub-four-minute demo, reconcile environment-qualified evidence, and complete the submission checklist.

## Creative Phases

- [x] Architecture design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`
- [x] User Journey design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`
- [x] Security / approval-flow design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`
- [x] Verification / evidence design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`

---

## Execution State

**Build Status**: IDLE
**Current Phase**: BUILD
**Last Completed**: Specification, taxonomy, concrete-NFR, glossary, and critique-resolution gates
**Can Resume**: NO
**PLAN BACKEND**: anthropic — configured
**BRAINSTORM CRITIQUE**: skipped — codex unavailable (unresolved:no-companion, glob=∅; `C:\Users\alexa\.claude\plugins` absent)

### Active Sub-Agents
(none)

### Completed Steps
- Full design approved by the user on 2026-08-19.
- Feature, task scaffold, and creative design prepared on the feature branch.
- PLAN BACKEND: anthropic — configured.
- Taxonomy lint: CLEAN (15 canonical NFR acceptance criteria).
- Concrete NFR specification gate: PASS.
- Glossary: not built; proceeded without naming-conventions context.
- BRAINSTORM CRITIQUE: skipped — codex unavailable (unresolved:no-companion, glob=∅; `C:\Users\alexa\.claude\plugins` absent).

## Plan Critique

- **Backend**: Codex companion requested by the default `creative-critique` seam.
- **Outcome**: skipped — `unresolved:no-companion` (`C:\Users\alexa\.claude\plugins` absent; glob result empty).
- **Verdict**: not run; the configured skip outcome does not block `/ala:brainstorm` finalization.
- **Findings**: none produced; zero applied and zero noted.
