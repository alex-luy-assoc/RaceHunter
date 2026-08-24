# RaceHunter

RaceHunter is an autonomous concurrency-correctness tester for HTTP/JSON APIs. It turns a business rule into a bounded campaign, records causal evidence, and keeps deterministic invariant evaluation outside the model.

## Implemented local golden path

The foundation contains .NET 10 Clean Architecture projects, a React application served by the API, EF Core/Npgsql migrations, a controlled vulnerable/fixed inventory target, three non-root Docker images, Docker Compose portability, and Terraform for the approved Cloud Run/Pub/Sub/Cloud SQL/Secret Manager architecture.

The implemented workflow now covers asynchronous Gemini planning through the Pub/Sub boundary, bounded deterministic campaigns, durable progress and recovery, measured three-of-three reproduction, exact-schedule minimization to two actors and the minimum failure-preserving steps, and immutable vulnerable-versus-fixed replay. The Finding page keeps deterministic evidence separate from Gemini interpretation and presents the exact verified message, evidence-filtered actor lanes, Agent Activity, replay identity, and Verify Fix comparison. PostgreSQL remains authoritative across refreshes.

Reproduction and minimization persist every deterministic probe boundary in PostgreSQL. Stable artifact-, candidate-, actor-, and step-scoped operation keys let an expired worker reuse completed attempts and reductions without repeating target mutations. An authenticated reference-target status preflight reconciles receiver results with RaceHunter trace correlations before reserving request budget. The Finding page renders the minimized actor, operation, step, and offset schedule as accessible evidence.

### Prerequisites

- .NET SDK 10.0.400
- Node.js 22+
- Docker Desktop with Compose

### Build and test

```powershell
dotnet restore RaceHunter.slnx
dotnet build RaceHunter.slnx --no-restore
dotnet test RaceHunter.slnx --no-build
npm ci --prefix src/RaceHunter.Web
npm test --prefix src/RaceHunter.Web
npm run build --prefix src/RaceHunter.Web
npm ci --prefix tests/RaceHunter.AcceptanceTests
npm test --prefix tests/RaceHunter.AcceptanceTests
docker compose config --quiet
```

The PostgreSQL integration and reference-target tests use Testcontainers and require Docker. The Playwright 1.62.1 suite exercises the New Hunt → Plan Review → Live Campaign → Finding & Replay journey, refresh rehydration, and recoverable Verify Fix failure.

### Run locally

```powershell
docker compose up --build
```

- API and React: `http://localhost:8080`
- Worker health: `http://localhost:8081/healthz`
- Reference target: `http://localhost:8082`

Run a deterministic two-actor vulnerable-target hunt after resetting the sandbox:

```powershell
$headers = @{ 'X-Demo-Control-Key' = 'local-demo-only' }
Invoke-RestMethod -Method Post -Uri 'http://localhost:8082/demo/reset' -Headers $headers -ContentType 'application/json' -Body '{"quantity":1,"mode":"vulnerable"}'
$runId = [guid]::NewGuid()
$body = @{ runId = $runId; actorCount = 2; maxConcurrency = 2; maxRequests = 2; maxDurationSeconds = 30; schedule = 'CheckpointInterleaving'; seed = 4242; maximumSuccessfulOrders = 1 } | ConvertTo-Json
Invoke-RestMethod -Method Post -Uri 'http://localhost:8081/internal/manual-hunts' -ContentType 'application/json' -Body $body
Invoke-RestMethod -Uri "http://localhost:8080/api/runs/$runId"
Invoke-RestMethod -Uri "http://localhost:8080/api/runs/$runId/events?after=0"
Invoke-RestMethod -Uri "http://localhost:8080/api/runs/$runId/traces?after=0"
```

The final response reports deterministic `Fail` evidence when successful orders exceed the configured maximum. `POST /api/runs/{runId}/cancel` persists an idempotent cancellation request; manual execution polls every 200 ms and autonomous Pub/Sub campaigns poll every 250 ms, stopping scheduling and persisting `Cancelled` within the two-second contract.

The full UI journey starts at `http://localhost:8080/hunts/new`. After a verified run, `GET /api/runs/{runId}` exposes its `findingId`; `GET /api/findings/{findingId}` returns the persisted finding projection; and `POST /api/findings/{findingId}/replays` executes the server-owned fixed-target replay with a required idempotency key. The Phase 4 portability gate uses a fresh Compose volume and follows that plan/approve/run/finding/Verify Fix path through the API, worker, reference target, PostgreSQL, and Pub/Sub emulator. The finding and replay subset is documented in `docs/openapi.json`.

Run `./scripts/run-real-playwright.ps1` from PowerShell for the non-mocked browser acceptance journey. It builds an isolated Compose stack, drives the real UI/API/worker/reference-target/PostgreSQL/Pub/Sub path, verifies vulnerable `Fail` versus fixed `Pass`, reloads persisted evidence, and removes its test containers, network, and volumes afterward.

The demo reset endpoint is disabled when `DemoControl:Key` is absent. Compose supplies the development-only `X-Demo-Control-Key: local-demo-only`; staging receives a generated key through a least-privilege Secret Manager reference and stores no key value in the repository.

