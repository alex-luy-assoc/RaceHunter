# Security policy

## Supported version

Security fixes are applied to the latest commit on `main`. This hackathon
project is a non-production reference implementation; do not point it at a
system you do not own or have explicit authorization to test.

## Report a vulnerability

Please report a security vulnerability privately through GitHub Security
Advisories for `alex-luy-assoc/RaceHunter`. Do not open a public issue with
credentials, exploit details, target URLs, personal data, or production logs.

Include the affected commit, a minimal reproduction, impact, and suggested
remediation if known. You should receive an acknowledgement within seven days.

## Trust boundaries

- Gemini proposes only schema-constrained plans and allowlisted strategy
  changes. Deterministic code owns budgets, invariant evaluation, finding
  promotion, minimization, and replay.
- Manual targets are opt-in, owner-scoped, HTTPS-only outside Development,
  DNS/IP validated, redirect-blocked, proxy-bypassed, request-budgeted, and
  restricted to explicit operations and templates.
- No service-account keys are created. Cloud Run calls use workload identity
  and exact-audience OIDC; secrets are referenced by exact Secret Manager
  versions and are never stored in Git or Terraform variables.
- Raw Terraform state, plans, credentials, traces, videos, and staging evidence
  stay outside Git. Run `scripts/audit-public-release.ps1` before publication.

See [the security model](README.md#security-model) for the public architecture
summary and `docs/architecture/security-auth.md` for implementation detail.
