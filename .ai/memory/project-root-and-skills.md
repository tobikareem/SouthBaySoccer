---
name: project-root-and-skills
description: SouthBaySoccer is the solution root; project skills live in skills/
type: project
created: 2026-06-17
---

`D:\source\SouthBaySoccer` is the canonical project/solution root: it holds `SouthBaySoccer.slnx`, `CLAUDE.md`, `agent.md`, and all agent tooling (`.ai/`, `.claude/`, `.codex/`, `skills/`). The .NET MAUI project itself lives one level down at `D:\source\SouthBaySoccer\SouthBaySoccer\` (its `.csproj`). Run agents from the repo root so the tooling is discovered. Four project skills live under `skills/` (at the root): `brand-design-kit`, `maui-blazor-conventions`, `matchday-content`, and `player-stats` (stats follow Premier League / UEFA Champions League conventions).

Codex code-review requests use the canonical automatic skill at
`.agents/skills/source-command-code-review/SKILL.md`. Do not duplicate that workflow under
`.codex/prompts/`; that prompt directory is reserved for `create-agent-memory` and
`create-lessons`.

**How to apply:** Treat `skills/` as authoritative guidance for branding, code conventions, group communications, and stats formatting. Keep all agent tooling at the solution root. The retired `Football` working folder is no longer used.

Related: [[brand-green-white]]
