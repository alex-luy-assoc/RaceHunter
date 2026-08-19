---
version: next
status: planned
priority: high
complexity: 4
linked_tasks: [deploy-racehunter-staging-and-complete-submission]
created: 2026-08-19
---

# RaceHunter Google Cloud Staging Validation

Promote the archived RaceHunter MVP into a reproducible non-production Google Cloud staging environment, prove live Gemini, Cloud Run, Pub/Sub, Cloud SQL, least-privilege IAM, and workload identity, then complete an independently timed golden-path smoke test and unedited sub-four-minute demo. The release must preserve distinct, explicit approvals before any credential use, billable-resource creation, or deployment; retain secret-safe, environment-qualified evidence; and close the submission checklist without converting local or emulated proof into deployed claims.

**Complexity rationale**: inferred by `/ala:brainstorm`; this is system-wide Level 4 work spanning release automation, Terraform state and plan security, immutable image publication, Google Cloud identity and cost controls, three deployed services, asynchronous and database integrations, live model verification, browser acceptance, timed demo evidence, and submission governance.
