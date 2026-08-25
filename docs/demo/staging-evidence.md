# RaceHunter Staging Evidence

## Qualification boundary

- Environment: Google Cloud project `racehunter-staging`, region `us-east1`.
- Evidence closeout commit: `211ed6e240f316f6b0b5f45d41641054cc71a8b3`.
- Foundation binding: `7567c87d2fd730cb00555ad2b4d786b73adc90d6f3f71c165b29d98d4ce2b399`.
- Immutable images:
  - API: `racehunter-api@sha256:41c153e581c1f07a3a3c05f7d99f86b42557925424d89b16219b91c3beac65ee`
  - Worker: `racehunter-worker@sha256:aa0a63568d3f90abcaf79fcfdaaf789b768d322fa2d11a7d24a678f85f50a43f`
  - Reference target: `racehunter-reference-target@sha256:41cc7b4b2088db0dbea31b3ecd3e8211199fd2e0f9554c4352322bca9a302114`
- Final reviewed Terraform plan SHA-256: `06a110998b7af8fb8fbb7f7edaf3452592189129a178bc6e41521ba10e05deae`.
- Raw Terraform state, plans, traces, videos, credentials, tokens, secret material, and sensitive provider output remain gitignored under `memory-bank/.local/staging-release/`.

## Deployment and route validation

The exact approved saved plan applied successfully. Its sanitized review recorded 0 creates, 3 in-place updates, 0 replacements, 0 deletes, and 55 no-ops. The API and reference-target changes were provider-only volume-order normalization; the worker also changed to the immutable digest above. Deletion protection remained enabled.

The post-deploy application-route validation completed with the expected public/private boundary:

- API `GET /api/capabilities`: HTTP 200.
- Worker `GET /internal/replays`: HTTP 403 without credentials.
- Reference target `GET /api/inventory`: HTTP 403 without credentials.
- Internal Cloud Run startup probes remain `/healthz`; external validation avoids Cloud Run's reserved `z`-suffixed route behavior.

The deployment and route assertions were executed only through their exact hash-bound approvals. No user-managed service-account key was created.

## Automated staging smoke

Evidence class: `deployed-staging` and `live-gemini`.

- Completed UTC: `2026-08-25T17:31:48.6762167Z`.
- Elapsed: 1.8 seconds for the approved existing-finding completion boundary.
- Hunt: `08071bfc-ada1-4517-b595-b234f1fd24f4`.
- Run: `d8e4e344-8bd9-4436-8454-587c6f37dc96`.
- Finding: `12c04495-2d5c-4693-b089-5bdb53a55854`.
- API revision: `racehunter-api-00001-gqb`.
- Immutable finding artifact fingerprint: `sha256:10efcf70891b6d075a82beb906a45aa737821ad3ed371879f1873b741bc383ef`.
- Durable result SHA-256: `71606756652c06453cf05acc31fc298d2501cfe2f577d2ab6196252e5893a352`.

The bound smoke journey proved the live plan, one durable run, deterministic 3/3 violation, two-actor minimization, vulnerable `Fail`, fixed `Pass`, unchanged artifact fingerprint, private-service denial, and correlated Cloud Proof. Earlier read-only diagnostics and sanitized logs established the live model invocation and worker/target execution path without committing raw model or log content.

## Browser recovery evidence and remaining demo gap

Evidence class: recovery evidence only; it does not satisfy the final `timed-staging-demo` criterion by itself.

- Completed UTC: `2026-08-25T18:25:58.788Z`.
- Completion recording elapsed: 4.5 seconds.
- Hunt: `162877ca-0f11-4efb-a61c-69bf05713ca0`.
- Run: `80b413e0-d747-4144-9c33-62258fccee9d`.
- Finding: `f9ca8dd2-d7f6-c3dc-0eb9-18da3adf2681`.
- Video count: exactly 1 in the final completion evidence directory.
- Video SHA-256: `8e0fed2c177f70b4e0b72e4da6540007f5965999657e286862481a4889a5ed57`.
- Demo result SHA-256: `2b99d8281232ca12e95033b2a711c1fd9d66e7e42b695c16217e1a76c9a79563`.
- Release state SHA-256: `79fd5f94fc554d5d7ad2e9c0ebf6986b217d29560e8b5c8152d695dc95f82b1c`.
- Runner result SHA-256: `3ccd2af7b64757385dfacc2e1f0f394889cb15b3462c79aa19335ec86561de13`.

The preceding unedited browser attempt created the hunt and received HTTP 202 for this exact run, then stopped locally when SPA navigation made the response body unavailable. Its retained trace conclusively records `Location: /api/runs/80b413e0-d747-4144-9c33-62258fccee9d` and the subsequent successful run-page navigation. The separately approved completion recording reused that exact hunt/run, created no hunt, plan, or run, reached the verified finding, completed the idempotency-keyed Verify Fix replay, and produced the one final completion video. The two retained recordings are not merged or represented as one uninterrupted take.

AC-VERIFY-6 remains open: the submission still needs one fresh, uninterrupted, unedited recording that begins at New Hunt and continues through Generate Plan, Approve & Run, Finding, and Verify Fix in under four minutes. The 4.5-second completion video cannot prove that full journey, and no further demo execution was authorized during this closeout.

## Local and supply-chain verification

- Final recovery RED reproduced the navigation/body race; GREEN eagerly buffers the response body when it arrives.
- Local Playwright golden-path and recovery suite: 4/4 passed.
- Architecture contracts: 57/57 passed.
- `git diff --check`: passed.
- Earlier immutable-candidate qualification passed the configured .NET/web builds, lint, local/emulated golden path, image builds, dependency audits, repository secret scan, Compose validation, and pinned Terraform 1.14.4 formatting, backend-free initialization, and validation gates.
- Independent recovery review: passed with no blocking finding.

## Evidence integrity

The committed report contains only sanitized identifiers, hashes, statuses, counts, and outcomes. It contains no access token, authorization header, cookie, database URL/password, demo-control key, secret payload, personal data, or raw Terraform/provider state. Ignored raw artifacts remain the authority for the cited hashes.
