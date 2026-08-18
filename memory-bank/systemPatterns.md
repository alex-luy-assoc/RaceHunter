# RaceHunter System Patterns

## Guiding Principles

1. **Evidence before explanation.** Only deterministic invariant evaluation plus trace references can create a finding. Gemini text is interpretation.
2. **Bounded autonomy.** Every campaign has enforced ceilings for actors, concurrency, requests, iterations, duration, retries, Gemini use, and target hosts.
3. **Safe targets only.** Require explicit authorization and allowlisting; validate resolved destinations; block metadata and prohibited ranges; redact credentials and sensitive fields.
4. **Reproducibility is measured.** Record seeds and replay results. Never promise control over an external server's scheduler.
5. **Model-independent core.** Domain rules, scheduling, invariant evaluation, persistence, and replay must function with a deterministic fake planner.
6. **Structured model interaction.** Version all prompts and schemas. Validate outputs and reject unknown endpoints, strategies, or invariant types before execution.
7. **Production-minded simplicity.** Use a modular monolith with explicit boundaries and separate deployables; do not distribute business logic without evidence.
8. **Demo reliability is a product requirement.** The golden path must be automated, resettable, observable, and finish comfortably inside the four-minute limit.

## Architectural Boundaries

- Domain depends on no application, infrastructure, web, API, or worker project.
- Application owns use cases and interfaces; it does not depend on Infrastructure, API, Worker, or UI.
- Infrastructure implements repositories and external adapters without leaking EF Core, Npgsql, Pub/Sub, HTTP, or Gemini provider types through application contracts.
- API and Worker are composition roots using built-in .NET dependency injection.
- Controllers translate HTTP concerns and invoke application use cases; they contain no business logic and never access `DbContext` or repositories directly.
- React components render state and dispatch typed client operations; they do not own orchestration rules.

## SOLID and Dependency Injection

- Separate scenario planning, strategy, scheduling, execution, invariant evaluation, trace persistence, minimization, and explanation.
- Extend strategies, evaluators, adapters, and sinks through focused interfaces and registrations, not central switches or reflection-driven plugins.
- Constructor injection is the default. No service locator and no application/domain resolution from `IServiceProvider`.
- Inject time, IDs, randomness, HTTP/model clients, queues, secrets, and persistence where deterministic tests require control.
- Implementations must honor interface cancellation, idempotency, and error contracts.

## Persistence Patterns

- Use aggregate-specific repositories and dedicated read/query services; never a generic CRUD repository.
- Never expose `DbContext`, `DbSet`, `IQueryable`, or provider-specific types outside Infrastructure.
- No inline SQL in production or tests. Use EF Core LINQ, typed mappings, migrations, and database constraints.
- Use short explicit transactions; never hold one open across HTTP or Gemini calls.
- Use optimistic concurrency for mutable aggregates, UTC `timestamptz` semantics, application-generated UUIDs, and demonstrated indexes.
- Append trace evidence incrementally and page it; never require a full trace in memory.
- Use database uniqueness constraints as the final idempotency enforcement.

## Asynchronous Execution Patterns

- All I/O is async end to end and propagates `CancellationToken`.
- Never use `.Result`, `.Wait()`, `async void`, sync-over-async, or `Task.Run` around I/O.
- Bound all user-controlled work with channels and explicit limiters; never use unbounded `Task.WhenAll`.
- API run creation persists intent and publishes an idempotent message; the worker owns campaign execution.
- Model run lifecycle as a legal state machine. Terminal states are immutable.
- Persist `Running -> Reproducing -> Minimizing` as monotonic run-state transitions with ordered run events before entering each probe phase. Recovery may replay already-checkpointed probe orchestration, but it must not append duplicate phase events or regress a later persisted status; Live Campaign rebuilds the current phase from PostgreSQL before resuming SSE after the durable cursor.
- Use leases or heartbeats for active ownership, persist attempt boundaries/checkpoints, and recover interrupted work explicitly.
- Retry only classified transient failures with bounded exponential backoff and jitter. Never automatically retry unsafe target operations without an idempotency mechanism.
- Dead-letter poison messages and surface them through operations state.
- Persist an attempt checkpoint immediately after deterministic target work and before asking the model. Persist the validated agent decision, run event, and next work checkpoint in one database transaction. Lease recovery may reuse a completed attempt or resume after a decision, but must not spend target or model budgets twice after a durable boundary.
- Acquire an expired work lease with one conditional database update; a prior read never grants ownership. Busy/retry-scheduled push deliveries remain unacknowledged for later recovery. If heartbeat renewal fails or ownership expires, cancel the handler token immediately and reject stale checkpoint, failure, and completion writes.
- Guard the combined agent-decision, run-event, and work-checkpoint transaction with a conditional update of the still-active lease row before staging inserts. Require exactly one affected row and retain that row lock through commit so a stale owner cannot append or overwrite state after takeover.
- Charge every planning provider invocation to a durable work checkpoint before retry, terminal projection, or plan persistence. On redelivery, pass only the remaining model-call allowance to planning; retain completed-plan and terminal-outcome checkpoint state so persistence retries never call the provider again.
- Resolve retries from the persisted hunt/run budget and count delivery attempts cumulatively. Resolve campaign duration from the original persisted run start, never from worker-process start. Dead-letter state must be idempotently projected onto a non-terminal subject with refresh-visible recovery guidance.

