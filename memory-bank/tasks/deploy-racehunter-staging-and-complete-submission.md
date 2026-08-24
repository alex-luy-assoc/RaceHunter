---
slug: deploy-racehunter-staging-and-complete-submission
feature: racehunter-google-cloud-staging-validation
status: IN PROGRESS
---

# deploy-racehunter-staging-and-complete-submission: Deploy RaceHunter Staging and Complete Submission

**Complexity**: Level 4
**Status**: IN PROGRESS
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
- [x] `deploy/scripts/StagingRelease.psm1` — approval, state-machine, plan binding, redaction, and evidence helpers.
- [x] `deploy/scripts/staging-release.ps1` — resumable local/preflight/foundation/plan/deploy/validate/demo orchestration entry point.
- [x] `deploy/scripts/staging-evidence.schema.json` — versioned machine-readable sanitized evidence contract.
- [ ] `deploy/terraform/bootstrap/providers.tf` — minimal provider and local-to-remote state bootstrap contract.
- [ ] `deploy/terraform/bootstrap/variables.tf` — project, region, repository, and protected state-bucket inputs.
- [ ] `deploy/terraform/bootstrap/main.tf` — required APIs, Artifact Registry, and private versioned Terraform state bucket.
- [ ] `deploy/terraform/bootstrap/outputs.tf` — non-secret foundation identifiers consumed by the release orchestrator.
- [x] `tests/RaceHunter.Architecture.Tests/StagingReleaseContractTests.cs` — executable approval, evidence, plan, and infrastructure contracts.
- [ ] `docs/demo/staging-evidence.md` — sanitized environment-qualified submission evidence report populated only from observed results.

### Phases
- [x] Phase 1: Build the approval-gated release state machine and sanitized evidence contract. ✓

  **Completed**: 2026-08-19
  **Test Results**: 14/14 focused staging-release contract tests and 241/241 full-suite tests passing across 11 suites.
  **Build/Lint**: .NET and web builds PASS; web lint PASS.
  **Code Review**: APPROVED after two RED→GREEN remediation loops; code security PASS; 65 NuGet packages audited with no vulnerable packages or upgrades required.
  **Evidence Boundary**: Contract-validated local implementation only. No credential use, Google API call, billable mutation, image publication, Terraform apply, deployment, staging smoke, demo, cleanup, or destruction occurred.
- [x] Phase 2: Separate and harden foundation, protected Terraform state, immutable image publication, deployment planning, and IAM/topology validation. ✓

  **Completed**: 2026-08-19
  **Test Results**: 12/12 focused Phase 2 contracts, 38/38 architecture tests, 245/245 full .NET tests, and 8/8 web tests passing.
  **Build/Lint**: .NET and web builds PASS; web lint, `git diff --check`, and pinned Terraform 1.14.4 formatting/init-without-backend/validation PASS for both roots.
  **Code Review**: APPROVED after two RED→GREEN remediation loops; code security PASS; provider locks reviewed locally with no application dependency change.
  **Evidence Boundary**: Contract-validated local implementation only. No Google credential use, authenticated API call, billable mutation, image publication, state migration, Terraform plan/apply, deployment, staging smoke, demo, cleanup, or destruction occurred.
- [x] Phase 3: Qualify the immutable release candidate locally and stop at the credential-use gate with a complete preflight request. ✓

  **Completed**: 2026-08-19
  **Test Results**: 8/8 focused Phase 3 contracts, 46/46 architecture tests, and 261/261 full-suite tests passing across 11 suites.
  **Build/Lint**: .NET and web builds PASS; web lint and `git diff --check` PASS.
  **Code Review**: APPROVED after two review-remediation loops and one post-commit qualification-regression loop; code security PASS; 230 resolved dependency/version pairs audited with no vulnerable packages or upgrades required.
  **Evidence Boundary**: The immutable candidate can now be qualified locally from a clean committed checkout and emits an exact, default-deny Preflight request. No Google credential use, authenticated API call, billable mutation, image publication, state migration, Terraform plan/apply, deployment, staging smoke, demo, cleanup, or destruction occurred.
