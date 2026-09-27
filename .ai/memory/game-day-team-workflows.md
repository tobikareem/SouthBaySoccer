---
name: game-day-team-workflows
description: Game-day captain, draft, team-lock, and postgame state transitions are enforced server-side
type: project
created: 2026-07-22
---

The primary session Match is the server-side state machine for game-day team and postgame work.
`Draft` permits GameAdmin captain topology changes and resource-scoped captain picks.

Captain draft window: captains may act on the pre-game team workflow for the whole Pacific game
day — from Pacific midnight of the session's start date (or check-in opening, whichever is earlier,
`CaptainTeamSetupOpensAtUtc`) until post-game opens 90 minutes after kickoff. GameAdmins are
unconstrained by this (publish until 3 days after kickoff). `GetTeamDraftQueryHandler`'s `locked`
mirrors `IsTeamSetupOpen` for non-admins so the draft page can never show picks the mutation would
reject; before the window it labels the state "Drafting opens on game day". The idempotent
GameAdmin `POST game-day/sessions/{sessionId}/teams/lock` transition validates captains and the
confirmed (Going + Waitlist) assignment roster, audits the action, and moves the Match to
`InProgress`. Result and event-review commands reject `Draft`; normal mutation rejects `Published`
and `Locked`.

Team eligibility is the confirmed **Going + Waitlist** roster, NOT the checked-in roster. Captain
selection, the draft player pool, and the lock guard all use
`GameDayWorkflowQueries.ListEligibleRosterAsync` (local Going + active Waitlist + linked imported
Pickup Pal participants, deduped, Going preferred). Check-in is a separate attendance fact; an admin
checks players in from Game Day (`GameDayContextModel.Roster` carries per-player `IsWaitlist` /
`IsCheckedIn`, and `CanManageCheckIns` gates the in-window admin `CheckedIn` button).

Captain authority is derived from the authenticated player's `MatchTeam.CaptainPlayerProfileId`, not
from a global client flag. GameAdmins can assign captains, lock teams, and operate any result;
captains draft and record only their own team, **but a GameAdmin/Admin/Owner can also draft any
team on behalf of its captain** (`GetTeamDraftQueryHandler` sets `CanManageAllTeams`, and
`SaveCaptainTeamPicksCommandHandler` allows `team.Captain == actor || IsGameAdmin` and appends the
team's own captain, not the acting admin). Either captains or GameAdmins may review the match event
queue. The Game Day capability flags are projections of these persisted facts.

Postgame begins 90 minutes after session start. Every raw event is visible in the approval queue,
and approval addresses the stable MatchEvent id. Publishing requires every team result, no pending
events, no unresolved review state, and a complete, internally consistent rotation W/D/L matrix.
Publishing is idempotent and makes ordinary facts immutable; subsequent corrections use the audit
correction workflow.

Related: [[game-day-today-projection]], [[m8-teams-stats]], [[m9-leaderboards-queries]]
