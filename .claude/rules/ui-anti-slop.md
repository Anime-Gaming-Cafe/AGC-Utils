---
paths:
  - "**/*.razor"
  - "**/*.cshtml"
  - "wwwroot/**"
  - "DESIGN.md"
---

# Dashboard UI: hard rules

These apply to every change to the dashboard UI. `DESIGN.md` is the source of truth for tokens, palette,
radii, components and the documented exceptions. Read it before building or restyling anything.

## Standing rules

- No em or en dashes, in UI text, docs or messages.
- Mobile friendly is mandatory. Check every layout at about 400px: no horizontal page scroll, and nothing
  that only works on hover.
- No vibecoded AI UI. Reuse the components in `Pages/SharedPages/` and the classes in
  `wwwroot/css/app.css` first. Change a shared component rather than adding a page-local variant.
- User-facing strings are German.

## Colour and surfaces

- No generic blue-purple, blue-cyan or purple-pink gradients, no full-page glow, no rainbow or neon or
  pastel palettes, no blurred orbs.
- Glassmorphism on at most 1 to 2 elements (currently zero).
- No excessive border radius, nothing pill-shaped by default. Use the small radius set: `--r-control`,
  `--r-surface`, and 50% only for avatars.
- Shadows only as an elevation marker, with the reason written down (currently two overlay elements).
- Glow on at most 1 to 2 elements (currently zero).
- No background grids.
- No dark mode without a reason. The reason for this dashboard is recorded in `DESIGN.md`.
- 2 to 3 core colours plus 1 accent. The accent appears only in its four documented places, not
  everywhere.
- No sterile default look.

## Layout

- No monotonous template layout, copy-paste feature cards, bento grids, uniform spacing or uniform
  section rhythm.
- No forced "How it works" three steps, fake "Trusted by" logo bars, "Most popular" pricing cards, demos
  without a product, or 4-column template footers.

## Decoration

- No generic AI icons and no Lucide-style uniform icon look. The icon set is a decision: inline SVG via
  `AppIcon`, only the symbols actually in use.
- No emoji as decoration, no arrows on every button.
- No decorative coloured left stripe. It is only allowed when it carries real state.
- No AI capsule badges.
- No generic AI typography: no monospace headings, no letter-spaced uppercase. A font needs a brand
  reason. Monospace only for ids.
- No fake terminal windows, no unrelated illustrations.

## Structure and data

- No dead navigation and no non-functional controls. A nav entry must match the role gate of its page.
- No template filler sections and no default dashboard shell (sidebar, 4 stat cards, chart, table).
- No stat cards with invented numbers or deltas. Numbers come from real queries.
- No filler activity feeds, no charts without a question, no generic table columns, no filler data such
  as John Doe.
- Empty states say why the screen is empty and offer the next action. Loading states say what is
  loading. Errors say what failed and offer a retry where that makes sense. Nothing spins forever.

## Motion

- No endless pulses or loops. The loading spinner is the only exception.
- No stacked template animations. Respect `prefers-reduced-motion`.