- [ ] Phase 4: After fresh credential, billable-resource, and deployment approvals, prepare and deploy staging and prove live Gemini plus Google Cloud integrations.

  **Recovery checkpoint (2026-08-24)**: Foundation remains complete and the three immutable images remain reusable because their Docker build inputs have not changed. The first application apply partially created 40 managed resources before Cloud SQL rejected `db-f1-micro` under the PostgreSQL 17 `ENTERPRISE_PLUS` default and Billing Budgets rejected a user access token without an explicit quota project. After those defects were corrected, the exact recovery apply exposed a second ordering defect: the reference-target Cloud Run service began before its `latest` database secret version existed. Read-only reconciliation now records 51 Terraform addresses, including both SQL stacks, both database secret versions, the budget, and the reference-target service, without state locking or mutation. RED→GREEN contracts require every Cloud Run service to depend explicitly on each generated secret version it consumes; the corrected Terraform passes the 14 focused deployment contracts, all 49 architecture contracts, and pinned Terraform formatting, isolated initialization, and validation. Every stale saved plan and Plan/Deploy approval remains invalid; recovery must create a new commit-bound exact plan gate without repeating Foundation provisioning or image publication.
- [ ] Phase 5: Run the golden-path smoke, record a separate unedited sub-four-minute demo, reconcile environment-qualified evidence, and complete the submission checklist.

## Creative Phases

