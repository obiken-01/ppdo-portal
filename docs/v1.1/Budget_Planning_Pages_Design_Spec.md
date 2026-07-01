# Budget Planning Pages — Design Spec (extracted from Penpot)

_Source: Penpot file "PPDO Portal v0.5", Page 4 — boards 13–21. Extracted 2026-06-14 by reading shape text directly (PNG export was unreliable). Companion to `Config_Pages_Design_Spec.md` (config boards 09–12)._

> **Purpose:** capture the LDIP / AIP / WFP / Planning-Dashboard screens as a written spec so
> implementation (RAL-75, RAL-64/76, RAL-68, RAL-80) does **not** depend on a live Penpot
> connection. When this spec and a ticket prompt disagree, the **ticket prompt wins**. Sample
> data in the mockups is illustrative — real counts/values come from the data.

All boards are 1280×800 with the portal shell (Sidebar nested as a separate "Sidebar" board +
Topbar breadcrumb). Money figures in the mockups are shown in **thousands** unless noted.
Flat design — no rounded corners. Reuse the shared components from
`UI_Component_Standards.md` (DataTable, Modal, MessageDialog, ConfirmDialog, CSV buttons, Toast)
and the `lib/config.ts` envelope-unwrap pattern.

Status lifecycle is **Draft → Final → Archived** (finalize + admin unlock; **not** Submitted/Approved).

---

## 1. LDIP List — `/budget-planning/ldip` (board 13, RAL-75)

- Breadcrumb `Planning / LDIP`; title "Local Development Investment Program";
  subtitle "Multi-year provincial planning documents"; top-right **`+ New`**.
- **Toolbar:** `Search title or ref code…` · `All Status ▾` · `All Types ▾`
- **Columns:** REF CODE · TITLE · PERIOD · ENTRY MODE · STATUS · CREATED · ACTIONS
- **Sample rows:**
  - `LDIP-2027-001 · Prov. Dev. Investment Plan 2027–2029 · 2027–2029 · New · Final · Jan 15, 2026 · Edit · View`
  - `LDIP-2027-002 · Supplemental PDIP FY2027 · 2027–2029 · Amendment · Draft · Mar 3, 2026 · Edit · View`
  - `LDIP-2026-001 · Prov. Dev. Investment Plan 2026–2028 · 2026–2028 · New · Archived · Dec 10, 2025 · View`
  - Footer "Showing 3 of 3 records".
- Entry Mode ∈ {New, Amendment, Supplemental}. RAL-75 ships this as a **skeleton/filler** list.

## 2. LDIP Entry — `/budget-planning/ldip/new` (board 14, RAL-75)

- Breadcrumb `Planning / LDIP / New LDIP`; title "Create New LDIP"; subtitle "Fill in the details below. You can save as Draft at any time."
- **Fields:** Title (text, ph "e.g. Provincial Development Investment Plan 2027–2029") · Entry Mode (`New ▾`) ·
  Fiscal Year Start (2027) / Fiscal Year End (2029) · Source LDIP ("required for Amendment / Supplemental";
  shows "N/A — Entry Mode is New" when mode = New).
- Footer note + buttons: **Save Draft · ~~Submit for Approval~~ · Cancel**.
  > ⚠️ The mockup's "Submit for Approval" / "route for approval" copy is **stale** — the confirmed
  > workflow is Draft → Final → Archived (no approval step). Use **Finalize** semantics, not Submit/Approve.

---

## 3. AIP List — `/budget-planning/aip` (board 15, RAL-64/76)

- Breadcrumb `Planning / AIP`; title "Annual Investment Program"; subtitle "Yearly investment allocations per sector and office"; top-right **`+ New AIP`**.
- **Toolbar:** `Search AIP…` · `FY 2027 ▾` · `All Status ▾` · `All Sources ▾`
- **Columns:** FISCAL YR · SOURCE · STATUS · OFFICES · LDIP REF · UPLOADED BY · UPLOADED AT · ACTIONS
  - SOURCE here = **entry mode** (Upload / Manual). OFFICES = office count. LDIP REF is optional (AIP is independent of LDIP).
