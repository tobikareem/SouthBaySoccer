---
description: "Save a durable fact or convention into the shared .ai/memory knowledge base so agents always apply it. Use when the user says 'remember this', 'always do X', 'save this convention/decision', or states a stable project preference."
allowed-tools: Read, Write, Edit, Glob, Grep
---

## Purpose

Record a **durable fact or convention** that Claude and Codex should always apply — a project decision, standard, preference, or environment fact. Memory lives in `.ai/memory/` and is shared across both tools.

Memory vs lessons: **memory** is "how things should be done" (stable). A specific incident we learned from belongs in `/create-lessons` instead.

## Process

1. **Identify the fact** from the conversation or the user's `$ARGUMENTS`. State it in 1-4 plain sentences. Determine its `type`: project | convention | preference | reference | environment.

2. **Check for duplicates** — read `.ai/memory/INDEX.md`. If an entry already covers this, update it rather than duplicating. Delete entries that are now wrong.

3. **Write the memory** to `.ai/memory/<short-kebab-slug>.md` using `.ai/memory/TEMPLATE.md` (frontmatter: name, description, type, created; body: the fact, then **Why:** and **How to apply:**). Link related entries with `[[slug]]`.

4. **Update the index** — add one line to `.ai/memory/INDEX.md`: `- [<slug>](<file>.md) — <one-line hook>`.

5. **Confirm** — show the user the new entry path and its one-line summary.

## Rules

- One fact per file. Prefer updating over duplicating.
- Do NOT save sensitive personal data, secrets, tokens, or connection strings, even if asked casually.
- Don't record what the code or git history already makes obvious; capture the non-obvious decision instead.
