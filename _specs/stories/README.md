# `_specs/stories/` — Per-story specifications

Each user story has its own directory with the three spec concerns kept **separate**:

```
stories/<STORY-ID>-<slug>/
├── requirements.md   # the user story + Gherkin acceptance criteria (the "what")
├── design.md         # how this story is realized (the "how") — links to global design
└── tasks.md          # the implementation tasks for this story (the "do")
```

## Relationship to the top-level overviews

The top-level `_specs/` files remain the **cross-cutting source of truth** and are not duplicated here:

- [`../requirements.md`](../requirements.md) — personas, **global invariants (`INV-*`)**, **NFRs**, and the epic/story index.
- [`../design.md`](../design.md) — layer/dependency model, domain model, API surface, shared flows, persistence, test mapping.
- [`../tasks.md`](../tasks.md) — the milestone roadmap (`M0…M12`) and dependency graph.

A per-story file states only what is specific to that story and **links** to the global rules it
depends on, rather than copying them (so invariants/NFRs never drift).

## Index (pilot: Welcome Back screen)

| Story | Directory | Summary |
|-------|-----------|---------|
| `AUTH-7` | [`AUTH-7-welcome-back-screen/`](AUTH-7-welcome-back-screen/requirements.md) | The Welcome Back (sign-in) screen — first app route. |
| `AUTH-8` | [`AUTH-8-continue-with-whatsapp/`](AUTH-8-continue-with-whatsapp/requirements.md) | Request + verify the one-time WhatsApp sign-in challenge. |
| `AUTH-9` | [`AUTH-9-pickup-pal-actions/`](AUTH-9-pickup-pal-actions/requirements.md) | Pickup Pal bot / signup external actions. |

This is the **pilot** of the per-story layout. Once approved, the remaining stories (PROF, WAIV,
PAY, SES, RSVP, CHK, TEAM, STAT, LEAD, NOTIF, ADMIN) are migrated into the same structure.

## Conventions

- Directory name: `<STORY-ID>-<kebab-slug>`.
- A story is **Done** only when every Gherkin scenario has an automated test and works through the
  Function App and, where relevant, the MAUI client (per the global definition of done in `../tasks.md`).
- Visual authority for client stories is [`../../documentation/mobile-wireframes.html`](../../documentation/mobile-wireframes.html); implementation contract is [`../client-ui.md`](../client-ui.md).
