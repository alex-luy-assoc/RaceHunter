# RaceHunter

RaceHunter is an autonomous concurrency-correctness tester for HTTP/JSON APIs. It turns a business rule into a bounded campaign, records causal evidence, and keeps deterministic invariant evaluation outside the model.

## Implemented foundation and deterministic hunt

The foundation contains .NET 10 Clean Architecture projects, a React shell served by the API, EF Core/Npgsql migrations, a controlled vulnerable/fixed inventory target, three non-root Docker images, Docker Compose portability, and Terraform for the approved Cloud Run/Pub/Sub/Cloud SQL/Secret Manager architecture.

The Phase 2 deterministic engine adds actor/request/duration/concurrency budgets; simultaneous-start, seeded-jitter, and controlled-checkpoint schedules; numeric-boundary, uniqueness/cardinality, and cross-observation evaluators; ordered correlated trace evidence; durable cursor-based progress; and cancellation polling below the two-second requirement. Gemini and Pub/Sub orchestration remain Phase 3 work—the private worker currently exposes an explicit manual inventory-hunt endpoint for deterministic local verification.

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
npm run build --prefix src/RaceHunter.Web
docker compose config --quiet
```

The PostgreSQL integration and reference-target tests use Testcontainers and require Docker.

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

The demo reset endpoint is disabled when `DemoControl:Key` is absent. Compose supplies the development-only `X-Demo-Control-Key: local-demo-only`; staging receives a generated key through a least-privilege Secret Manager reference and stores no key value in the repository.

### Google Cloud

Terraform under `deploy/terraform` preserves the same three-image architecture and provisions Cloud Run, Pub/Sub, Cloud SQL, Secret Manager, IAM, logging/trace APIs, and an optional budget. Do not run `deploy/scripts/deploy.ps1` without explicit approval: it rejects execution unless `-ApproveBillableResources` is supplied.

Terraform and deployed smoke validation were not run during Phase 1 because Terraform is unavailable locally and creating/contacting Google Cloud was outside the approved scope.
