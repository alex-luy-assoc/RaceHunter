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

## Observability Pattern

Emit structured logs, OpenTelemetry traces, and metrics for queue delay, active runs, attempts, limiter occupancy, target latency/errors, invariant outcomes, findings, replay rate, Gemini calls/usage/failures, database latency, duplicate messages, and cancellation latency. Successful low-level events may be sampled; evidence supporting a finding may not.

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
