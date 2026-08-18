# RaceHunter Product Brief

## Product Foundation

- **Working name:** RaceHunter
- **Category:** Autonomous concurrency-correctness testing for HTTP/JSON APIs
- **Hackathon:** All Things Agentic Hackathon, August 3–31, 2026
- **Track:** The Taskmaster
- **Submission deadline:** August 31, 2026 at 5:00 PM Pacific / 8:00 PM Eastern
- **Source brief:** `RaceHunter_Project_Brief.md`
- **Official references:** `https://allthingsagentichackathon.devpost.com/` and `/rules`

RaceHunter plans and runs bounded concurrent API experiments to discover business-level race conditions, prove them with deterministic invariant checks, minimize the failing schedule, and replay the same artifact against a fix.

Traditional load tests answer whether a system remains fast under traffic. RaceHunter answers whether it remains correct when otherwise valid operations collide.

## Primary Persona

A backend developer responsible for a transactional HTTP API who suspects a rare concurrency defect but cannot reliably reproduce it. They need a safe way to turn a business rule into a bounded experiment and leave with evidence they can act on.

Secondary users are QA/test-automation engineers, platform and reliability engineers, and architects diagnosing intermittent distributed-workflow failures.

## Core User Journey

1. The developer chooses the included, authorized inventory target.
2. They state the business rule: successful orders must not exceed available inventory.
3. Gemini proposes a structured multi-buyer scenario and machine-evaluable invariant for review.
4. The developer starts one bounded campaign and may leave the page.
5. The worker independently executes, observes, adapts, and stops within configured budgets.
6. RaceHunter proves an oversell with a causal trace, then minimizes it to two buyers.
7. The developer replays the immutable artifact against vulnerable mode and sees it fail.
8. They enable the transactionally correct target and replay the same artifact to verify the fix.

## Hackathon Outcome

The submission should compete credibly for the Grand Prize, Taskmaster prize, Individual/Hobbyist prize when eligible, and Best Architectural Design. The product must optimize for the published weighting:

- **Innovation & Operational Utility (40%):** visibly complete a distinctive, high-value background workflow with minimal intervention. The agent must make consequential experiment-strategy decisions and take action, not merely generate text.
- **Architectural Discipline & Tech Stack (30%):** demonstrate clean boundaries, durable state, bounded execution, scoped tools, credential safety, idempotency, recovery, and observable failures.
- **Demo & Production Readiness (30%):** show an unedited live run, undeniable state changes and evidence, a clear architecture diagram, reproducible setup, and visible Google Cloud deployment within four minutes.

## Required Product Capabilities

- Accept a concise business objective plus a small authorized HTTP/JSON target definition.
- Generate versioned, schema-constrained scenarios and proposed invariants with Gemini 3.5 Flash or newer.
- Execute a bounded plan-execute-observe-adapt loop asynchronously through a worker.
- Enforce server-side request, actor, concurrency, duration, retry, model, and target-host budgets.
- Evaluate correctness deterministically as pass, fail, or inconclusive; AI explanations cannot change the outcome.
- Persist incremental progress, agent decisions, trace evidence, findings, and immutable replay artifacts.
- Minimize a failing campaign and measure replay success rather than overclaim determinism.
- Replay the exact artifact against vulnerable and fixed target modes.
- Clearly separate observed evidence from Gemini interpretation in the UI.
- Provide cancellation, duplicate-message safety, restart-aware state, secret redaction, and target authorization controls.

## Golden-Path Success Criteria

- Reset the reference target to one inventory unit.
- Start from the plain-language oversell rule.
- Show Gemini creating or refining the executable plan.
- Run multiple controlled schedules without further user direction.
- Detect and evidence the oversell.
- Minimize to two buyers and the minimum required steps.
- Present an actor-lane causal timeline and replay ID.
- Replay to fail on vulnerable mode and pass on fixed mode.
- Complete the judge-facing live demonstration, including Cloud Run or Vertex AI proof, in under four minutes.

## Non-Functional Requirements

- At least 100 logical actors per bounded experiment.
- Independent global and per-target concurrency limits.
- UI recovery from persisted state after refresh.
- Cancellation starts within two seconds of request.
- Duplicate Pub/Sub delivery cannot create a second logical execution.
- UTC timestamps, structured logs, correlated traces, and secret-safe evidence.
- English UI and submission materials.
- Consistent install/run behavior matching the demo and written description.

## Responsible-Use Boundary

RaceHunter is not an open load generator. The MVP is restricted to the included reference target and explicitly configured user-owned targets. It requires target authorization, host allowlisting, SSRF defenses, hard traffic budgets, redaction, and secret references. It does not claim exhaustive schedule verification.

## Hackathon Scope Guardrails

The contest requires work submitted as RaceHunter to be newly created during the submission period. Standard libraries, frameworks, templates, and AI coding assistants are allowed; any other pre-existing code or work incorporated must be disclosed.

The MVP excludes browser concurrency testing, source-code race detection, arbitrary public targets, packet capture, direct target-database access, multi-region or Kubernetes deployment, multi-tenant billing, exhaustive formal verification, non-HTTP protocols, CI pull-request commenting, and a scenario marketplace.

OpenAPI import is stretch scope. Exportable xUnit tests, CI integration, extra scenario libraries, and SaaS controls are post-hackathon scope.

## Current Repository State

This is a greenfield repository initialized from `RaceHunter_Project_Brief.md`. Product code has not yet been created. Proposed source, test, deployment, and documentation structure is recorded in `techContext.md`.
