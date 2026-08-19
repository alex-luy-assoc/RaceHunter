# Agent Rules Index

Generated: 2026-08-18

## Learned Rules

| Rule | Topics | Priority | Evidence | Applies to |
|---|---|---:|---:|---|
| [`deterministic-evidence`](agent-rules/_learned/deterministic-evidence.md) | concurrency, findings, model safety, evidence | low | 1 | concurrency engine, findings, worker campaign execution |
| [`recovery-idempotency`](agent-rules/_learned/recovery-idempotency.md) | recovery, idempotency, workers, HTTP, budgets | low | 1 | worker, persistence, target safety adapters |
| [`replay-integrity`](agent-rules/_learned/replay-integrity.md) | replay, evidence, immutability, versioning | low | 1 | replay domain, executor, worker replay paths |
| [`workflow-documentation`](agent-rules/_learned/workflow-documentation.md) | Memory Bank, planning, documentation, verification | low | 1 | task, reflection, and archive artifacts |

Retired rules are retained in `_learned/` with `superseded_by` metadata and should not be applied directly. No rules are retired as of this index.
