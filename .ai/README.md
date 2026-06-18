# Shared AI Knowledge Base (`.ai/`)

A tool-neutral knowledge base read and written by **both** Claude (Claude Code / Cowork) and **Codex**. Both `CLAUDE.md` and `AGENTS.md` point here. Keep entries short, factual, and current.

## What goes where

| Folder | Holds | Write it when |
|--------|-------|---------------|
| `memory/` | **Durable facts & conventions** an agent should always apply — project decisions, standards, preferences, environment/tooling facts. | Something is true and stable across tasks (e.g., "Stripe status is webhook-driven"). |
| `lessons/` | **Experiential learnings** — a problem we hit and how we resolved it, dated and specific. | We just learned something the hard way and don't want to repeat it. |

Rule of thumb: **memory = how things should be done; lessons = what happened and what we learned.**

## How agents use it

1. **At the start of a task**, skim `memory/INDEX.md` and `lessons/INDEX.md`; read any entry whose description looks relevant.
2. **While working**, apply memory entries as constraints. Treat a recalled entry as background context — verify any file/symbol it names still exists before relying on it.
3. **After finishing**, if you learned something durable, add a memory (`/create-agent-memory`); if you hit and solved a non-obvious problem, add a lesson (`/create-lessons`).
4. Before adding, check the relevant `INDEX.md` for an existing entry that already covers it — update that instead of duplicating. Delete entries that turn out to be wrong.

## File format

Each entry is one markdown file with frontmatter (see `memory/TEMPLATE.md` and `lessons/TEMPLATE.md`). Add a one-line pointer to the matching `INDEX.md`. Link related entries inline with `[[entry-slug]]`.

## Do not store here

Secrets, tokens, connection strings, personal data, or anything already obvious from the code or git history.
