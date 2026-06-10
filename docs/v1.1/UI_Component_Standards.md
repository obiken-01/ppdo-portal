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

## 5. Checklist Before Adding Any New UI

- [ ] Does a component in `components/ui/` already cover this? → use it
- [ ] Can an existing component be extended with a prop? → extend it
- [ ] Genuinely new pattern? → build in `components/ui/`, document the usage block at the top of the file (match `Toast.tsx` / `ConfirmDialog.tsx` style), and add a row to §2/§3 of this doc
