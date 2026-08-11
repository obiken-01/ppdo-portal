# PPDO Portal — Design System

The reference for building or updating any page. Read this before writing UI; it is the one place
that answers "what should this look like?" so you don't have to reverse-engineer it from whichever
page you happened to open.

Companion docs: `PERFORMANCE_GUIDELINES.md` §6-7 (loading states, images),
`NAMING_CONVENTIONS.md` (file and component naming), `CLAUDE.md` (frontend architecture rules).

> **Status:** the token layer is mature and reviewed, and as of 2026-08-10 its application is too —
> all 7 divergences §7 originally found (Inventory having diverged first, then drifted from Budget
> Planning and Config) are resolved. What's left is two open decisions, not drift: `lucide-react`
> (adopt or remove) and dark mode (finish or remove). §7 keeps the full history — what changed, what
> was deliberately left alone, and why — for anyone touching these pages next.

---

## 1. Colour tokens

All colours come from `frontend/tailwind.config.ts`. **Never hardcode a hex value in a component.**

### Green — primary brand

| Token | Hex | Use for |
|---|---|---|
| `green-950` | `#071F12` | Deepest accents |
| `green-900` | `#0F4526` | |
| `green-800` | `#13512D` | |
| `green-700` | `#196638` | Login header, landing hero |
| `green-600` | `#1F7A45` | **PRIMARY** — sidebar, primary buttons |
| `green-500` | `#2E9958` | Hover state on primary buttons |
| `green-400` | `#3BAD6A` | Progress bars, dots, badges |
| `green-300` | `#6DC492` | |
| `green-200` | `#A8DABC` | Borders on green elements |
| `green-100` | `#D4EDDE` | |
| `green-50` | `#F0FAF4` | Table hover, icon backgrounds |
| `green-25` | `#F7FCF9` | Faintest tint |

### Slate — neutrals

The PPDO slate ramp is **neutral warm grey**, not Tailwind's blue-tinted stock slate. Accessibility
reasoning here is load-bearing (RAL-133) — read the whole table before picking a shade.

