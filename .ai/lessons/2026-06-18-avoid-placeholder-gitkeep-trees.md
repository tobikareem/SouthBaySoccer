---
name: avoid-placeholder-gitkeep-trees
description: Do not commit .gitkeep files solely to mirror a planned architecture tree
area: repository
created: 2026-06-18
---

**Context:** The initial Clean Architecture scaffold created dozens of empty directories matching
the target structure in `documentation/architecture.md`.

**Problem:** Git does not track empty directories, so `.gitkeep` files were added throughout the
tree. They added noise without providing executable structure, made reviews larger, and could make
the scaffold appear more complete than it was.

**Resolution:** Remove placeholder `.gitkeep` files. Create directories when the first real source,
configuration, or migration file is added. The architecture document remains the source for the
planned structure.

**Takeaway:** Use `.gitkeep` only when an empty runtime directory must exist after clone. Do not use
it to materialize a speculative source-code folder tree.
