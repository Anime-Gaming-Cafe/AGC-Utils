# Design system

This file is the reference for every visual decision in the web dashboard. If a rule here and the code
disagree, the code is wrong. Repo convention applies: user-facing strings are German, everything here is
English.

## Design read

**Product.** AGC Utils dashboard, the web side of the bot that runs the *Anime Gaming Cafe* Discord
(`discord.gg/animegamingcafe`), served at `dashboard.animegamingcafe.de`.

**Audience.** Roughly two dozen staff members (BotOwner, Administrator, HeadModerator, Moderator, Supporter,
HeadEventmanager, Team), plus every server member on the applicant-facing and leaderboard screens.

**The job.** Nearly every screen does the same thing: *find the record that needs a decision, decide, move on.*
Configuration screens do one other thing: change a setting and be certain it took. That is the hierarchy every
layout answers to.

This is not a marketing surface. There is no hero, no pricing, no testimonials, no logo bar, and none are
coming. Sections exist because the work needs them.

## Dials

| Dial | Value | What that commits us to |
|---|---|---|
| RHYTHM | 2 | Sections differ by the weight of their content, not by decoration. A screen with one important list and three side facts must look like that. |
| MOTION | 1 | Hover, focus and state changes only. No entrance animations, no loops, no floating. The loading spinner is the single exception, because it reports real progress. |
| DENSITY | 3 | Staff scan lists. Rows stay compact, whitespace goes between groups rather than inside them. |

## Theme

**Dark, fixed.** The dashboard is used in the same sitting as the Discord client, often for hours, and the same
records are read there on a dark surface. That is a reason from the working context, not a trend. There is no
toggle because there is no second context to serve.

There is no background image. Surfaces carry real colours so they can carry hierarchy.

## Colour

Two core scales, one accent, three signals.

```
--bg               #14171a   page ground
--surface          #1b1f23   panel
--surface-raised   #22272c   nested panel, table head
--border           #2d343a   hairline
--border-strong    #3b444c   control border

--fg               #e6eaed   primary text
--fg-muted         #9aa5ad   secondary text
--fg-subtle        #6e7a83   timestamps, meta
```

**The accent is the bot's own colour.** `_Host.cshtml` writes `--accent-rgb` at render time from
`BotConfig.GetEmbedColor()` (`[EmbedConfig] DefaultEmbedColor`, currently `2F84A2`). The dashboard and the
embeds the team reads in Discord therefore always match, and changing the config changes both.

The accent appears in **exactly four places**: the primary button fill, the focus ring, the active navigation
item, and selection (a selected row, a checked control). Nowhere else: no accent headings, no accent borders, no accent backgrounds. An
accent that is everywhere is not an accent.

**Signals** (`--ok`, `--warn`, `--bad`) do not widen the palette; they carry information. They appear only on
status pills, on the left edge of an application row, and in notice boxes. An element with no state gets no
signal colour.

### Contrast

Target: 4.5:1 for text, 3:1 for controls and graphical objects.

`#2F84A2` has a relative luminance of 0.197. White on it gives **4.25:1**, short of AA for body text. Dark
text on it gives **4.94:1**. So `--on-accent` is `#0d1216`, not white. Used as *text* on `--bg` the raw accent
reaches only 4.24:1, which is why `--accent-strong` (lightened via `color-mix`) exists; the focus ring uses it.

**Links do not use the accent.** In running text a link is `--fg` with an underline, because the underline is
the affordance and the accent has no fifth job. Unread records, loading spinners and neutral notices are not
accent-coloured either - they read through brightness, not hue.

## Radius

Three values, each with a job.

| Token | Value | Applies to |
|---|---|---|
| `--r-control` | 4px | buttons, inputs, selects, pills; small reads as "this is a control" |
| `--r-surface` | 8px | panels, table containers |
| (none) | 50% | avatars only |

No pill-shaped buttons. Radius is a hierarchy tool here, not decoration.

## Elevation

**Flat by default.** Surfaces separate through a background step plus a 1px border, not through shadow.

Exactly two elements carry `--shadow-overlay`, because they genuinely float above the page and have to read as
detached: the user dropdown and the error bar Blazor pins to the bottom of the viewport. Nothing else. The
top bar stays flat too: a border separates it from the page, and on small screens its menu expands the bar
itself instead of floating a drawer over the content.

## Glass and glow

