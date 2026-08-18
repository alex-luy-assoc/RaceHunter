# RaceHunter

RaceHunter is an autonomous concurrency-correctness tester for HTTP/JSON APIs. It turns a business rule into a bounded campaign, records causal evidence, and keeps deterministic invariant evaluation outside the model.

## Phase 1 walking skeleton

The foundation contains .NET 10 Clean Architecture projects, a React shell served by the API, EF Core/Npgsql migrations, a controlled vulnerable/fixed inventory target, three non-root Docker images, Docker Compose portability, and Terraform for the approved Cloud Run/Pub/Sub/Cloud SQL/Secret Manager architecture.

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

The demo reset endpoint is disabled when `DemoControl:Key` is absent. Compose supplies the development-only `X-Demo-Control-Key: local-demo-only`; staging receives a generated key through a least-privilege Secret Manager reference and stores no key value in the repository.

### Google Cloud

Terraform under `deploy/terraform` preserves the same three-image architecture and provisions Cloud Run, Pub/Sub, Cloud SQL, Secret Manager, IAM, logging/trace APIs, and an optional budget. Do not run `deploy/scripts/deploy.ps1` without explicit approval: it rejects execution unless `-ApproveBillableResources` is supplied.

Terraform and deployed smoke validation were not run during Phase 1 because Terraform is unavailable locally and creating/contacting Google Cloud was outside the approved scope.
