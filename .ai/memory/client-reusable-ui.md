---
name: client-reusable-ui
description: MAUI client uses a token-driven reusable UI design system; spec in _specs/client-ui.md
type: convention
created: 2026-06-18
---

The MAUI client UI is built from a reusable, token-driven design system defined by
`_specs/client-ui.md` and visually governed by `documentation/mobile-wireframes.html`. Brand colors,
typography, spacing, radii, and shared styles live in
`SouthBaySoccer/Resources/Styles/BrandColors.xaml`, `BrandTokens.xaml`, and `BrandStyles.xaml`.
Reusable controls live in `SouthBaySoccer/Controls/`.

The reusable foundation and its `M11.0` task are complete. Adoption by individual product screens is
tracked separately and does not make the design-system foundation incomplete.

The first product-screen wave has one additive shared-library prerequisite (`M11.0c`): add
`LeadingContent` to `BrandHeader`/`PlayerRow` plus shared `IconButton`, `IconToggleButton`,
`MetadataChip`, and `RatingSlider` styles. Product pages must wait for those shared extensions rather
than create page-local substitutes.

**Why:** This keeps screens consistent with the green/white brand and authoritative mobile wireframe while
keeping controls MVVM-clean: `BindableProperty` inputs, `ICommand` outputs, and no business logic.

**How to apply:** Use named resources rather than page-local hex values, font sizes, or spacing.
Compose new screens from BrandHeader, BrandCard, Badge, Avatar, StatTile, CapacityBar,
SectionHeader, PlayerRow, SegmentedControl, CounterStepper, and StateView. Existing sample screens
are migrated only when their product feature is replaced.

If a reusable control or this memory conflicts with the wireframe, update the control/spec to match
the wireframe rather than creating page-local visual exceptions.

Related: [[brand-green-white]], [[mobile-wireframes-design-source]], [[spec-driven-development]]