## Agent Loop Pattern

```text
authorize and validate
  -> generate structured scenario and invariant proposal
  -> execute one bounded deterministic attempt
  -> evaluate invariants in code
  -> summarize evidence references
  -> let Gemini choose an allowed next action
  -> execute, refine, minimize, or stop
  -> persist finding and immutable replay artifact
```

Gemini can select only from an allowlisted action vocabulary. Every iteration records evidence inputs, chosen action, rationale summary, model and schema versions, usage, timestamps, and outcome. Server-side budgets and safety checks override model choices.

Planning and strategy adapters count every provider invocation, including constrained repair calls. A zero remaining model budget prevents provider access; invalid or repaired output cannot bypass operation, invariant, strategy, actor, request, timing, iteration, or duration limits.

## Finding, Minimization, and Replay Pattern

- Promote a deterministic reference-target failure to a finding only after three separately executed equivalent schedules all return `Fail`; `Pass` or `Inconclusive` prevents the verified 3/3 claim.
- Minimize the recorded schedule deterministically: remove actors down to the two-actor floor, then remove steps, and retain a candidate only when exact replay preserves the same failed invariant. Minimization never infers success from timing or model interpretation.
- Treat the replay artifact as content-addressed evidence. Canonicalize embedded JSON, normalize timestamp precision to the PostgreSQL storage boundary, order steps deterministically, compute a SHA-256 fingerprint, and validate it on every rehydration and before and after execution.
- Persist the finding, immutable artifact, ordered steps, three reproduction outcomes, and initial vulnerable attempt as one short transaction. Subsequent replay attempts append evidence; they never update the artifact or original finding.
- Persist every reproduction, reduction candidate, and proof outcome under a stable run-scoped probe key. Recover a completed probe row before scheduling new work.
- Reconcile receiver idempotency keys and correlations with persisted trace request IDs before execution. Reserve trace-ledger-missing logical work before mutation and charge newly persisted evidence exactly once.
- Scope receiver keys by artifact or candidate, actor, and stable step identity; a caller idempotency key alone is never sufficient.
- Serialize the one allowed fixed-target execution with a durable per-artifact claim. Commit or take over the claim independently, perform worker HTTP outside a database transaction, then persist the winning attempt. Release failed ownership and allow bounded stale-claim recovery.
- Build the causal timeline only from trace references that support the finding. Preserve run-attempt identity on every event, order by UTC timestamp and durable sequence, group by actor lane, and present Gemini Agent Activity as advisory evidence alongside—not inside—the deterministic proof.
- Compare vulnerable and fixed outcomes only when both attempts report the stored artifact fingerprint. A Verify Fix transport failure leaves the original finding visible and retryable.

## Error and Outcome Taxonomy

Classify failures as target, transport, model, persistence, orchestration, cancellation, validation, safety/authorization, or invariant outcome. Invariant evaluation returns pass, fail, or inconclusive; inconclusive is never a defect.

Use structured RFC 9457 Problem Details at HTTP boundaries. Preserve correlation across project, experiment, run, attempt, actor, step, request, and model invocation IDs.

## Security Patterns