| Token | Hex | Contrast on white | Use for |
|---|---|---|---|
| `slate-800` | `#343A40` | ~11:1 | Headings, footer, strongest text |
| `slate-600` | `#5A636B` | ~6.1:1 | **The only AA-safe token for readable content** — body text, labels, helper text |
| `slate-500` | `#7D858C` | ~3.8:1 | Secondary/decorative only — **never** label or body text |
| `slate-400` | `#ADB5BD` | fails AA | Disabled/inactive controls only (WCAG contrast minimum doesn't apply to disabled) |
| `slate-300` | `#C7CFD6` | fails AA | Decorative only — dividers, chevrons |
| `slate-200` | `#E9ECEF` | — | Input and card borders |
| `slate-100` | `#F1F3F5` | — | Page background |
| `slate-50` | `#F8F9FA` | — | Zebra rows |

> ### ⚠️ `slate-700` is not a PPDO token — migrated, keep it that way
>
> There is **no `slate-700` in `tailwind.config.ts`**. Any use silently resolves to stock Tailwind
> `#334155` — which is *blue-tinted*, unlike every shade in the PPDO ramp above. It is not a
> contrast failure (it's very dark), so this is a **palette-coherence** bug, not an accessibility
> one: headings drift blue while the surrounding text stays neutral grey.
>
> **Use `slate-800` for headings and `slate-600` for body text. Never `slate-700`.**
> This is the same class of bug RAL-133 fixed for `slate-400`/`slate-500` — an undefined shade
> falling back to an unreviewed stock value.
>
> ✅ **Migrated 2026-08-10 (§7 item 1).** All 215 uses across 46 files are gone; the compiled CSS
> emits no `text-slate-700` rule at all. Reintroducing one is a regression — grep before merging.

### Status colours

| Token | Hex | Meaning |
|---|---|---|
| `amber-500` / `amber-100` | `#EF9F27` / `#FEF3CD` | Warning, partial delivery — text / pill background |
| `danger-500` / `danger-100` | `#E24B4A` / `#FDECEA` | Danger, out of stock |
| `info-500` / `info-100` | `#378ADD` / `#E3F2FD` | Informational, open PR |

Use `green-*` for success. There is no separate success token.

### Surface tints

**Stat card backgrounds** — `stat-blue` `#EBF4FF`, `stat-amber` `#FEF9EC`, `stat-green` `#F0FAF4`,
`stat-red` `#FEF2F2`, `stat-purple` `#F3F0FF`.

**Excel-like cells** — `cell-fill` `#FFFDE7` (user fills in), `cell-auto` `#F1F3F5` (auto-filled),
`cell-green` `#F0FAF4` (system-generated). These encode meaning in the PR/WFP entry grids; don't
reuse them decoratively.

### The shadcn CSS-variable layer

`globals.css` defines HSL variables (`--background`, `--foreground`, `--primary`, `--border`,
`--radius: 0.5rem`, …) that `tailwind.config.ts` maps to `background`, `primary`, `border` etc.
These exist for shadcn/Radix component compatibility.

**Prefer the explicit PPDO tokens** (`bg-green-600`, `text-slate-800`) over the semantic aliases
(`bg-primary`, `text-foreground`) in application code. The aliases are indirection with no
benefit here, and they make it harder to see what colour you're actually getting.

> **Dark mode is scaffolded but not implemented.** `darkMode: ["class"]` is set and `globals.css`
> has a full `.dark` block, but nothing toggles it and no page has been reviewed in it. Treat dark
> mode as **not supported** — don't rely on it, don't half-maintain it. Removing it or finishing it
> is an open decision.

---

## 2. Typography

Font stack: `Segoe UI, Source Sans Pro, system-ui, sans-serif` (`font-sans`). No web font is
loaded — this is deliberate, it costs zero bytes and matches the Windows environment the office
actually uses.

**Canonical scale.** Use these; §7 tracks the pages that don't yet.

| Role | Classes |
|---|---|
| Page title (`h1`) | `text-xl font-bold text-slate-800` |
| Page description | `text-sm text-slate-600` |
| Section heading (`h2`) | `text-sm font-semibold text-slate-800` |
| Card/stat label | `text-xs font-semibold text-slate-600 uppercase tracking-wide` |
| Body / table cell | `text-sm text-slate-600` |
| Modal title | `text-base font-semibold text-slate-800` |
| Numeric value | add `tabular-nums` — always, so digits align in columns |

`text-xl` for `h1` is the confirmed standard (Ralph's call, 2026-08-07). It matches the Inventory
dashboard and the Budget Planning sub-pages, which are the majority of real pages.

> ⚠️ **`ConfigPageHeader` currently emits `text-lg`** (`ConfigPageHeader.tsx:26`), so it does not
> yet match this rule. Using the component — which §5 tells you to do — gives you `text-lg` until
> that one line is changed. Changing it fixes all 7 Config pages at once. Tracked as §7 item 3.

---

## 3. Shape, borders, elevation

**The portal is flat.** No rounded corners on cards, panels, tables, buttons, inputs, or modals.

- Borders: `border border-slate-200`
- Card surface: `bg-white border border-slate-200`
- Shadows: avoid. `shadow-lg` on toasts and modal overlays only — things that float above the page.

**`rounded-full` is exempt** and correct for pills, badges, status chips, avatars, and step
indicators (109 uses). "Flat" means no softened rectangles, not no circles.

`rounded-lg` / `rounded-xl` / `rounded-md` / `rounded-sm` on portal surfaces are **violations** —
see §7. The `borderRadius` scale in `tailwind.config.ts` exists only for the shadcn variable layer.

> **The public site is deliberately different.** `(public)/` pages — landing, about, services,
> contact, login, reconnecting — use rounded cards and softer styling. They speak to citizens, not
> staff, and were designed that way. **Do not flatten them to match the portal.** The flat rule
> applies to `(portal)/` only.

---

## 4. Page shell

Every portal page uses this outer structure:

```tsx
<div className="min-h-full bg-slate-100">
  <div className="max-w-6xl mx-auto px-3 py-4 sm:px-6 sm:py-6 space-y-4">
    <ConfigPageHeader title="…" description="…" actions={<>…</>} />
    {/* page content */}
  </div>
</div>
```

- **`bg-slate-100`** on the outer wrapper — the page background, not white.
- **`max-w-6xl mx-auto`** so content doesn't stretch across ultrawide monitors.
- **Responsive padding** `px-3 py-4 sm:px-6 sm:py-6`. The `sm:` step is not optional — RAL-201
  fixed real mobile squishing across the Config pages, and a flat `p-6` regresses it.
- **`space-y-4`** between top-level blocks.

✅ **Adopted portal-wide 2026-08-10** (§7 item 2). The original "4 variants" undercounted —
the audit sampled the Inventory dashboard but missed that all 8 Inventory sub-pages shared a
variant of their own (`min-h-screen bg-slate-100 font-sans` / `max-w-screen-xl` — already
responsive, just the wrong height unit and width), and mischaracterized `admin/users` and the
Budget Planning dashboard, both of which were already close to this exact target. See §7 item 2
for what changed per family and what was deliberately left alone.

---

## 5. Component inventory

Everything in `frontend/src/components/ui/`. Check here before building anything.

| Component | Use it for | Do NOT use it for |
|---|---|---|
| `DataTable` | Any tabular list. TanStack-backed; supports client or server pagination/sorting. Pass `minWidth` so `overflow-x-auto` engages on mobile (RAL-201). | Non-tabular card lists |
| `TableSkeleton` | Loading state for a page whose content is a table. Pass the real column list. | Pages that aren't a single table |
| `LoadingState` | Only pages with **no** known final shape. Its own doc comment excludes table/form pages. | Anything with a table or form — use `TableSkeleton` or a bespoke skeleton matching the real layout |
| `ConfigPageHeader` | **Every** portal page title. Handles the mobile stacking. Name is historical — it is not Config-only. | — |
| `Toast` | Success confirmations; API errors on a *completed* action | Form validation; errors inside an open modal — those go inline |
| `Modal` | Dialogs | — |
| `ConfirmDialog` | Destructive confirmations | Informational messages — use `MessageDialog` |
| `MessageDialog` | Informational acknowledgement | Confirmations |
| `Lookup` | Typeahead against a large dataset (e.g. price index) | Small fixed lists — use a plain `<select>` |
| `OfficeSelect` | Office picker | — |
| `MoneyInput` | Peso amounts. Handles formatting and precision. | — |
| `CsvUploadButton` / `CsvDownloadButton` | CSV import/export triggers | — |
| `RowActions` | Any table's per-row action buttons. See §6a for the layout rule. | A single always-visible primary action with no alternatives — a plain button is enough |

**Adoption today:** `Toast` 32 files, `DataTable` 10, `ConfigPageHeader` 7, `TableSkeleton` 6,
`RowActions` 11 (`ldip`, `aip`, `items-master`, `pr-register`, `resource-links`, and 6 config
pages — `accounts`, `offices`, `divisions`, `funding-sources`, `price-index`,
`procurement-presets`), `Lookup` 3, `LoadingState` 1. Fully converted as of 2026-08-11 — the 6
config pages' local `TextAction` helper (an underlined-text-link style, duplicated identically in
each file) was deleted entirely once nothing referenced it.

### Loading states

Match the loaded layout — a spinner replaced by a full table causes layout shift
(`PERFORMANCE_GUIDELINES.md` §6, real CLS regression fixed in RAL-192). Render the shell and header
immediately; skeleton only the data region.

### Icons

**Use emoji.** They need no dependency, render consistently on Windows, and are already the
established pattern (`📦` Inventory, `📋` Create PR, `🚚` Receive Delivery, `📊` Report, `🏭`
Warehouse, `📢` Announcements).

> `lucide-react` is a declared dependency with **zero imports anywhere in the codebase**. Either
> adopt it deliberately and convert consistently, or drop it from `package.json`. Don't leave it
> half-present — and don't start importing it into one page, which would give us two icon systems.
> Decision pending (§7).

---

## 6. Buttons

| Variant | Classes |
|---|---|
| Primary | `px-3 py-2 bg-green-600 hover:bg-green-500 text-white text-sm font-medium transition-colors` |
| Secondary | `px-3 py-2 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-sm font-medium transition-colors` |
| Destructive | `px-3 py-2 bg-danger-500 hover:opacity-90 text-white text-sm font-medium transition-colors` |

No rounding. Always `transition-colors`. Disabled state: `disabled:opacity-50
disabled:cursor-not-allowed`, and use `slate-400` for any disabled text.

There is no `Button` component — these are inline classes. Extracting one is a reasonable future
cleanup, but until then copy the strings above exactly rather than improvising.

### 6a. Row actions (`RowActions`)

Table rows with more than one action button had drifted into four different styles: `items-master`
and `resource-links` used bare icons (✓/✏️, ✏️/🗑️), `pr-register` used bordered chips, `ldip` and
`aip` used underlined text links, and the 6 config pages shared a local `TextAction` helper — the
same underlined-text-link style, copy-pasted identically into each file. `RowActions` replaced all
of it 2026-08-11; the `TextAction` helper was deleted from every config page once nothing
referenced it.

**Every button in one row renders at the same fixed width — not stretched, not shrunk to its own
text.** Two earlier versions got this wrong, both caught live by Ralph before they spread past
`ldip`: a CSS-grid version with `w-full` stretched short labels like "View" into an oversized box;
a `flex-wrap` version with natural per-button width made every button a different size and wrapped
unpredictably. Fixed width is what actually reads as one component instead of several buttons that
happen to sit near each other — worth remembering before reaching for either alternative again.

**`btnWidth` prop, default 80px.** 80px fits `ldip`/`aip`'s longest labels ("Finalize" /
"Archive") without clipping — every page above uses the default *except* `pr-register` (`116`;
"Mark Completed" is 14 characters, the longest real label anywhere in the app).

Don't widen the shared default to cover one page's outlier label — every other page would carry
unnecessary padding for a word they never show. Add a row below whenever a new page needs a
non-default width, so the reasoning doesn't have to be re-derived from the button text alone.

**`btnPaddingX` prop, default `"px-1.5"`.** The 6 config pages ("Deactivate" / "Reactivate", 10
characters) first got a wider `btnWidth={92}`, then Ralph asked to keep the default 80px width and
tighten the padding instead — trading inner padding for text room rather than widening the button.
They pass `btnPaddingX="px-1"`.

| Page | `btnWidth` | `btnPaddingX` | Why |
|---|---|---|---|
| `pr-register` | `116` | default | "Mark Completed" (14 chars) needs more width, not less padding |
| 6 config pages | default | `"px-1"` | "Deactivate"/"Reactivate" (10 chars) fit the default 80px once padding is tightened |

**Wrapping.** `ldip` and `aip` are the only pages that ever show 3 actions on one row (both
status-gated: Draft → Edit + Finalize + Archive). Ralph tried both a 2-per-row wrap and a single
row live and preferred **all 3 on one line** — the component is `flex justify-end`, no wrapping
logic at all. The table's own horizontal scroll absorbs a wide Actions column the same as it would
any other wide row. 5+ actions want a primary action + overflow menu instead — **not built**.
Nothing in the portal hits this today; build the overflow menu when a real page needs it, don't
reach for wrapping again — it was tried and explicitly rejected.

**Variants:** `default` (neutral bordered — Edit/View/Mark Completed), `primary` (green — the
forward action, e.g. Finalize, View Report), `warn` (amber — a reversible-but-notable action, e.g.
Archive/Unlock/Unmark/`items-master`'s Review), `danger` (the `danger-*` tokens — genuinely
destructive or restrictive, e.g. `resource-links`' Delete and the config pages' Deactivate).
Deactivate kept `danger` deliberately: the pre-conversion code had explicitly marked it
`<TextAction danger>` while Reactivate carried no such marking, so the distinction was preserved
rather than re-decided during the refactor. Give the `actions` column `align: "right"` on
whichever table component you're using — `RowActions` right-aligns its own content, and an
unaligned header reads as a mismatch against it (the same bug found and fixed in
`items-master`'s and `pr-register`'s Actions columns).

Supports either `href` (renders a `Link`) or `onClick` (renders a `button`) per action — `ldip`'s
Edit/View are navigation, Finalize/Archive/Unlock are handlers, and both need to sit in the same
row. `disabled` and `loading` both map onto the rendered `disabled` state (`loading` also shows a
spinner) — pass both independently rather than pre-combining them, e.g. `pr-register`'s Mark
Completed button is `disabled: anyBusy, loading: isCompleting`.

**Not yet converted:** `admin/users` (icon-only `ActionButton` helper, same shape `items-master`
and `resource-links` had) and `announcements` (a hand-rolled Edit action) both have real per-row
actions that were simply not part of this pass — not a deliberate exception, a genuine gap.
`account` has no table rows at all, so it was never a candidate.

---

## 7. Known divergences and migration targets

Inventory shipped first (v1.0/v1.1) and set patterns that later features didn't follow; Config was
refactored most recently (RAL-201) and is closest to the target. This table is the backlog for
making them uniform.

| # | Divergence | Current state | Target | Priority |
|---|---|---|---|---|
| 1 | ~~**`slate-700`**~~ | ✅ **Done 2026-08-10** — all 215 uses across 46 files migrated; compiled CSS emits no `text-slate-700` rule | `slate-800` headings/buttons/emphasis, `slate-600` body | — |
| 2 | ~~**Page shell**~~ | ✅ **Done 2026-08-10** — see below for what changed and what was left alone | §4 shell | — |
| 3 | ~~**`h1` size**~~ | ✅ **Done** (PR #214) — `ConfigPageHeader.tsx:26` and both dashboards use `text-xl` | **`text-xl`** | — |
| 4 | ~~**`h2` styling**~~ | ✅ **Done 2026-08-10** — 14 portal section headings on target; both dialog titles on `text-base`. Remaining variants are the deliberate exceptions listed below | `text-sm font-semibold text-slate-800`; modals `text-base font-semibold` | — |
| 5 | ~~**`ConfigPageHeader` adoption**~~ | ✅ **Done 2026-08-10** — adopted by the Inventory dashboard, the BP dashboard, and the AIP/LDIP/Allocation/Report/WFP pages. `title`/`description` widened `string` → `ReactNode` so WFP's inline promo badge and status pill didn't have to be flattened or moved into `actions`. Not adopted by wizards or per-record detail views — see exceptions below | Use it everywhere | — |
| 6 | ~~**Inventory sub-pages have no `h1`**~~ | ✅ **Done 2026-08-10** — the 5 audited (`items-master`, `item-ledger`, `pr-register`, `pr-report`, `distribution`) plus `create-pr` and `receive-delivery`, which the audit missed and Ralph caught live. All 7 now render a `ConfigPageHeader` | Add `ConfigPageHeader` | — |
| 7 | ~~**Rounded corners in portal**~~ | ✅ **Done 2026-08-10** — 30 occurrences across 10 files, not the 6 originally counted (see below) | Remove (keep `rounded-full`) | — |
| 8 | **`lucide-react` unused** | Declared dependency, 0 imports | Decide: adopt or remove | Low |
| 9 | **Dark mode** | Scaffolded, unimplemented, unreviewed | Decide: finish or remove | Low |

### Deliberately NOT unified

Don't "fix" these — the difference carries meaning:

- **Public vs portal styling.** `(public)/` pages are rounded and softer on purpose (§3).
- **`rounded-full` pills and badges.** Not a flat-design violation.
- **`text-xs uppercase tracking-wide` micro-labels** in `import-preview` and the filter panels.
  These are *field labels*, a different semantic from section headings — keep them.
- **Wizard chrome.** WFP Entry and AIP New have step indicators and multi-pane layouts that list
  pages have no use for.
- **Denser headings on dashboards.** A dashboard packs many small widget headings; a list page has
  one or two. Dashboards may keep `text-sm` section headings where a detail page uses more space.
- **Bespoke skeletons** in `pr-report` (3-section report) and `distribution` (stat card + card
  list). Neither is a single table, so `TableSkeleton` genuinely doesn't fit.
- **Config tile titles** (`config/page.tsx`, `text-base font-semibold`). These are *card titles* in
  a tile grid, not section headings — shrinking them to `text-sm` would flatten the landing page's
  visual hierarchy. Added as an explicit exception when item 4 was migrated.
- **Full-page success screens** (`create-pr`, `receive-delivery`, `text-lg font-bold`). §7 item 4
  originally filed these under "modals", but they are neither modals nor section headings: they are
  the sole message on an otherwise empty `min-h-screen` confirmation page. Demoting them to
  `text-base font-semibold` would weaken the one thing the page exists to say. Left as-is.
- **Per-record detail/edit views** (`aip/detail`, `ldip/LdipForm`, and both `import-preview` pages)
  keep their hand-rolled header. Each computes its title from the record (`"AIP FY {year}"`, a
  filename, `record.refCode`) and renders a `StatusBadge` beside it — a materially different shape
  from the "list page header" §7 item 5 targeted, closer to a detail-view header than a page title.
  Not converted when item 5 shipped.
- **`account`, `announcements`, `admin/users`.** Outside item 5's stated scope of "Inventory and
  Budget Planning hand-roll the same markup" — left alone, not audited. `admin/users` still renders
  no page title at all, same gap item 6 fixed for Inventory; a candidate for a future finding, not
  claimed as done here. (Item 2 did bring its outer shell to target — shell and header adoption are
  separate concerns.)
- **`config/audit-log`'s `max-w-7xl`.** Wider than the `max-w-6xl` every other page uses. Not
  flattened when item 2 shipped — an audit trail table has more columns (timestamp, actor, table,
  action, description) than a typical config list, and the extra width looked like a deliberate
  choice, not drift. Everything else about its shell (height unit, responsive padding) was still
  brought to target.
- **`allocation` and `wfp`'s internal spacing.** Item 2 normalized their outer `max-width` and
  padding to target, but left their existing `flex flex-col min-h-full` / `flex-1` wrapper and
  scattered `mb-*` margins between sections untouched, rather than converting to `space-y-4`. Both
  pages branch into several tabs/panels beyond the simple header→filter→table shape `aip` and `ldip`
  have, and a blind sweep for every sibling margin with no way to render and check the result was a
  worse trade than leaving their internal rhythm as-is. Revisit with a live visual check, not blind.

### Sequencing note

Items 1 through 7 are done. Only the two open decisions (**8**, **9**) remain.

Item 7 (2026-08-10) undercounted the same way item 2 did: the audit's "6 files" only caught
`rounded-lg`/`rounded-xl`/`rounded-sm`, missing bare `rounded` (Tailwind's default radius) entirely
— the exact same violation, just a different utility name. The real count was **30 occurrences
across 10 files**: the 6 originally named, plus `item-ledger`, `pr-register`, `resource-links`, and
two more in `dashboard/page.tsx` split across a multi-line `className` a same-line grep couldn't
see. All 30 were buttons, inputs, a `<kbd>` hint, a notification chip, or card containers — pure
class-token removal, no restructuring, so unlike item 2 this carried no meaningful visual risk to
weigh even without a screen to check it against. `StatCard.tsx` went first, per the ticket's own
note that it's shared (Main Dashboard + Inventory Dashboard both consume it).

Item 2 (2026-08-10) split into three tiers by risk, since none of it could be visually verified in
the session that did it (no live backend, no compositing browser):

- **Fully converted to the two-div target shell**, `space-y-4` replacing manual margins: the
  Inventory dashboard, and `aip`/`ldip` (clean 3-block header→filter→table structure, low risk).
- **Additive-only fixes** — `min-h-screen` → `min-h-full`, flat `p-6`/`px-6 py-6` → responsive
  `px-3 py-4 sm:px-6 sm:py-6`: the 7 Config pages, `admin/users`, the Budget Planning dashboard, and
  all 8 Inventory sub-pages (which already had responsive padding and `space-y-*`, just the wrong
  height unit and a nonstandard `max-w-screen-xl`/`max-w-screen-lg`). Purely additive — mobile
  breathing room only, nothing shrinks — so safe to ship without a screen to check it against.
- **Width/padding only, structure untouched**: `allocation` and `wfp` — see "Deliberately NOT
  unified" above for why their internal spacing wasn't swept into `space-y-4` alongside the rest.

Two `max-width` shrinks (`max-w-screen-xl` 1280px → `max-w-6xl` 1152px, a ~10% reduction) were
checked against overflow risk before applying, not assumed safe: `aip`/`ldip` route their table
through the shared `DataTable` component, which already wraps in `overflow-x-auto` internally
regardless of the outer page's width, and `items-master`/`pr-register`/`item-ledger` do the same at
the page level. `stock-balances`'s width went the other way — `max-w-screen-lg` (1024px) widened to
`max-w-6xl` (1152px) — since it's a form/upload page, not a table.

Items 1 and 4 (2026-08-10) were **less mechanical than "find-and-replace-shaped" suggests**:
`slate-700` maps to two different targets depending on role, so the sweep needed a per-occurrence
decision. The mapping actually used, derived from what the untouched majority of the codebase
already did (`<td>` 72:26, `<label>` 128:13, non-broken inputs 14:3 — all favouring `slate-600`):

- `slate-800` — `h1`/`h2`, §6 secondary buttons, and `font-semibold`/`font-bold` emphasis
- `slate-600` — everything else: body, table cells, form labels, input and select text
- `hover:text-slate-700` → `hover:text-slate-800` (darkens from a `slate-600` base)
- `text-xs uppercase tracking-wide` micro-labels → `slate-600`, per §2's card/stat label row

Items 5 and 6 (2026-08-10) turned out to have a real scope decision buried in "use it everywhere":
not every page with a title is a *list-page* header. Wizards were already an exception; per-record
detail/edit views (dynamic title + adjacent `StatusBadge`) were added as a second one, and three
pages outside item 5's own stated scope (Inventory + Budget Planning) were left alone rather than
silently pulled in. See "Deliberately NOT unified" above for the full reasoning.

Item 7's `StatCard` is shared — change it once and every consumer follows, so verify the dashboard
after.

None of this changes the *design*, only its consistency. The look is reviewed and deliberate: flat
by decision, contrast-audited in RAL-133, mobile-fixed in RAL-201. **This document describes the
existing design; it does not propose a new one.**
