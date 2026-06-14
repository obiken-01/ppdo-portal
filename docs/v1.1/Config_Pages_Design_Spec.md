# Configuration Pages — Design Spec (extracted from Penpot)

_Source: Penpot file "PPDO Portal v0.5", Page 3 — boards 09 Config Dashboard, 10 Account Config, 11 Office Config, 12 Funding Sources Config. Extracted 2026-06-14 by reading shape text directly (PNG export was unreliable)._

> **Purpose:** capture the config-section screens as a written spec so implementation
> (RAL-72/73/74/71) does **not** depend on a live Penpot connection. When this spec and
> a ticket prompt disagree, the **ticket prompt wins** (e.g. the Account table shows
> Normal Balance instead of the mockup's Type column — see §2).

All boards are 1280×800, sharing the portal shell: left **Sidebar** (green, ~220px) + **Topbar**
(breadcrumb left, user chip right). Below the topbar each page has a **content header**
(title + subtitle), then a **toolbar row**, then the table, then a "Showing N of M" footer.

Common conventions across all three list pages:
- **Breadcrumb** (topbar): `Configuration  >  <Page>`; content title repeats `<Page>`.
- **Toolbar (single row):** filters on the **left** (search box, then any dropdowns),
  CSV + primary action on the **right**: `↑ Upload CSV`  `↓ Download CSV`  `+ Add <Thing>`.
- **Status filter** is a **dropdown** (`Status: All ▾`) defaulting to **All** in the mockup.
  (RAL-72 prompt asked for a segmented Active/Inactive/All toggle instead — prompt wins there.)
- **Row actions** are **text links**: `Edit · Deactivate` (active rows) / `Edit · Reactivate`.
- **Status cell**: `Active` / `Inactive` pill.
- Flat design — no rounded corners. Footer: `Showing <pageCount> of <total> <noun>`.

---

## 1. Sidebar — Configuration group (board 09)

The Sidebar shows a collapsible **Configuration** group (icon ⚙️) expanded into:
`• Dashboard` · `• Accounts` · `• Offices` · `• Funding Sources`
(Budget Planning, Inventory, Resource Links, User Management are sibling top-level items.)

> Live `Sidebar.tsx` currently links **Configuration → /config** as a single item. The
> expanded sub-nav above is the **RAL-71** target. RAL-72 ships an interim `/config →
> /config/accounts` redirect; RAL-71 replaces it with the dashboard + sub-nav.

---

## 2. Account Config — `/config/accounts` (board 10, RAL-72 ✅ built)

- **Title:** Accounts — **Subtitle:** "Manage chart of accounts used in WFP expenditure entry."
- **Toolbar (left):** `Search accounts…` · `Type: All ▾` · `Status: All ▾`
- **Toolbar (right):** `↑ Upload CSV` · `↓ Download CSV` · `+ Add Account`
- **Mockup columns:** Account Number · Account Title · **Type** · Status · Actions
  - **As built (prompt override):** Account Number · Account Title · **Normal Balance** · Status · Actions,
    with the derived **PS/MOOE/CO type rendered as a badge next to the account number** (keeps the
    mockup's type visibility without adding a 6th column).
- **Type filter** → PS/MOOE/CO, sent as `?accountType=`; API translates to the
  `5-01-/5-02-/5-03-` account_number prefix.
- **Sample rows:** `5-01-01-010 Salaries and Wages – Regular [PS]`, `5-02-01-010 Travelling
  Expenses [MOOE]`, `5-03-01-040 Motor Vehicles [CO]` … — Footer "Showing 8 of 143 accounts".
- **Add/Edit modal fields:** account_title (req), account_number (req, unique on blur),
  normal_balance (opt), description (opt). **No** Display Label / Account Type select.
- **CSV:** export order `account_title, account_number, normal_balance, description, is_active`;
  upload upserts by `account_number` → `{ new, updated, skipped }`. Upload is the seeding path (143 rows).

---

## 3. Office Config — `/config/offices` (board 11, RAL-73)

- **Title:** Offices — **Subtitle:** "Manage provincial government offices used as planning scope…"
- **Toolbar (left):** `Search offices…` · `Status: All ▾`  (no type filter)
- **Toolbar (right):** `↑ Upload CSV` · `↓ Download CSV` · `+ Add Office`
- **Columns:** Code · Office Name · Status · Actions (`Edit · Deactivate`)
- **Sample rows:** `PGO Office of the Provincial Governor`, `SPO Sangguniang Panlalawigan
  Office`, `PTO Provincial Treasurer's Office`, `PAO Provincial Assessor's Office`,
  `PBO Provincial Budget Office`, `PPDO Provincial Planning and Development Office`, `OPA
  Office of the Provincial Administrator`, `GSO General Services Office`, `PHO Provincial
  Health Office`, `PSWDO Provincial Social Welfare and Dev. Office` — Footer "Showing 10 of 16 offices".
- **Add/Edit modal fields (per RAL-70 API):** office_code (key), office_name. Status via row action.
- **CSV columns:** `office_code, office_name, is_active`; upsert key `office_code`.

---

## 4. Funding Sources Config — `/config/funding-sources` (board 12, RAL-74)

- **Title:** Funding Sources — **Subtitle:** "Manage budget funding sources used across AIP and WFP entries…"
- **Toolbar (left):** `Search funding sources…` · `Status: All ▾`
- **Toolbar (right):** `↑ Upload CSV` · `↓ Download CSV` · `+ Add Funding Source`
- **Columns:** Code · Name · Description · Status · Actions (`Edit · Deactivate`)
- **Sample rows (all 6):**
  - `GF — General Fund` — "Main operating budget of the provincial government funded by…"
  - `GAD — 5% GAD Fund` — "Gender and Development Fund – mandatory 5% allocation per RA…"
  - `LDRRMF — Local DRRM Fund` — "Minimum 5% of IRA for disaster risk reduction and management"
  - `SEF — Special Education Fund` — "Collected from property taxes earmarked for education programs"
  - `20DF — 20% Development Fund` — "20% of IRA allocated exclusively for development projects…"
  - `TF — Trust Fund` — "Funds held in trust for a specific purpose including grants…"
  - Footer "Showing 6 of 6 funding sources".
- **Add/Edit modal fields (per RAL-70 API):** code (key), name, description (opt). Status via row action.
- **CSV columns:** `code, name, description, is_active`; upsert key `code`.

---

## 5. Config Dashboard — `/config` (board 09, RAL-71)

- **Title:** Configuration — **Subtitle:** "Manage reference data used across AIP and WFP planning entries…"
- **Three stat/link cards** (counts are live):
  | Card | Count (sample) | Caption | Action |
  |---|---|---|---|
  | **Accounts** | 143 | "Chart of accounts for WFP expenditure entry." | `Manage →` → /config/accounts |
  | **Offices** | 16 | "Provincial government offices for planning scope." | `Manage →` → /config/offices |
  | **Funding Sources** | 6 | "Budget funding sources used across AIP entries." | `Manage →` → /config/funding-sources |
- No table; this is the section landing/hub. Replaces the interim `/config` redirect from RAL-72.

---

## 6. Reuse note

All three list pages (Accounts/Offices/Funding) are the **same shape** — they differ only
in columns, search placeholder, add-button label, and whether a Type filter exists. Build
Offices and Funding from the RAL-72 Account page pattern using the shared components
(`DataTable`, `Modal`, `MessageDialog`, `ConfirmDialog`, `CsvUploadButton`,
`CsvDownloadButton`, `Toast`) and the `lib/config.ts` helper module (extend with
office/funding functions following `listAccounts`/`createAccount`/etc.).