- **Sample rows:**
  - `FY 2027 · Upload · Final · 16 offices · LDIP-2027-001 · R. Alcaide · Jan 20, 2026 · View · WFP`
  - `FY 2027 · Manual · Draft · 4 · LDIP-2027-001 · R. Alcaide · Mar 5, 2026 · Edit · View · WFP`
  - `FY 2026 · Upload · Archived · 16 · LDIP-2026-001 · R. Alcaide · Jan 18, 2025 · View`
  - Footer "Showing 3 of 3 records".
  > Note: **Manual entry is deferred** in v1.1 (disabled placeholder) — the Manual rows/tab are mockup-only.

## 4. AIP Upload — `/budget-planning/aip/new` (board 16, RAL-64/76)

- Breadcrumb `Planning / AIP / New AIP`; title "Create New AIP"; subtitle "Upload an .xlsm file or enter data manually".
- **Tabs:** `Upload File` | `Manual Entry` (**Manual Entry = disabled placeholder** in v1.1).
- **Fields:** Fiscal Year (`2027 ▾`) · Link to LDIP (optional) (`Select LDIP… ▾`).
- **Dropzone:** 📁 "Drag & drop your .xlsm file here" / "or" / **Browse File**; "Accepts .xlsm files up to 20 MB".
- **Buttons:** Upload & Preview · Cancel. (Upload & Preview → Import Summary, §5.)

## 5. AIP Import Summary — `/budget-planning/aip/import-preview` (board 17, RAL-64/76)

- Breadcrumb `Planning / AIP / Import Preview`; title "Import Preview — AIP FY2027"; subtitle "Review the data before confirming. This cannot be undone."
- **Count tiles:** Offices · Programs · Projects · Activities (mockup sample: 16 / 48 / 112 / 389 — **real file ≈ 2,309 activities**; counts come from the parse).
- **Preview by Sector** (rows show sector prefix + programs + activities):
  - General Services `1xxx` — 8 programs · 86 activities
  - Social Services `3xxx` — 20 programs · 142 activities
  - Economic Services `8xxx` — 16 programs · 124 activities
  - Other Services `9xxx` — 4 programs · 37 activities
  - Sector display order: General → Social → Economic → Others.
- **Skip notice:** "⚠ 1 activity skipped — 'Janaury' name could not be resolved to a valid…"
  (matches the known `3000-000-1-01-017` data-entry issue in `AIP_WFP_Import_Findings.md` §5).
- **Buttons:** Confirm Import · Cancel.

---

## 6. WFP Activity Grid — `/budget-planning/wfp` (board 18, RAL-68)

- Breadcrumb `Planning / WFP`; title "Work and Financial Plan"; subtitle "PPDO · AIP FY2027".
- **Selectors:** AIP (`AIP FY2027 ▾`) · Office (`PPDO ▾`); top-right **`Finalize`**.
- **Level toggle:** "Show levels: 1 2 3 4" (collapse/expand the hierarchy depth).
- **Columns:** AIP REF CODE · PROGRAM / PROJECT / ACTIVITY · PS · MOOE · CO · TOTAL · Q1 · Q2 · Q3 · Q4 · _(Edit)_
- **Layout:** rows grouped under **sector headers** (e.g. `SOCIAL SERVICES`, `ECONOMIC SERVICES`).
  AIP ref codes are 2-line; rows nest Program → Project → Activity (description indents per level).
  **Activity** rows carry the PS/MOOE/CO/Total/Q1–Q4 numbers + an **Edit** link → Expenditure Popup (§7).
  Program/Project rows are headers (no amounts).
  - Sample: `3000-…-013-001-001-001 Purchase of Medicines & Supplies · PS 480 · MOOE 1,200 · CO 0 · TOTAL 1,680 · Q1–Q4 420 each · Edit`
- **Footer:** "You have unsaved changes." + always-visible **Save** button (not per-field auto-save).

## 7. WFP Expenditure Popup — modal over the grid (board 19, RAL-68)

