# PPDO Portal — Design System

The reference for building or updating any page. Read this before writing UI; it is the one place
that answers "what should this look like?" so you don't have to reverse-engineer it from whichever
page you happened to open.

Companion docs: `PERFORMANCE_GUIDELINES.md` §6-7 (loading states, images),
`NAMING_CONVENTIONS.md` (file and component naming), `CLAUDE.md` (frontend architecture rules).

> **Status:** the token layer is mature and reviewed. The *application* of it is not yet uniform —
> Inventory was built first and diverged from Budget Planning and Config. §7 records every known
> divergence with a canonical target. Where a difference is legitimate, it says so and why.

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

> ### ⚠️ `slate-700` is not a PPDO token
>
> There is **no `slate-700` in `tailwind.config.ts`**, but it is used **215 times across 46 files**.
> Those all silently resolve to stock Tailwind `#334155` — which is *blue-tinted*, unlike every
> shade in the PPDO ramp above. It is not a contrast failure (it's very dark), so this is a
> **palette-coherence** bug, not an accessibility one: headings drift blue while the surrounding
> text stays neutral grey.
>
> **For new code: use `slate-800` for headings and `slate-600` for body text. Never `slate-700`.**
> This is the same class of bug RAL-133 fixed for `slate-400`/`slate-500` — an undefined shade
> falling back to an unreviewed stock value. Migration is tracked in §7.

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
| Page title (`h1`) | `text-lg font-bold text-slate-800` |
| Page description | `text-sm text-slate-600` |
| Section heading (`h2`) | `text-sm font-semibold text-slate-800` |
| Card/stat label | `text-xs font-semibold text-slate-600 uppercase tracking-wide` |
| Body / table cell | `text-sm text-slate-600` |
| Modal title | `text-base font-semibold text-slate-800` |
| Numeric value | add `tabular-nums` — always, so digits align in columns |

`text-lg` for `h1` (not `text-xl`) matches `ConfigPageHeader`, which is the most-used header
component and the de facto standard. Page titles sit under a persistent Topbar that already
establishes context, so they don't need to shout.

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

Four different shells exist today (§7). This one is the target.

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

**Adoption today:** `Toast` 32 files, `DataTable` 10, `ConfigPageHeader` 7, `TableSkeleton` 6,
`Lookup` 3, `LoadingState` 1. `LoadingState`'s single use is consistent with its narrow purpose.

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

---

## 7. Known divergences and migration targets

Inventory shipped first (v1.0/v1.1) and set patterns that later features didn't follow; Config was
refactored most recently (RAL-201) and is closest to the target. This table is the backlog for
making them uniform.

| # | Divergence | Current state | Target | Priority |
|---|---|---|---|---|
| 1 | **`slate-700`** | 215 uses / 46 files; not a PPDO token, resolves to blue-tinted stock `#334155` | `slate-800` headings, `slate-600` body | **High** — most widespread, mechanical |
| 2 | **Page shell** | 4 variants: Inventory `gap-6 p-3 sm:p-6` no max-width · Config `max-w-6xl px-6 py-6 space-y-4` · Budget Planning `p-6 max-w-screen-xl` · `admin/users` bare `space-y-4` | §4 shell | **High** |
| 3 | **`h1` size** | `text-xl` (Inventory dashboard, BP sub-pages) vs `text-lg` (`ConfigPageHeader`, Config landing, BP dashboard) | `text-lg` | Medium |
| 4 | **`h2` styling** | 5 treatments: `text-sm`+`slate-700`, `text-base`+`slate-800`, `text-sm`+`slate-800`, `text-lg font-bold` (modals), `text-xs uppercase` | `text-sm font-semibold text-slate-800`; modals `text-base font-semibold` | Medium |
| 5 | **`ConfigPageHeader` adoption** | Config only (7 files). Inventory and Budget Planning hand-roll the same markup — the exact duplication RAL-201 consolidated | Use it everywhere | Medium |
| 6 | **Inventory sub-pages have no `h1`** | `items-master`, `item-ledger`, `pr-register`, `pr-report`, `distribution` render no page title at all | Add `ConfigPageHeader` | Medium |
| 7 | **Rounded corners in portal** | `items-master` 8 (`rounded-lg`×7, `rounded-xl`×1) · `StatCard.tsx` `rounded-xl` · `ResourceLinksWidget` · `admin/users` · `profile` · `DashboardCalendar` `rounded-sm` | Remove (keep `rounded-full`) | Medium — `StatCard` first, it's shared |
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

### Sequencing note

Items 1, 3, and 4 are find-and-replace-shaped and safe to batch. Item 2 changes layout and should
be done one page family at a time with a visual check on each. Item 7's `StatCard` is shared —
change it once and every consumer follows, so verify the dashboard after.

None of this changes the *design*, only its consistency. The look is reviewed and deliberate: flat
by decision, contrast-audited in RAL-133, mobile-fixed in RAL-201. **This document describes the
existing design; it does not propose a new one.**