### Manual targets and safety

The public sandbox does not show manual target configuration. In local Development, the New Hunt page exposes an opt-in admin form; outside Development the capability is absent. Set `ManualTargets__AdminToken` from a local secret source or call `POST /api/admin/targets` with that bearer token. The request accepts an HTTPS base URL, exact host and port allowlist, authorization acknowledgement, stable GET/POST operation IDs, bounded JSON request templates, typed deterministic metric-to-JSON-path observations, optional setup operation, sensitive JSON paths, and a Secret Manager reference shaped like `projects/<project>/secrets/<secret>/versions/<version>`. Templates permit only `actorId`, `runId`, `executionKey`, and `checkpoint` placeholders. It never accepts a credential value. A setup operation must explicitly declare either `receiver-keyed` (the receiver guarantees stable `X-RaceHunter-Idempotency-Key` replay semantics) or `none` (no automatic recovery retry). The UI safely previews substitution and exposes that recovery choice plus numeric-boundary, cardinality, and same-response cross-observation invariants. An authenticated `POST /api/hunts` may then supply the returned `targetId`; that immutable target snapshot constrains planning, execution, finding reads, replay, Verify Fix, cancellation, traces, events, and Cloud Proof. The owner-key fingerprint is persisted, and every manual-resource API returns `401` for a missing session/token and `403` for the wrong one; UUID possession is never authorization.

Manual destination validation rejects user-info URLs, non-HTTPS destinations outside explicit Development hosts, wildcards, metadata hostnames, loopback/private/link-local/multicast/reserved ranges, mixed public/private DNS answers, alternate ports, unapproved methods/paths/templates, and redirects. The safe client bypasses proxies, disables automatic redirects, pins each connection to a freshly validated address, and caps evidence bodies at one MiB. Authorization, cookies, API keys, demo-control keys, and configured JSON fields are redacted before evidence or model use. Number/text observations are extracted only through configured bounded JSON paths. Before setup transport, the worker durably reserves a physical request and setup claim. Completed claims are reused; receiver-keyed ambiguous claims resend the same stable key and retain every physical request in the run budget; a `none` claim becomes `manual_recovery_required` and is never resent automatically. The controlled fixture transactionally stores reset keys and returns the stored outcome without resetting twice. The controlled fixture's failure is derived from shared persisted reservation count after concurrent mutations, never from actor identity, and a finding requires at least two deterministic failures in three equivalent external replays. Its minimized artifact embeds the complete authorized target snapshot and replay refuses a changed snapshot. Configuration, execution, and replay safety failures persist a sanitized category for refresh through `GET /api/admin/audit-events`; manual target metrics record latency and success/failure outcome for every call. For staging, list only the referenced Secret Manager secret IDs in Terraform's `manual_target_secret_ids`; Terraform grants the worker per-secret accessor IAM and never stores secret data.

### Observability

The API, worker, and target emit structured request logs and OpenTelemetry traces/metrics. W3C `traceparent` and `tracestate` span API → Pub/Sub/worker → target/model → finding/replay, with work, run, attempt, actor, step, request, model invocation, finding, and replay-artifact correlations. Metrics cover work, target requests/latency, model calls, invariant outcomes, findings, replays, and cancellation latency. Compose sends no telemetry unless `OTEL_EXPORTER_OTLP_ENDPOINT` is explicitly configured. Terraform supplies a Google-built collector sidecar to each application service and exports application traces to Cloud Trace and metrics to Managed Service for Prometheus. The Finding page Cloud Proof panel queries persisted run, plan, worker revision, model invocation, trace-event, and finding evidence rather than echoing caller input.

### Google Cloud staging

Terraform is split into a protected foundation root at `deploy/terraform/bootstrap` and an application root at `deploy/terraform`. The foundation enables the exact required APIs and creates a private, versioned, retention-protected state bucket plus an Artifact Registry repository with immutable tags. The application root preserves the same three application images and provisions Cloud Run, Pub/Sub/DLQ, Secret Manager, a mandatory budget, hard service scale ceilings, and deletion protection. Primary RaceHunter data and reference-target data use separate Cloud SQL instances, users, passwords, and Secret Manager connection references. Cloud Run templates bind generated secrets to the exact Secret Manager versions Terraform created rather than the floating `latest` alias, so service revisions cannot race secret materialization. Both PostgreSQL 17 instances explicitly use Cloud SQL `ENTERPRISE` so their bounded `db-f1-micro` tier cannot inherit the incompatible `ENTERPRISE_PLUS` default. The Google provider also sends quota and billing attribution through the explicitly bound staging project, including Billing Budgets requests made with a short-lived user access token.

Only the API grants `allUsers`; worker and target keep internet-routable `run.app` ingress so authenticated service-to-service calls work without an absent VPC route, while scoped `run.invoker` IAM still denies unauthenticated callers. No service-account keys are created. Pub/Sub and API use their exact service identities and the worker service URL as the OIDC audience; the worker uses its identity and the exact target service URL for target calls. Staging caps the worker at one instance and one inbound request so its process-wide global/target semaphores are deployment-wide while still supporting 100 logical actors inside a campaign.

