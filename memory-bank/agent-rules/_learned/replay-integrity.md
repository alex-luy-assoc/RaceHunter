---
topics: [replay, evidence, immutability, versioning]
globs: ["src/RaceHunter.Domain/Replays/**", "src/RaceHunter.Concurrency/Replay/**", "src/RaceHunter.Worker/Execution/*Replay*"]
priority: low
auto_generated: true
derived_from: [build-racehunter-hackathon-mvp]
evidence_count: 1
last_validated: 2026-08-18
---

# Content-Addressed Replay Integrity

- Bind replay artifacts to canonicalized, versioned target, scenario, invariant, actor-step, seed, and offset snapshots.
- Compute and persist a content fingerprint, then validate it before and after execution.
- Store replay attempts separately; never rewrite the original finding or artifact to represent a later target mode or outcome.
- Scope recovery keys by run, artifact, candidate, actor, and step so unrelated probes cannot collide.
