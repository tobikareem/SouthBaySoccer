---
name: brand-design-kit
description: Use whenever creating ANY visual or written deliverable for the Pickup Soccer LLC project — Word docs, PDFs, slide decks (pptx), HTML/UI mockups, generated images, social graphics, or email/announcement layouts. Applies the green-and-white Nigerian-flag brand: color palette, typography, spacing, and motif. Trigger any time output should "look on-brand" for the soccer group.
---

# Pickup Soccer — Brand & Design Kit

Apply this brand to every deliverable made for the Pickup Soccer LLC project. The identity is the **Nigerian flag**: green and white, clean and energetic.

## Color Palette

| Role | Name | Hex | Use |
|------|------|-----|-----|
| **Primary** | Flag Green | `008751` | Headers, primary buttons, key fills, brand bar. The dominant color (~50–60% of color weight). |
| **Depth** | Pine | `005C37` | Hover states, gradients-via-layering, dark section backgrounds, footers. |
| **Accent** | Spring | `1FB573` | Highlights, active states, chart accents, small callouts. Use sparingly. |
| **Tint** | Mist | `E8F5EE` | Card backgrounds, table zebra rows, subtle fills on white. |
| **Base** | White | `FFFFFF` | Dominant background. Negative space is part of the brand — keep it generous. |
| **Ink** | Charcoal | `14241B` | Body text on light backgrounds. Never pure black. |
| **Muted** | Sage Gray | `5B6B62` | Captions, secondary text, footnotes. |

Rules:
- **60 / 30 / 10**: ~60% white space, ~30% green, ~10% accent. White dominates; green frames and anchors.
- On green backgrounds, text is **white** (`FFFFFF`); on white backgrounds, text is **Charcoal** (`14241B`).
- Never place Flag Green text directly on Pine, or Charcoal on green — keep contrast high.
- Do not introduce other hues (no blues, oranges). Status colors are the only exception: success = Spring `1FB573`, warning = `D98E04`, error = `C0392B`.

## Typography

| Context | Header font | Body font |
|---------|-------------|-----------|
| App / web UI (MAUI/Blazor) | Segoe UI Semibold / Inter | Segoe UI / Inter |
| Documents & decks | Georgia (or Cambria) | Calibri |

Sizes: deck title 36–44pt bold; section header 20–24pt bold; body 14–16pt; captions 10–12pt muted. In UI, follow an 8pt type scale (12/14/16/20/24/32).

## Motif

- **The green bar**: a solid Flag Green vertical bar (~0.25") down the left edge of content slides/pages, echoing the flag's side stripe. Reuse it as the single repeating brand element.
- **The ball**: a simple soccer ball / pentagon mark in green or white as the logo glyph.
- Optional flag echo: a three-band layout (green | white | green) for hero/cover sections only — don't overuse.
- Rounded corners 8–12px on cards and buttons; soft shadows (low opacity, ~0.15) for depth.

## Application Notes

- **pptx / docx / pdf**: dark-green cover and closing slide, white content slides ("sandwich"). Left green bar on content slides. Tint (`E8F5EE`) for cards and table header rows; Flag Green for table header fills with white text.
- **HTML / UI mockups**: white canvas, Flag Green primary buttons, Pine on hover, Mist card surfaces, Charcoal text. Use CSS variables: `--brand: #008751; --brand-dark: #005C37; --accent: #1FB573; --tint: #E8F5EE; --ink: #14241B; --muted: #5B6B62;`
- **Images / graphics**: prefer green/white compositions; avoid clashing backgrounds.

## Do / Don't

- Do keep generous white space; do anchor each layout with one green element; do carry the left green bar across a set.
- Don't fill whole backgrounds with saturated green (use Pine for dark sections instead); don't add accent lines under titles; don't mix in off-brand colors; don't use pure black text.

## Quick Checklist (before shipping any deliverable)

1. White-dominant with green anchoring? 
2. Correct hex values from the table above?
3. High-contrast text (white-on-green / charcoal-on-white)?
4. Left green bar or ball motif present?
5. On-brand typography and generous spacing?