The staged-release entry point can safely initialize and inspect its gitignored local state without credentials or Google API access:

```powershell
$commitSha = git rev-parse HEAD
./deploy/scripts/staging-release.ps1 -Stage Initialize -ProjectId racehunter-staging -Region us-east1 -CommitSha $commitSha
./deploy/scripts/staging-release.ps1 -Stage Status -ProjectId racehunter-staging -Region us-east1 -CommitSha $commitSha
```

The project and region in this local record identify a proposed release; they do not authorize cloud access or mutation. Every external stage is default-denied unless supplied a fresh exact-stage approval bound to its release material. The Phase 2 helpers produce explicit image-publication, backend-migration, Terraform-plan, and saved-plan-apply descriptors; they do not execute those actions. Raw release state, `.tfvars.json`, saved plans, generated backend configuration, provider caches, and Terraform state stay outside Git. Only schema-validated, environment-qualified, secret-safe evidence may be promoted later.

After the release-candidate changes have been committed, qualify that exact commit from a clean checkout. `QualifyLocal` checks cleanliness and `HEAD` identity before running the more expensive .NET, web, Playwright, image-build, dependency-audit, secret-scan, Compose, and pinned Terraform gates:

```powershell
# This command must produce no output before qualification starts.
git status --porcelain=v1 --untracked-files=all

$commitSha = git rev-parse HEAD
pwsh -NoLogo -NoProfile -NonInteractive -File .\deploy\scripts\staging-release.ps1 `
  -Stage QualifyLocal `
  -ProjectId 'racehunter-staging' `
  -Region 'us-east1' `
  -CommitSha $commitSha
```

`racehunter-staging` and `us-east1` are safe examples, not fixed deployment settings; the operator must supply the intended non-production staging project and region. Qualification runs subprocesses with an isolated minimal environment, stores only `local` or `local-emulated` secret-safe evidence under `memory-bank/.local/staging-release/`, and emits a `Preflight` request bound to the exact commit, project, region, binding hash, qualification hash, allowed read-only checks, and request hash.

**Stop after `QualifyLocal`.** The generated request is not credential-use approval and authorizes no cloud access or mutation. Do not load Google credentials, obtain tokens, invoke `Preflight`, call authenticated Google APIs, publish images, migrate state, plan/apply Terraform, deploy, smoke-test staging, or run the demo until the applicable fresh approval gate is explicitly satisfied. A preflight approval must bind the unchanged generated request, be issued after qualification, and remain within its 15-minute validity window; any drift requires a new clean qualification and request.

Validate Terraform locally without credentials or state:

```powershell
docker run --rm -v "${PWD}:/workspace" -w /workspace/deploy/terraform hashicorp/terraform:1.14.4 fmt -check -diff -recursive
docker run --rm -v "${PWD}:/workspace" -w /workspace/deploy/terraform/bootstrap hashicorp/terraform:1.14.4 init -backend=false
docker run --rm -v "${PWD}:/workspace" -w /workspace/deploy/terraform/bootstrap hashicorp/terraform:1.14.4 validate
docker run --rm -v "${PWD}:/workspace" -w /workspace/deploy/terraform hashicorp/terraform:1.14.4 init -backend=false
docker run --rm -v "${PWD}:/workspace" -w /workspace/deploy/terraform hashicorp/terraform:1.14.4 validate
```

After fresh Foundation approval and an approved bootstrap apply has created the bucket, materialize `deploy/terraform/bootstrap/backend.gcs.tf` from `backend.gcs.tf.example`. Then use the exact local-to-GCS migration and application `init -reconfigure` arguments emitted by `New-StagingBackendMigrationPlan`; the generated backend file remains gitignored. Do not improvise state moves or apply the application root with local state.

After separately approved publication resolves all three application images to repository-qualified `@sha256:` references, `New-StagingTerraformPlan` writes the exact reviewed variables to a gitignored `.tfvars.json` file and binds its bytes to the Terraform-input hash. A deployment binding adds the reviewed saved-plan hash, and `New-StagingDeploymentPlan` accepts only those unchanged plan bytes; it never regenerates the plan. Collector-image digest resolution requires explicitly authorized network access and is deferred to the later deployment phase.

Do not run `deploy/scripts/deploy.ps1` without explicit approval. It requires `-ApproveBillableResources`, rejects mutable application image tags, validates Terraform, creates a plan, and applies only immutable application `@sha256:` image digests. The deployed golden-path script separately requires the API, worker, and target URLs plus `-ApproveStagingSmoke`; it proves unauthenticated worker/target denial, bounds every request by the remaining deadline, and fails if the complete finding/fix proof exceeds four minutes. Phase 2 performed only local contract generation and validation: it used no credentials and made no Google API calls, image publications, state migrations, Terraform plans or applies, deployments, staging smoke or demo runs, cleanup, or destruction.

The judge narration and release evidence are in `docs/demo/demo-script.md` and `docs/demo/submission-checklist.md`; the current trust, identity, data, and telemetry paths are in `docs/architecture/system-context.md`.
