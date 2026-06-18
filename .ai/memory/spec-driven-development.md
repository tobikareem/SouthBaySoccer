---
name: spec-driven-development
description: Specs live in _specs/ (requirements, design, tasks, client-ui); Gherkin acceptance criteria
type: convention
created: 2026-06-18
---

This is a spec-driven project. The executable specification lives in **`_specs/`** (plural, at the
solution root): `requirements.md` (whole-product user stories with **Gherkin** `Given/When/Then`
acceptance criteria + stable IDs like `RSVP-3` and global invariants), `design.md` (requirements →
architecture mapping, domain model, API surface, flows, test mapping), `tasks.md` (ordered milestones
`M0…M12` tracing to story IDs), and `client-ui.md` (MAUI reusable UI design system).

**Why:** Every feature is built from an approved spec; scenarios become tests.
**How to apply:** Start a feature by reading the relevant story IDs in `_specs/requirements.md` and the
milestone in `_specs/tasks.md`; `documentation/architecture.md` stays authoritative. Folder is `_specs/`, not `_spec/`.

Related: [[project-root-and-skills]], [[client-reusable-ui]]
