---
description: "Review uncommitted code changes (staged and unstaged) on the current branch before committing. Use when the user says 'review my changes', 'check my code', 'code review', or wants pre-commit feedback."
allowed-tools: Bash(git diff *), Bash(git diff --staged *), Bash(git status *), Task
---

## Process

1. **Gather the diff** — run these in parallel:
   - `git diff` for unstaged changes
   - `git diff --staged` for staged changes
   - `git status` for the changed file list
   - If both diffs are empty, tell the user there are no changes to review and stop.

2. **Dispatch the dotnet-code-reviewer subagent** using the Task tool:
   - Pass the combined diff output (staged + unstaged) as input
   - Instruct the subagent to focus ONLY on changed code, not the entire codebase
   - Instruct the subagent to reference file paths, line numbers, and code snippets
   - If the change is primarily XAML/MVVM UI, also dispatch the maui-xaml-reviewer subagent in parallel and merge its findings.

3. **Present the subagent's report**, then append these additional sections:
   - **Action plan**: Ordered checklist of fixes derived from the critical issues and improvements
   - **Questions/uncertainties**: Anything needing human clarification

## Rules

- Do NOT edit any files.
- Do NOT make formatting-only changes.
- Apply project standards from `AGENTS.md`, `CLAUDE.md`, `documentation/architecture.md`, `_specs/`,
  and `skills/southbay-soccer-conventions/SKILL.md`.
- Check `.ai/memory/` and `.ai/lessons/` for relevant constraints before reporting.

Finish by asking: "Do you want me to implement the action plan now?"
Wait for user confirmation before making any changes.

If the review surfaced a non-obvious gotcha worth keeping, suggest running `/create-lessons` to capture it.
