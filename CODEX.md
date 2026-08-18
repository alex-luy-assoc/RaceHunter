# Codex Project Guidance

This project uses the ALA Memory Bank v2 workflow.

- Start with `ala:go` to inspect durable state and choose the next safe action.
- Treat `memory-bank/projectConfig.md`, `productBrief.md`, `techContext.md`, and `systemPatterns.md` as durable project context.
- Treat decisions marked Required and ADR-001 through ADR-007 in `RaceHunter_Project_Brief.md` as approved constraints.
- Keep task plans and execution state in `memory-bank/tasks/<slug>.md`.
- Preserve user changes and use test-first implementation for production code.
- Respect configured branch routing and protected branches when Git is initialized.
- Never add usage telemetry, analytics, session hooks, transcript logging, or external collectors.
- Keep deterministic evidence, validation, and safety enforcement outside Gemini.
