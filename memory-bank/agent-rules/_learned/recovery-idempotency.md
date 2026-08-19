---
topics: [recovery, idempotency, workers, http, budgets]
globs: ["src/RaceHunter.Worker/**", "src/RaceHunter.Infrastructure/Persistence/**", "src/RaceHunter.Infrastructure/Security/**"]
priority: low
auto_generated: true
derived_from: [build-racehunter-hackathon-mvp]
evidence_count: 1
last_validated: 2026-08-18
---

# Receiver-Aware Recovery and Physical Request Accounting

- Reserve the physical request budget and durable operation claim before transport, not after a response.
- Validate lease ownership inside the transaction that commits checkpoints or terminal outcomes; a prior ownership read is insufficient.
- Retry an ambiguous mutation only when the receiver explicitly guarantees keyed idempotency for the operation. Otherwise fail closed with manual recovery guidance.
- Bound every composed receiver key after all scope, actor, step, and artifact segments are added.
