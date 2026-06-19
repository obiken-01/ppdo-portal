# PPDO Portal v1.1 — Shared UI Component Standards
_Date: June 10, 2026 | Target: PPDO Portal v1.1 (Budget Planning)_

---

## 1. Principle

All v1.1 Budget Planning pages (Configuration, AIP, WFP, LDIP) must **reuse shared UI components** instead of building one-off implementations per page. If a needed pattern doesn't exist yet, build it once in `frontend/src/components/ui/` and consume it everywhere.

Rules:

1. **Check `components/ui/` first** before writing any modal, toast, table, or form control.
2. **No page-local copies** of shared patterns — if a component needs a new capability, extend the shared component (with a prop), don't fork it.
3. All shared components follow the **flat design system** (no rounded corners) and PPDO design tokens (see `PPDO_PROJECT_CONTEXT.md` §8).

---

## 2. Existing Shared Components (v1.0 — reuse as-is)

| Component | File | Use For |
|---|---|---|
| `Toast` / `ToastProvider` / `useToast()` | `frontend/src/components/ui/Toast.tsx` | Success confirmations, API errors on completed actions. Variants: success / error / info / warning. Auto-dismiss 5 s. ❌ Not for form validation or errors inside open modals — use inline errors there |
| `ConfirmDialog` | `frontend/src/components/ui/ConfirmDialog.tsx` | Yes/No confirmations before destructive or locking actions (e.g. finalize record, deactivate config entry). Variants: primary / warning / danger |

---

## 3. New Shared Components Needed for v1.1

> **Build status:** all five below were built in RAL-72 (Account Config page) and are now available in `frontend/src/components/ui/`. Reuse as-is for Offices / Funding Sources (RAL-73/74) and the AIP/WFP pages — do not fork.

Build these in `frontend/src/components/ui/` following the same conventions as `ConfirmDialog` (backdrop click + Escape closes, focus trap, design tokens).

### 3.1 `Modal` — generic content modal with custom footer buttons

The base building block for all popups. Props:

- `title` — header text
- `children` — arbitrary content (form, table, summary, etc.)
- `footer` — custom buttons (caller provides; e.g. Save / Cancel, or Add Row / Close)
- `size` — `sm` / `md` / `lg` / `xl` (WFP expenditure popup needs `xl`)
- `onClose` — backdrop / Escape / × handler

**v1.1 consumers:**
- Config Add/Edit forms (Accounts, Offices, Funding Sources)
- WFP expenditure entry popup (PS / MOOE / CO sections)
- AIP manual entry forms (Office / Program / Project / Activity)
- CSV upload preview/confirm step

### 3.2 `MessageDialog` — simple message popup

Informational popup with a single OK/Close button (no confirm/cancel choice). For notices like "Import completed — 2,309 activities saved" or blocking validation summaries. Could be implemented as a thin wrapper over `Modal`.

### 3.3 `DataTable` — searchable/sortable table

All three config pages share the same table behavior: search box, sortable columns, row actions (edit icon), pagination if needed. Build once, configure with a column definition array.

### 3.4 `CsvUploadButton` + `CsvDownloadButton`

Shared CSV round-trip controls used on every config page: upload triggers parse + preview modal (new vs. updated vs. skipped counts) before confirming; download exports current table in the seed CSV column order.

> **As built (RAL-72):** the buttons are intentionally thin. `CsvDownloadButton` is self-contained (fetch via authed Axios → Blob → download). `CsvUploadButton` only surfaces the chosen `File` via `onSelect`; the consuming page composes the confirm step from `Modal` and the post-import summary from `MessageDialog` (the RAL-70 upsert endpoint commits on POST and returns the new/updated/skipped counts — there is no dry-run, so the "preview" is a pre-commit confirm + post-commit summary).

### 3.5 `DataTable` filtering note (RAL-72)

`DataTable` owns render + client-side **sort** + pagination + the loading/error/empty states. **Filtering is the consumer's responsibility** — config pages keep a dedicated filter bar (search + type + status) and pass already-filtered `rows` in. This keeps server-side filters (e.g. the `accountType` → account_number prefix translation, handled by the API) separate from client-only sort.

---

## 4. Composition Guideline

```
Modal (base)
├── ConfirmDialog   = Modal + fixed message + Confirm/Cancel footer (existing — keep as-is)
├── MessageDialog   = Modal + message + single OK footer
├── Config form     = Modal + form children + Save/Cancel footer
├── WFP expenditure = Modal (xl) + 3-section tables + always-visible Save footer
└── CSV preview     = Modal + diff table + Confirm Import/Cancel footer
```

Toast remains separate (non-blocking, top-right stack).

---

## 5. Breadcrumbs — always use the Topbar `SECTIONS` array

Breadcrumbs are rendered in **`frontend/src/components/layout/Topbar.tsx`** via `SectionBreadcrumb`, not as inline markup inside pages.

**Do not add a `<p>` / `<nav>` breadcrumb block inside a page component.** The Topbar reads `pathname` and automatically selects the right crumb from `SECTIONS`.

### How to register a new page

Open `Topbar.tsx` and add an entry to the relevant `Section.crumbs` array:

```ts
// 2-level:  Budget Planning › AIP
{ prefix: "/budget-planning/aip", label: "AIP" }

// 3-level:  Budget Planning › AIP › New AIP
{ prefix: "/budget-planning/aip/new", label: "New AIP",
  parent: { label: "AIP", href: "/budget-planning/aip" } }
```

Matching is longest-prefix-first, so **put deeper paths before shallower ones** within the same crumbs array.

### Adding a new top-level section

Add a new `Section` object to the `SECTIONS` array:

```ts
{
  root: "/my-section",
  rootLabel: "My Section",
  crumbs: [
    { prefix: "/my-section/sub-page", label: "Sub Page" },
  ],
}
```

### Registered sections (as of v1.1)

| Section | Root label | Registered crumbs |
|---|---|---|
| `/inventory` | Inventory | Create PR, Receive Delivery, Items Master, PR Report, Distribution, Stock Overview, PR List |
| `/config` | Configuration | Accounts, Offices, Funding Sources |
| `/budget-planning` | Budget Planning | AIP; AIP › New AIP; AIP › Import Preview; LDIP; WFP |

---

## 6. Checklist Before Adding Any New UI

- [ ] Does a component in `components/ui/` already cover this? → use it
- [ ] Can an existing component be extended with a prop? → extend it
- [ ] Genuinely new pattern? → build in `components/ui/`, document the usage block at the top of the file (match `Toast.tsx` / `ConfirmDialog.tsx` style), and add a row to §2/§3 of this doc
- [ ] New page with a breadcrumb? → register it in `Topbar.tsx` SECTIONS, do not add inline breadcrumb markup