- Persist secret references, never secret values. Resolve with least-privilege workload identity.
- Redact authorization headers, cookies, tokens, and configured sensitive JSON paths before persistence, logs, traces, or Gemini calls.
- Default targets to HTTPS; permit local HTTP only in explicit development mode.
- Enforce hard traffic and cost budgets server-side and make them visible to the user.
- Audit target changes, run/replay requests, cancellation, safety rejections, and privileged demo reset actions.
- Keep reference-target reset and vulnerable/fixed controls unavailable outside approved demo/development configuration.
- Hide manual-target configuration behind a fixed-time local/admin bearer check and return authoritative `401`/`403` outcomes for missing/wrong credentials. Persist a one-way owner-key fingerprint with the target and enforce it across hunt planning/approval, run reads/actions, findings, replay/Verify Fix, and Cloud Proof so UUID possession is never authorization. Require explicit ownership acknowledgement, an exact host/port allowlist, HTTPS outside explicit Development hosts, stable GET/POST operation IDs, bounded JSON templates with four allowlisted placeholders, typed deterministic observation JSON paths, configured sensitive paths, and a Secret Manager version reference; never accept a credential value.
- Resolve every manual destination immediately before use, reject the whole answer set if any address is loopback, private, link-local, metadata, multicast, or otherwise prohibited, and pin the HTTP connection to the validated address with `SocketsHttpHandler.ConnectCallback`. Disable automatic redirects and reject cross-scheme, cross-host, and even same-host implicit redirects until an operation explicitly authorizes them.
- Enforce public-sandbox ceilings at the API boundary even when a caller supplies larger JSON values. Only the authenticated local/admin engine may request up to 100 logical actors, and it remains subject to global, target, experiment, request, model, duration, and retry limits.
- Acquire Cloud Run identity tokens only from the metadata server with proxy bypass, cache them only until near expiry, bind them to the exact configured HTTPS audience, and refuse to attach them to a different scheme, host, or port. Pub/Sub push uses the same exact worker audience and service-account-scoped `run.invoker` IAM.
- Keep worker and target `run.app` ingress routable when no VPC path is provisioned, but grant `run.invoker` only to the exact API, Pub/Sub push, and worker service accounts. An internet-routable URL is not a public service without `allUsers` IAM.
- Bind an authorized manual target ID immutably to the hunt. Planning sees only that snapshot's executable operations and compatible numeric-boundary/cardinality/same-response cross-observation families; concurrent execution reuses one validated in-scope snapshot, resolves its Secret Manager reference only inside the worker, renders only allowlisted placeholders, bounds and redacts responses, and extracts only configured number/text observations. Findings must reflect shared transactional state rather than actor identity. Require at least two failures in three equivalent external replays, minimize to two actors, embed the complete target snapshot in the artifact, and reject replay if the current immutable record differs. Persist sanitized configuration/execution/replay safety categories and record manual-call latency/outcome on every exit. Staging grants the worker accessor IAM only for Terraform's explicit `manual_target_secret_ids` set.

## Observability Pattern

Emit structured logs, OpenTelemetry traces, and metrics for queue delay, active runs, attempts, limiter occupancy, target latency/errors, invariant outcomes, findings, replay rate, Gemini calls/usage/failures, database latency, duplicate messages, and cancellation latency. Successful low-level events may be sampled; evidence supporting a finding may not.

Use JSON console logging in all three application images. Propagate W3C `traceparent` and `tracestate` through Pub/Sub attributes and HTTP automatically; retain the durable RaceHunter correlation ID across outbox and inbox recovery. Add safe span tags for work, run, attempt, actor, step, request, model invocation, finding, and replay artifact IDs, never payload bodies or credentials. Compose export is opt-in through `OTEL_EXPORTER_OTLP_ENDPOINT`; Terraform supplies a Google-built collector sidecar per service and the least-privilege Cloud Trace/Monitoring writer roles.

Cap staging at one worker instance and one inbound worker request while limiters are process-local. The single worker still schedules up to 100 logical actors under its global, target, experiment, request, and duration ceilings; scale-out requires a distributed limiter first.

## Testing Patterns

- Start production behavior test-first at the narrowest meaningful boundary.
- Use fixed clocks, seeds, IDs, and deterministic model fakes.
- Test ceilings and backpressure as invariants, not timing guesses.
- Test vulnerable and fixed reference-target modes through the same replay artifact.
- Architecture tests enforce dependency and data-access constraints continuously.
- Integration tests own database/provider behavior; unit tests do not mock EF internals.
- Every phase must leave a demonstrable vertical capability on the golden path.

## Change Control

The decisions marked Required and ADR-001 through ADR-007 in `RaceHunter_Project_Brief.md` are approved constraints. Do not replace them silently. Any change must document the new evidence, trade-off, demo impact, and user approval.
