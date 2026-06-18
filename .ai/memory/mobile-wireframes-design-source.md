---
name: mobile-wireframes-design-source
description: documentation/mobile-wireframes.html is the authoritative visual and interaction reference for the MAUI client
type: convention
created: 2026-06-18
---

`documentation/mobile-wireframes.html` is the **source of truth for SouthBaySoccer mobile product
design**. New MAUI product screens and reusable controls must reproduce its visual hierarchy,
component shapes, spacing, interaction states, and screen flows unless the wireframe is explicitly
updated first.

**Precedence:** for UI work, use the wireframe for screen composition and visual behavior,
`_specs/client-ui.md` for the reusable MAUI implementation contract, and
`skills/brand-design-kit/SKILL.md` for the underlying brand palette and identity. If they disagree,
update the spec/control library to match the wireframe; do not silently invent a different pattern.

**Core patterns to preserve:**

- White-dominant screens anchored by Flag Green `#008751` and Pine `#005C37`.
- Green-to-pine headers and hero cards, subtle circular/flag motifs, 16px cards, and 12–14px controls.
- Strong next-action hierarchy, compact uppercase section labels, metadata chips, status badges,
  capacity bars, player rows, segmented leaderboards, and rounded-square 44px steppers.
- Minimum 44px interactive targets, explicit focus/semantic state, and color paired with text/glyphs.
- Shell bottom navigation for Sessions, Stats, and Profile.

Do not replace the wireframe wholesale or introduce a competing page-level design language. Change
the wireframe first when product design changes, then update `_specs/client-ui.md`, tokens, shared
styles, controls, and product screens together.

Related: [[brand-green-white]], [[client-reusable-ui]], [[spec-driven-development]]
