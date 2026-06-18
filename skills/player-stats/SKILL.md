---
name: player-stats
description: Use when recording, computing, validating, or presenting player and team statistics for the pickup soccer group — goals, assists, appearances, minutes, clean sheets, cards, leaderboards, standings, and season tables. Stats follow official Premier League / UEFA Champions League conventions. Trigger on "record the stats", "update the leaderboard", "who's top scorer", "build the league table", or "season stats".
---

# Pickup Soccer — Player Stats

Track and present stats the way the **Premier League** and **UEFA Champions League** do, so the numbers feel familiar and official. Use this skill for the data definitions and output formats; pair with **brand-design-kit** for styled tables and **xlsx** when the deliverable is a spreadsheet.

## Stat Set (Premier League / UCL aligned)

**Outfield (per player, per season)**

| Code | Stat | Definition |
|------|------|-----------|
| MP / Apps | Matches Played (Appearances) | Sessions the player took part in (sub or start). |
| Starts | Starts | Appearances from the opening whistle. |
| Min | Minutes | Total minutes played. |
| G | Goals | Goals scored (own goals tracked separately, not credited). |
| A | Assists | Final pass/touch leading directly to a goal (one assist per goal). |
| G+A | Goal Involvements | G + A. |
| xG / xA | Expected Goals / Assists | Optional, only if shot data is tracked. |
| SoT | Shots on Target | Optional. |
| YC / RC | Yellow / Red Cards | Disciplinary. |
| MOTM | Player of the Match | Awarded per session (vote or admin pick). |

**Goalkeeper**

| Code | Stat | Definition |
|------|------|-----------|
| CS | Clean Sheets | Matches where the GK's team conceded 0 (min. a set number of minutes, PL uses 60+ — scale to your session length). |
| GC | Goals Conceded | Total conceded while on the pitch. |
| Sv | Saves | Optional. |

**Derived / rate metrics** (compute, don't store): Goals per game `G/MP`, Goal involvements per 90 `(G+A) ÷ Min × 90`, Minutes per goal, Win % , Attendance % `MP ÷ sessions held`.

## Leaderboards (PL/UCL style)

- **Golden Boot** — rank by Goals. Project tie-breakers are fewer appearances, then more assists,
  matching `documentation/architecture.md` and `_specs/requirements.md`.
- **Playmaker (Assists)** — rank by Assists; tie-break by fewer minutes, then more goals.
- **Goal Involvements** — rank by G+A.
- **Golden Glove** — rank GKs by Clean Sheets; tie-break by fewer goals conceded.
- Show the top 5–10 with rank, name, and the headline metric; mark movement (▲▼) vs. last update when available.

## Standings / League Table

When sessions are scored as fixtures (e.g., recurring teams or a mini-league), use the **standard football table** with Premier League ordering:

`Pos | Team | Pl | W | D | L | GF | GA | GD | Pts`

- Points: Win = 3, Draw = 1, Loss = 0.
- Sort by: Points → Goal Difference (GD = GF − GA) → Goals For → (then head-to-head or alphabetical).
- Highlight the leader; keep GD signed (e.g., `+7`, `−3`).

## Output Formats

**Markdown leaderboard**
```
| # | Player | MP | G | A | G+A | MOTM |
|---|--------|----|---|---|-----|------|
| 1 | …      | 12 | 9 | 4 | 13  | 3    |
```

**League table** — same columns as above, brand-styled (Flag Green header row, white text, Mist zebra rows) when rendered to docx/pptx/HTML, or via the **xlsx** skill for a workbook with a Players sheet, a Standings sheet, and auto-computed leaders.

## Data Integrity Rules

- One assist max per goal; own goals never credited to a scorer.
- A goal involvement requires the player to be recorded in that match's stat lines.
- Recompute derived metrics on read — store only raw events (goal, assist, appearance, card, result).
- Clean sheet requires the GK to have met the minutes threshold for that session.
- Lock a match's stats after admin review; corrections create an adjustment entry (keep an audit trail), never silent edits.

## Checklist

1. Using the PL/UCL stat codes and definitions above?
2. Leaderboards sorted with the correct tie-breakers?
3. Table ordered by Pts → GD → GF, points 3/1/0?
4. Derived metrics computed, not hand-entered?
5. Styled output on-brand (green/white) or delivered via xlsx as requested?
