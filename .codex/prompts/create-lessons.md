---
description: Capture a lesson learned into the shared .ai/lessons knowledge base.
---

Record an experiential learning into `.ai/lessons/` (shared by Claude and Codex) so we don't repeat a mistake. A lesson is "what happened and what we learned"; a stable convention belongs in `/create-agent-memory` instead.

## Process

1. Identify the lesson from the recent work or from the arguments: $ARGUMENTS. Capture Context, Problem, Resolution, Takeaway. Ask only if unclear.
2. Read `.ai/lessons/INDEX.md` — if an existing lesson covers this, update it instead of creating a duplicate.
3. Write `.ai/lessons/<YYYY-MM-DD>-<short-kebab-slug>.md` following `.ai/lessons/TEMPLATE.md`. Link related entries with `[[slug]]`.
4. Add one line to `.ai/lessons/INDEX.md`: `- [<slug>](<file>.md) — <one-line hook>`.
5. Confirm the new entry path and its summary.

Rules: one lesson per file; no secrets or personal data; use today's actual date.
