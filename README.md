# RaceHunter

RaceHunter is an autonomous concurrency-correctness tester for HTTP/JSON APIs. It turns a business rule into a bounded campaign, records causal evidence, and keeps deterministic invariant evaluation outside the model.

## Implemented local golden path

The foundation contains .NET 10 Clean Architecture projects, a React application served by the API, EF Core/Npgsql migrations, a controlled vulnerable/fixed inventory target, three non-root Docker images, Docker Compose portability, and Terraform for the approved Cloud Run/Pub/Sub/Cloud SQL/Secret Manager architecture.

The implemented workflow now covers asynchronous Gemini planning through the Pub/Sub boundary, bounded deterministic campaigns, durable progress and recovery, measured three-of-three reproduction, exact-schedule minimization to two actors and the minimum failure-preserving steps, and immutable vulnerable-versus-fixed replay. The Finding page keeps deterministic evidence separate from Gemini interpretation and presents the exact verified message, evidence-filtered actor lanes, Agent Activity, replay identity, and Verify Fix comparison. PostgreSQL remains authoritative across refreshes.

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

The final response reports deterministic `Fail` evidence when successful orders exceed the configured maximum. `POST /api/runs/{runId}/cancel` persists an idempotent cancellation request; an active manual execution checks durable cancellation every 200 ms and stops new target work.

The full UI journey starts at `http://localhost:8080/hunts/new`. After a verified run, `GET /api/runs/{runId}` exposes its `findingId`; `GET /api/findings/{findingId}` returns the persisted finding projection; and `POST /api/findings/{findingId}/replays` executes the server-owned fixed-target replay with a required idempotency key. The Phase 4 portability gate uses a fresh Compose volume and follows that plan/approve/run/finding/Verify Fix path through the API, worker, reference target, PostgreSQL, and Pub/Sub emulator. The finding and replay subset is documented in `docs/openapi.json`.

The demo reset endpoint is disabled when `DemoControl:Key` is absent. Compose supplies the development-only `X-Demo-Control-Key: local-demo-only`; staging receives a generated key through a least-privilege Secret Manager reference and stores no key value in the repository.

### Google Cloud

Terraform under `deploy/terraform` preserves the same three-image architecture and provisions Cloud Run, Pub/Sub, Cloud SQL, Secret Manager, IAM, logging/trace APIs, and an optional budget. Do not run `deploy/scripts/deploy.ps1` without explicit approval: it rejects execution unless `-ApproveBillableResources` is supplied.

Cloud Run service IAM, API-to-worker identity tokens, OpenTelemetry/Cloud proof, staging rollout, and deployed smoke validation remain Phase 5 work. Creating or contacting billable Google Cloud resources still requires explicit approval.
