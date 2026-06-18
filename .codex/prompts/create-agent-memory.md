---
description: Save a durable fact or convention into the shared .ai/memory knowledge base.
---

Record a durable fact or convention into `.ai/memory/` (shared by Claude and Codex) so it is always applied. Memory is "how things should be done"; a specific incident belongs in `/create-lessons` instead.

## Process

1. Identify the fact from the recent work or from the arguments: $ARGUMENTS. State it in 1-4 sentences and pick a `type`: project | convention | preference | reference | environment.
2. Read `.ai/memory/INDEX.md` — update an existing entry rather than duplicating; delete entries that are now wrong.
3. Write `.ai/memory/<short-kebab-slug>.md` following `.ai/memory/TEMPLATE.md` (body: the fact, then **Why:** and **How to apply:**). Link related entries with `[[slug]]`.
4. Add one line to `.ai/memory/INDEX.md`: `- [<slug>](<file>.md) — <one-line hook>`.
5. Confirm the new entry path and its summary.

Rules: one fact per file; prefer updating over duplicating; never save secrets, tokens, connection strings, or sensitive personal data; capture the non-obvious decision, not what the code already shows.