**Both are zero.** Translucency had nothing left to reveal once the background photo went, and it flattened
every surface into the same frosted layer. The coloured `box-shadow` rings under every button were an
attention amplifier applied everywhere, which amplifies nothing.

`backdrop-filter` must not appear in this codebase. A coloured `box-shadow` must not appear either.

## Motion

```
--t-fast  120ms
--t-base  180ms
easing    ease-out
```

Colour changes on hover and focus, and disclosure open/close. That is the whole list.
`prefers-reduced-motion: reduce` sets both durations to 0.

## Typography

**Inter**, kept deliberately: the dashboard is mostly dense tables with ID, level and timestamp columns, so
`font-variant-numeric: tabular-nums` matters, and the family is already self-hosted in `wwwroot/fonts/Inter`.
It stays because it fits, not because it was already there.

Four weights ship (400, 500, 600, 700), each with `font-display: swap`. The other five faces were loaded and
never selected by a single rule; they are gone.

No letter-spaced uppercase labels. No monospace as an aesthetic: monospace appears only on IDs and case IDs,
where reading character by character is the actual task.

Scale: `12 / 13 / 14 / 16 / 20 / 24`. Hierarchy comes from weight and size together, not from colour.

## Spacing

`4 / 8 / 12 / 16 / 24 / 32 / 48`.

Inside a panel 16, between panels 16, between page sections 32, page top 24. Spacing is structure: a single
repeated value would remove rhythm, so these levels are used for what they mean.

## Icons

One inline SVG sprite holding only the symbols actually in use, each chosen for what it says about the thing
it marks. This replaced three icon fonts (open-iconic, Bootstrap Icons, a Font Awesome kit) that together
shipped for 22 icons, two of them over external CDNs. For a self-hosted bot dashboard that was a dependency
problem as much as a visual one.

No emoji in UI text.

## Focus

`:focus-visible` is defined once, globally, and is never removed anywhere. The previous system disabled it on
every button with `outline: none !important` plus `box-shadow: none !important`, which made the dashboard
unusable by keyboard.

## States

Every screen that loads data declares all three states, and the `StateBlock` component enforces what each one
has to say:

- **Loading** names what is loading. Not "Loading…".
- **Empty** gives the reason it is empty *and* the one action that fills it. "First run", "filtered to
  nothing" and "no permission" are different screens and read differently.
- **Error** says what failed and what to do next.

## Data

Numbers shown are numbers queried. No placeholder rows, no invented names, no trend deltas without a real
comparison period behind them. An honest empty state beats a populated-looking screen.

## Navigation

One top bar, no sidebar. The navigation has a single level with at most six entries for staff; everything
deeper lives on the hub pages (Teambereich, Logging, Administration). A sidebar spent 248px of every screen on
that. Personal destinations, Meine Bewerbungen and Einstellungen, sit in the account menu under the avatar.
Below 1000px the entries collapse behind a menu button. The bar shares the content's max width and side
padding, so its left edge lines up with the page beneath it.

## Components

Shared components live in `Pages/SharedPages/` and are picked up automatically by `_Imports.razor`.

`PageHeader` · `Panel` · `StateBlock` · `Pager` · `StatusPill` · `ApplicationStatusPill` · `ApplicantStatusPill` · `ConfirmInline` · `CopyId` · `SeenByAvatars` · `AppIcon`

Before adding a page-local variant of one of these, change the component instead.

## Exceptions on record

- **The coloured left edge on application rows** encodes the application's real status (new, read,
  shortlisted, accepted, rejected, withdrawn). It is a signal, not a stripe added to make a card look
  designed, and it stays.
- **The loading spinner** loops forever by nature. It is the only perpetual motion allowed, and it reports
  real progress.
- **The panel preview** on the panel editor imitates Discord's own embed rendering. Its surface and text
  colours are Discord's, and its left bar is the panel embed's colour (`DiscordColor.Gold`), not the dashboard
  accent. It exists to show the team what members will see, so fidelity to Discord wins over the palette there.
- **The signed-in user's own row on the leaderboard** carries the accent tint. It is that user's selection on
  the list, which is one of the accent's four places.
- **The active-warning count in "Für dich"** (`rows__title--bad`) is `--bad-text` on plain row text, not a
  pill. Deliberate call: it is the one summary line whose whole point is "this needs your attention", so the
  colour is on the sentence itself, not tucked into a badge next to it.