- **Modal title:** "Expenditure Lines — <Activity name>" (e.g. "…Purchase of Medicines & Supplies"); `×` close.
- **Section tabs:** **PS · MOOE · CO** (one expandable table per type; each starts with 1 blank row).
- **Table columns:** ACCT CODE · OBJECT OF EXPENDITURE · TOTAL APPROP · RESERVE · NET TOTAL · Q1 · Q2 · Q3 · Q4 · LINE TOTAL · SOURCE
  - ACCT CODE auto-populates from the Object of Expenditure selection (searchable; shows `Account Title (Account Number)`).
  - RESERVE = per-line Yes/No (10% reserve checkbox); when Yes, NET TOTAL = TOTAL APPROP − 10%.
  - LINE TOTAL = Q1+Q2+Q3+Q4 (auto). SOURCE = funding source (default from AIP; editable).
  - Sample: `5-02-03-050 · Drugs & Medicines · 800,000 · Reserve Yes · Net 720,000 · 180,000×4 · 720,000 · GAD`
- **`+ Add Line`** per section.
- **Footer:** Save Changes · Cancel · "Draft auto-saved to localStorage" (offline buffer).

---

## 8. Planning Dashboard — PPDO view — `/budget-planning` (board 20, RAL-80)

- Breadcrumb `Planning / Dashboard`; title "Budget Planning Dashboard"; subtitle "FY overview · PPDO view — all offices".
- **Selectors:** FY (`FY 2027 ▾`) · Office (`All Offices ▾`).
- **Nav buttons:** LDIP · AIP · WFP (jump to each module).
- **Stat cards (3):**
  - `LDIP RECORDS` = 3 — "1 Final · 1 Draft · 1 Archived"
  - `AIP RECORDS` = 2 — "FY 2027 Final · FY 2026 Archived"
  - `WFPS — FY 2027` = "9 of 16 Final"
- **"WFP Status by Office — FY 2027"** table: OFFICE · STATUS · ACTION (View/Open).
  - Sample rows: Provincial Engineer's Office (Not started · View), Provincial Treasurer's Office
    (Not started · View), Office of the Governor (Draft · Open), Provincial Health Office (Draft ·
    Open), Provincial Planning and Development Office (Final · Open).
  - Footer "Showing 5 of 16 offices · sorted: Not started → Draft → Final · **no amounts**".
  - ⚠️ **No peso amounts** in this table — status only (confirmed RAL-80 decision).
- **"Recent Activity"** panel (right): "All offices (PPDO view)" — chronological (Today / Jun 9 / Jun 8):
  "R. Alcaide finalized PPDO WFP", "PGO updated 12 expenditure lines", "AIP FY 2027 imported (2,309 activities)", etc.

## 9. Planning Dashboard — Office-user view (board 21, RAL-80)

> Terminology: "Visitor" was retired in v1.1 — these are **office users** (non-PPDO, `users.office_id` set). See `User_Roles_Permissions.md`.

- Same header; subtitle "FY overview · Office view — <Office name>".
- **Selectors:** FY (`FY 2027 ▾`) · Office **locked to own office** (e.g. `PEO`, not a free dropdown).
- **Nav buttons:** LDIP · AIP · WFP.
- **No stat cards, no WFP-status-by-office table.**
- **"Recent Activity"** only — "<Office> only"; own-office entries (e.g. "J. Cruz saved PEO WFP draft",
  "J. Cruz updated 8 expenditure lines (PEO WFP)", "PEO WFP started", "AIP FY 2027 available for WFP entry").
- Access: PPDO users manage all offices (§8); non-PPDO office users get this restricted own-office view.

---

## 10. Build order & reuse

These screens come after the config section (RAL-72/73/74/71). Per the v1.1 sequence:
AIP upload (RAL-64/76) → WFP (RAL-68) → LDIP skeleton (RAL-75) → dashboard wiring (RAL-80 ships
early with empty data as the office-user redirect target). Reuse the config-page building blocks:
`DataTable` for the LDIP/AIP lists and the WFP grid's status table; `Modal` (size `xl`) for the WFP
expenditure popup; `MessageDialog` for the AIP import summary; the CSV/dropzone patterns; and the
`{ data, error, message }` envelope helpers in `lib/config.ts`.
