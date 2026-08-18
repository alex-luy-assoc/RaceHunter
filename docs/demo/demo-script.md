# RaceHunter Unedited Demo Script (3:55 target)

Use a pre-provisioned staging environment and a fresh browser session. Do not paste credentials or open cloud consoles containing secrets. Start the timer when narration begins.

| Time | Screen/action | Narration and visible proof |
|---|---|---|
| 0:00–0:25 | Dashboard → **New Hunt** | RaceHunter tests transactional correctness, not throughput. The public sandbox exposes only the authorized inventory target and visible 10 actor / 40 request / 5 Gemini / 90 second budgets. |
| 0:25–0:55 | Enter the oversell rule → **Generate Plan** | Gemini 3.5 Flash proposes a versioned structured plan. Point out the schema/model IDs and that unknown operations or budgets are rejected server-side. |
| 0:55–1:10 | Plan Review → **Approve & Run** once | This is the last human decision. Pub/Sub invokes the private worker; closing the browser does not stop durable execution. |
| 1:10–1:50 | Live Campaign | Show persisted lifecycle events and Agent Activity. Explain that Gemini selects only allowlisted strategy changes while deterministic scheduling, budgets, and invariant checks stay in code. |
| 1:50–2:35 | Finding | Read the exact headline: **Race condition verified — reproduced 3/3 and minimized to 2 actors.** Show deterministic evidence, three measured failures, actor lanes, minimized steps, seed, and replay fingerprint. |
| 2:35–3:15 | **Verify Fix** | Execute the unchanged artifact against fixed mode. Show vulnerable **Fail**, fixed **Pass**, and the matching fingerprint. |
| 3:15–3:45 | Cloud Proof + architecture diagram | Show API revision, private worker, Pub/Sub dispatch, Cloud SQL identifier, Gemini/schema version, OIDC worker authentication, and the three-image diagram. |
| 3:45–3:55 | Close | RaceHunter turns one business rule and one approval into bounded autonomous evidence a backend team can replay against a fix. |

## Before recording

1. Run the local fresh-volume golden path with `./scripts/run-real-playwright.ps1`.
2. With explicit staging approval, run `deploy/scripts/smoke.ps1 -ApiBaseUrl <api-url> -WorkerUrl <worker-url> -ReferenceTargetUrl <target-url> -ApproveStagingSmoke`; it verifies the known `/healthz` routes return authoritative IAM `401/403` denials and fails if the journey exceeds four minutes.
3. Confirm the Finding page has the exact headline, three failed reproductions, two actors, vulnerable Fail, fixed Pass, and Cloud Proof identifiers.
4. Confirm logs and screenshots contain no bearer tokens, cookies, database passwords, demo-control keys, or target response secrets.
5. Keep the architecture diagram and fallback local recording available; do not change the replay artifact or claim deterministic control of external server scheduling.