- [x] Architecture design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`
- [x] User Journey design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`
- [x] Security / approval-flow design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`
- [x] Verification / evidence design → completed in `memory-bank/creative/deploy-racehunter-staging-and-complete-submission-design.md`

---

## Execution State

**Build Status**: IN_PROGRESS
**Current Build**: Phase 4: Recover the partial staging deployment, rebuild the exact application plan binding, and deploy only after fresh approval (deploy-racehunter-staging-and-complete-submission)
**Build Started**: 2026-08-23T14:00:00Z
**Last Completed**: Phase 3: Qualify the immutable release candidate locally and stop at the credential-use gate with a complete preflight request
**Phase Number**: 4 of 5
**Is Multi-Phase**: YES
**Can Resume**: YES
**PLAN BACKEND**: anthropic — configured
**BRAINSTORM CRITIQUE**: skipped — codex unavailable (unresolved:no-companion, glob=∅; `C:\Users\alexa\.claude\plugins` absent)
**TDD BACKEND**: anthropic — configured
**CODE REVIEW BACKEND**: anthropic — codex unavailable (`unresolved:no-companion`; `C:\Users\alexa\.claude\plugins` absent); auto fallback

### Current Build Step
**Step**: Step 11 - Recovery Git Completion
**Status**: IN_PROGRESS
**Started**: 2026-08-24T12:41:35Z
**Completed**: —
**Output**: Second partial apply reconciled read-only to 51 addresses; Cloud Run secret-version ordering contract delivered RED→GREEN; pinned Terraform format/init/validate, 14/14 focused deployment contracts, and 49/49 architecture contracts pass. Commit-bound release checkpoint regeneration remains.

### Completed Steps
- Step 3 Recovery TDD (secret ordering): COMPLETE (2026-08-24T16:05:00Z) - RED reproduced the missing Cloud Run-to-secret-version edge; explicit dependencies for database, demo-control, and OTel versions delivered GREEN for API, worker, and reference target.
- Step 7 Recovery Verification (secret ordering): COMPLETE (2026-08-24T16:08:00Z) - Focused Phase Five contracts 14/14 PASS; full architecture contracts 49/49 PASS; pinned Terraform 1.14.4 fmt, isolated init without backend, and validate PASS.
- Step 8 Recovery Review (secret ordering): COMPLETE (2026-08-24T16:09:00Z) - Minimal ordering-only diff reviewed; no credential, IAM, API, image, state, or direct cloud operation added.
- Step 9 Recovery Documentation (secret ordering): COMPLETE (2026-08-24T16:09:00Z) - Durable Phase 4 recovery and resumption notes updated for the 51-address reconciliation checkpoint.
- Step 3 Recovery TDD: COMPLETE (2026-08-24T12:41:35Z) - RED contracts reproduced the Cloud SQL edition/tier and Billing Budgets quota-project failures; explicit `ENTERPRISE` and provider user-project routing delivered GREEN.
- Step 7 Recovery Verification: COMPLETE (2026-08-24T13:07:56Z) - Pinned Terraform fmt/init/validate PASS; focused architecture recovery suite 14/14 PASS; container architecture suite 47/48 with only the Windows-only command-shim contract excluded on Linux.
- Step 8 Recovery Review: COMPLETE (2026-08-24T13:07:56Z) - Diff review PASS; no credential, IAM, API, image, state, or cloud mutation added by the correction.
- Step 9 Recovery Documentation: COMPLETE (2026-08-24T13:07:56Z) - README and durable Phase 4 recovery checkpoint updated.
- Step 10 Recovery Memory Bank: COMPLETE (2026-08-24T13:07:56Z) - Stale Plan/Deploy bindings remain invalid and resumption is bound to a fresh corrected commit.
- Step 0.5 Git Setup: COMPLETE (2026-08-19T18:01:49Z) - Existing clean worktree verified on the feature branch.
- Step 0.6 Phase Gate: COMPLETE (2026-08-19T18:01:49Z) - Taxonomy clean, five-phase roadmap populated, and all creative references verified.
- Step 1 Read Task Context: COMPLETE (2026-08-19T18:01:49Z) - Phase 3 of 5 selected.
- Step 2 Load Complexity Context: COMPLETE (2026-08-19T18:01:49Z) - Level 4 rules and the credential-use stop boundary loaded.
- Step 3 TDD Agent: COMPLETE (2026-08-19T18:56:00Z) - 8 focused contracts delivered RED→GREEN across the initial build, two review-remediation loops, and one qualification-regression loop.
- Step 5 Create Test Batches: COMPLETE (2026-08-19T18:38:00Z) - One local-qualification/credential-boundary batch and one sequential execution group.
- Step 6 Execute Test Batches: COMPLETE (2026-08-19T18:56:00Z) - 8/8 focused Phase 3 tests passing after four batch iterations; no batch-worker fixes required.
- Step 7 Integration Verification: COMPLETE (2026-08-19T18:57:00Z) - 261/261 tests passing across 11 suites; .NET/web builds, web lint, and diff check PASS.
- Step 8 Code Reviewer: COMPLETE (2026-08-19T18:58:00Z) - APPROVED after the post-qualification regression review; security PASS; no dependency upgrades.
- Step 9 Documentation Agent: COMPLETE (2026-08-19T18:46:00Z) - README, techContext, and systemPatterns updated; C4 not configured.
- Step 10 Update Memory Bank: COMPLETE (2026-08-19T18:47:23Z) - Phase 3 checkbox and durable execution state updated.
- Step 11 Phase Git Completion: COMPLETE (2026-08-19T18:58:01Z) - Phase 3 artifacts and durable state prepared after the Windows command-shim qualification regression was fixed and reverified.
- Step 0.5 Git Setup: COMPLETE (2026-08-19T16:44:59Z) - Existing clean worktree verified on the feature branch.
- Step 0.6 Phase Gate: COMPLETE (2026-08-19T16:44:59Z) - Taxonomy clean, five-phase roadmap populated, and all creative references verified.
- Step 1 Read Task Context: COMPLETE (2026-08-19T16:44:59Z) - Phase 2 of 5 selected.
- Step 2 Load Complexity Context: COMPLETE (2026-08-19T16:44:59Z) - Level 4 rules and approved phase boundaries loaded.
- Step 3 TDD Agent: COMPLETE (2026-08-19T17:30:00Z) - 12 focused contracts delivered RED→GREEN across the initial build and two review-remediation loops.
- Step 5 Create Test Batches: COMPLETE (2026-08-19T17:30:00Z) - One staging foundation/topology batch and one sequential execution group.
- Step 6 Execute Test Batches: COMPLETE (2026-08-19T17:34:00Z) - 38/38 architecture tests and all pinned Terraform checks passing; no batch fixes required.
- Step 7 Integration Verification: COMPLETE (2026-08-19T17:36:00Z) - 245/245 .NET and 8/8 web tests passing; builds, lint, and Terraform validation PASS.
- Step 8 Code Reviewer: COMPLETE (2026-08-19T17:39:00Z) - APPROVED after two remediation loops; security PASS; no blocking findings remain.
- Step 9 Documentation Agent: COMPLETE (2026-08-19T17:42:00Z) - README, architecture context, techContext, and systemPatterns updated.
- Step 10 Update Memory Bank: COMPLETE (2026-08-19T17:43:08Z) - Phase 2 checkbox and durable execution state updated.
- Step 11 Phase Git Completion: COMPLETE (2026-08-19T17:43:08Z) - Phase 2 artifacts and durable state prepared for guard validation and push.
- Step 0.5 Git Setup: COMPLETE (2026-08-19T15:48:45Z) - Existing clean worktree verified on the feature branch.
- Step 0.6 Phase Gate: COMPLETE (2026-08-19T15:48:45Z) - Taxonomy clean, five-phase roadmap populated, and all creative references verified.
- Step 1 Read Task Context: COMPLETE (2026-08-19T15:48:45Z) - Phase 1 of 5 selected.
- Step 2 Load Complexity Context: COMPLETE (2026-08-19T15:48:45Z) - Level 4 rules loaded.
- Step 3 TDD Agent: COMPLETE (2026-08-19T16:17:00Z) - 14 focused tests delivered RED→GREEN across the initial build and two review-remediation loops.
- Step 5 Create Test Batches: COMPLETE (2026-08-19T16:17:00Z) - One staging-release contract batch and one sequential execution group.
- Step 6 Execute Test Batches: COMPLETE (2026-08-19T16:20:00Z) - 14/14 focused tests passing; no batch fixes required after final remediation.
- Step 7 Integration Verification: COMPLETE (2026-08-19T16:21:00Z) - 241/241 tests passing across 11 suites; .NET/web builds and web lint PASS.
- Step 8 Code Reviewer: COMPLETE (2026-08-19T16:22:00Z) - APPROVED after two remediation loops; security PASS; no dependency upgrades.
- Step 9 Documentation Agent: COMPLETE (2026-08-19T16:23:00Z) - Inline rationale, README, techContext, and systemPatterns updated; C4 not configured.
- Step 10 Update Memory Bank: COMPLETE (2026-08-19T16:23:55Z) - Phase 1 checkbox and durable execution state updated.
- Step 11 Phase Git Completion: COMPLETE (2026-08-19T16:23:55Z) - Phase 1 commit prepared for guard validation and push.
- Full design approved by the user on 2026-08-19.
- Feature, task scaffold, and creative design prepared on the feature branch.
- PLAN BACKEND: anthropic — configured.
- Taxonomy lint: CLEAN (15 canonical NFR acceptance criteria).
- Concrete NFR specification gate: PASS.
- Glossary: not built; proceeded without naming-conventions context.
- BRAINSTORM CRITIQUE: skipped — codex unavailable (unresolved:no-companion, glob=∅; `C:\Users\alexa\.claude\plugins` absent).

### Sub-Agents
- TDD Agent (Phase 4 recovery): COMPLETE (2026-08-24T12:41:35Z) - Cloud SQL edition and quota-project contracts delivered RED→GREEN; partial apply reconciled to 41 tracked addresses and 18 absent managed addresses.
- TDD Agent (Phase 3): COMPLETE (2026-08-19T18:56:00Z) - 8 focused contracts; credential-free local qualification, isolated child environment, exact fresh Preflight binding, and Windows command-shim execution delivered RED→GREEN.
- Batch Test Agent (Phase 3): COMPLETE (2026-08-19T18:56:00Z) - 8/8 focused tests PASS over four iterations; no batch-worker fixes required.
- Build Verifier Agent (Phase 3): COMPLETE (2026-08-19T18:57:00Z) - 261/261 tests; .NET/web builds, web lint, and diff check PASS.
- Code Reviewer Agent (Phase 3): COMPLETE (2026-08-19T18:58:00Z) - APPROVED after final qualification-regression review; security PASS; no blocking findings or dependency upgrades.
- Documentation Agent (Phase 3): COMPLETE (2026-08-19T18:46:00Z) - README, techContext, and systemPatterns updated; no product/API/observability/C4 update required.
- TDD Agent (Phase 2): COMPLETE (2026-08-19T17:30:00Z) - 12 focused contracts; protected bootstrap, exact plan binding, mandatory budget controls, database isolation, and IAM/topology implementation delivered RED→GREEN.
- Batch Test Agent (Phase 2): COMPLETE (2026-08-19T17:34:00Z) - 38/38 architecture tests and 6/6 Terraform module checks PASS; no fixes required.
- Build Verifier Agent (Phase 2): COMPLETE (2026-08-19T17:36:00Z) - 245/245 .NET and 8/8 web tests; builds, lint, and Terraform checks PASS.
- Code Reviewer Agent (Phase 2): COMPLETE (2026-08-19T17:39:00Z) - APPROVED after two remediation loops; security PASS; no blocking findings.
- Documentation Agent (Phase 2): COMPLETE (2026-08-19T17:42:00Z) - README, system context, techContext, and systemPatterns updated.
- Build Git Setup Agent: COMPLETE (2026-08-19T15:48:45Z) - Reused the clean active feature-branch worktree without disrupting the checkout.
- TDD Agent: COMPLETE (2026-08-19T16:17:00Z) - 14 tests; 1 test file; 3 source files; RED→GREEN preserved through two review-fix iterations.
- Batch Test Agent: COMPLETE (2026-08-19T16:20:00Z) - Final affected batch 14/14 PASS; build/type check PASS.
- Build Verifier Agent: COMPLETE (2026-08-19T16:21:00Z) - 241/241 tests; build PASS; lint PASS.
- Code Reviewer Agent: COMPLETE (2026-08-19T16:22:00Z) - APPROVED; 0 blocking/recommended/optional findings; security PASS.
- Documentation Agent: COMPLETE (2026-08-19T16:23:00Z) - Updated inline documentation, README, techContext, and systemPatterns.

### Guard & Recovery Log
- 2026-08-24: The second exact apply stopped without retry after Cloud Run evaluated a `latest` secret before Terraform had created its version. Read-only reconciliation found 51 addresses and no inspection mutation. All three Cloud Run resources now explicitly depend on every generated Secret Manager version they consume, preventing parallel creation from racing database, demo-control, or OTel materialization.
- 2026-08-24: The live generated bootstrap `backend.gcs.tf` exposed a pre-existing worktree-coupled contract. The test now uses an isolated temporary bootstrap copy, preserving the real post-Foundation operator file while verifying default-deny materialization behavior.
- 2026-08-24: Full offline solution verification was blocked by NuGet repository-signature network access and incomplete sandbox package probing. The official .NET 10 SDK container built the architecture suite; 47/48 passed, with only the Windows-only command-shim contract inapplicable on Linux. The exact recovery subset passed 14/14.

### Resumption Notes
**Can Resume**: YES
**Resume From**: Phase 4 recovery via `/ala:build deploy-racehunter-staging-and-complete-submission`
**Notes**: Foundation and immutable image publication are complete and must not be repeated. The second partial application apply was reconciled read-only to 51 Terraform addresses. The tracked secret-ordering correction invalidates every prior recovery commit binding, saved plan, and Plan/Deploy approval; resume from a fresh commit-bound local qualification and exact Plan gate. No authenticated cloud action is authorized until that request is approved.

## Plan Critique

- **Backend**: Codex companion requested by the default `creative-critique` seam.
- **Outcome**: skipped — `unresolved:no-companion` (`C:\Users\alexa\.claude\plugins` absent; glob result empty).
- **Verdict**: not run; the configured skip outcome does not block `/ala:brainstorm` finalization.
- **Findings**: none produced; zero applied and zero noted.
