---
topics: [concurrency, findings, model-safety, evidence]
globs: ["src/RaceHunter.Concurrency/**", "src/RaceHunter.Domain/Findings/**", "src/RaceHunter.Worker/Execution/**"]
priority: low
auto_generated: true
derived_from: [build-racehunter-hackathon-mvp]
evidence_count: 1
last_validated: 2026-08-18
---

# Deterministic Evidence Before Finding Promotion

- Persist the machine-evaluable invariant result and its trace references before promoting a finding.
- Keep model plans, strategy selections, and interpretations advisory; they may guide bounded exploration but cannot create, remove, or alter finding truth.
- Treat inconclusive, budget-exhausted, target-failed, and model-failed outcomes as explicit states rather than defects.
