---
description: "Capture a lesson learned from the current work into the shared .ai/lessons knowledge base. Use when the user says 'save this lesson', 'remember what went wrong', 'create a lesson', or after solving a non-obvious problem worth not repeating."
allowed-tools: Read, Write, Edit, Glob, Grep
---

## Purpose

Record an **experiential learning** — a problem we hit and how we resolved it — so neither Claude nor Codex repeats it. Lessons live in `.ai/lessons/` and are shared across both tools.

Lessons vs memory: a **lesson** is "what happened and what we learned" (dated, specific). A durable convention or fact belongs in agent memory instead — use `/create-agent-memory`.

## Process

1. **Identify the lesson** from the recent conversation/work (or from the user's `$ARGUMENTS`). Capture: context, problem, resolution, takeaway. Ask the user only if the lesson is unclear.

2. **Check for duplicates** — read `.ai/lessons/INDEX.md`. If an existing lesson covers this, update that file instead of creating a new one.

3. **Write the lesson** to `.ai/lessons/<YYYY-MM-DD>-<short-kebab-slug>.md` using the structure in `.ai/lessons/TEMPLATE.md` (frontmatter: name, description, area, created; body: Context, Problem, Resolution, Takeaway). Link related entries with `[[slug]]`.

4. **Update the index** — add one line to `.ai/lessons/INDEX.md`: `- [<slug>](<file>.md) — <one-line hook>`.

5. **Confirm** — show the user the new entry path and its one-line summary.

## Rules

- One lesson per file. Keep it short and concrete.
- Do not store secrets, tokens, or personal data.
- Use today's date (run a shell `date` check if unsure) and convert any relative dates to absolute.
