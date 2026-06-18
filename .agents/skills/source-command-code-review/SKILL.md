---
name: "source-command-code-review"
description: "Review uncommitted code changes (staged, unstaged, and untracked) before committing. Use when the user says 'review my changes', 'check my code', 'code review', or wants pre-commit feedback."
---

# source-command-code-review

This is the canonical Codex workflow for reviewing local changes. Use it for explicit
`code-review` requests and natural-language requests such as "review my changes."

## Process

1. Gather the review scope in parallel:
   - `git diff` for unstaged changes.
   - `git diff --staged` for staged changes.
   - `git status --short` for tracked and untracked files.
   - Include the contents of relevant untracked source and configuration files.
   - Stop only when both diffs are empty and there are no relevant untracked files.

2. Dispatch the configured `dotnet-code-reviewer` subagent when subagents are available:
   - Pass the combined staged, unstaged, and relevant untracked changes.
   - Instruct it to review only changed code and use surrounding files only for context.
   - Require file paths, line numbers, and concrete fixes.
   - For primarily XAML/MVVM changes, also dispatch `maui-xaml-reviewer` in parallel and merge
     the findings.
   - If subagents are unavailable, perform the same review directly.

3. Present findings grouped by Critical, Improvements, and Nits, then append:
   - **Action plan**: ordered fixes derived from the findings.
   - **Questions/uncertainties**: anything requiring human clarification.

## Rules

- Do not edit files during the review.
- Do not make formatting-only changes.
- Apply project standards from `AGENTS.md`, `documentation/architecture.md`, `_specs/`, and relevant
  project skills.
- Check `.ai/memory/` and `.ai/lessons/` for relevant constraints.
- Finish by asking: "Do you want me to implement the action plan now?"
- If the review exposes a non-obvious issue worth retaining, suggest `/create-lessons`.
