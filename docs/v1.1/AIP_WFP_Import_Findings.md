# AIP Import & WFP Findings
_Reviewed: June 10, 2026 | Target: PPDO Portal v1.1 | Files: AIP_2027 as of June 2, 2026.xlsm · PPDO_WFP 2026_FinalVersion (PPDO_WFP_2026_3FundS) · annex_b_charts_of_account.pdf_

---

## 1. AIP File Structure

### Sheets (4 sectors)
| Sheet | Non-Empty Rows | Activity Entries |
|---|---|---|
| GENERAL_FY2027 | 807 | ~549 |
| SOCIAL_FY2027 | 721 | ~554 |
| ECONOMIC_FY2027 | 1158 | ~1,006 |
| OTHERS_FY2027 | 286 | ~200 |
| **Total** | | **~2,309 activities** |

All 4 sheets share the same column layout (rows 1–9 are header/title; data starts row 14).

### AIP Reference Code — 4-Level Hierarchy
Level is determined by segment count in the AIP Ref Code. **All levels are imported** — not just Activity.

| Segments | Level | Excel Column | Example | DB Field |
|---|---|---|---|---|
| 5 | Office | B | `1000-000-1-01-001` | `office_code` + `office_name` |
| 6 | Program | C | `1000-000-1-01-001-001` | `program_code` + `program_name` |
| 7 | Project / Sub-Program | D | `1000-000-1-01-001-001-001` | `project_code` + `project_name` |
| 8 | Activity | E | `1000-000-1-01-001-001-001-001` | `activity_code` + `activity_name` |

The sector prefix in the ref code: `1xxx` = General, `3xxx` = Social, `8xxx` = Economic, `9xxx` = Others.

### Column Map (per Ralph's field notes, rows 10–13)
| Col | Field | Source | Level |
|---|---|---|---|
| A | AIP Reference Code | From LDIP / upload; auto-computed | All |
| B | Office Description | From LDIP / upload / selected office | Office |
| C | Program Description | From LDIP / upload | Program |
| D | Sub-Program / Project Description | User entry | Project |
| E | Activity Description | User entry | Activity |
| F | eSRE Code | Dropdown (hardcoded): SS, ES, ID, EN | Activity |
| G | Implementing Office | From selected office; editable | Activity |
| H | Start Date | User entry | Activity |
| I | Completion Date | User entry | Activity |
| J | Expected Outputs | User entry | Activity |
| K | Funding Source | Dropdown; user entry | Activity |
| L | PS (Personal Services) | Numeric; user entry | Activity |
| M | MOOE | Numeric; user entry | Activity |
| N | CO (Capital Outlay) | Numeric; user entry | Activity |
| O | Total | Auto-computed (L + M + N) | Activity |
| P | CC Adaptation Amount | Numeric; user entry | Activity |
| Q | CC Mitigation Amount | Numeric; user entry | Activity |
| R | CC Typology Code | Numeric; user entry | Activity |

### Database Model — `aip_entries` Table
Store all 4 hierarchy levels. Programs and Projects are parent records of Activities.

```
aip_import (one record per upload)
  id, fiscal_year, uploaded_by, uploaded_at, sector

aip_office
  id, ref_code, name, aip_import_id

aip_program
  id, ref_code, name, aip_office_id

aip_project
  id, ref_code, name, aip_program_id

aip_activity
  id, ref_code, name, aip_project_id
  esre_code, implementing_office
  start_date, end_date, expected_outputs, funding_source
  ps, mooe, co, total
  cc_adaptation, cc_mitigation, cc_typology_code
```

### Entry Modes
AIP records can be created in two ways:

1. **File Upload** — upload the existing xlsm file; system parses all 4 sector sheets and imports all hierarchy levels automatically.
2. **Manual Web UI Entry** — user creates the AIP record directly in the portal, filling in each level (Office → Program → Project → Activity) through forms.

Both modes produce the same data structure in the database.

### Post-Upload Summary Page
After a successful file upload, a summary page is displayed showing what was imported — structured similarly to the Excel file layout:
- Grouped by sector (General → Social → Economic → Others)
- Hierarchical display: Office → Program → Project → Activity
- Columns: AIP Ref Code, Description, eSRE Code, Implementing Office, Funding Source, PS, MOOE, CO, Total
- Shows counts: total offices, programs, projects, activities imported
- Option to proceed (confirm import) or cancel

### Other Import Notes
- **AIP is independent from LDIP** — no foreign key to LDIP records.
- The Office-level row (5 segments) has PS/MOOE/CO totals but no activity fields — import name and code only; totals are derived from children.
- Some multi-line descriptions span the next row with `None` in col A — concatenate during parsing.
- On re-upload: match by `ref_code` + `fiscal_year` to update existing records, or create a new import version.

---

## 2. WFP File Structure (Sheet: PPDO_WFP_2026_3FundS)

### Overview
A Work and Financial Plan for **one office** per sheet. Each AIP activity expands into **multiple expenditure lines** — one line per account code / object of expenditure. Expenditure lines are grouped under three types: **PS**, **MOOE**, **CO**.

### WFP Column Map (per Ralph's field notes, row 10)
| Col | Field | Source |
|---|---|---|
| A | AIP Ref Code | From AIP |
| B | Programs, Projects and Activities | From AIP |
| C | Resources Needed | User entry; optional — stored on expenditure line, not activity header |
| D | Responsible Unit / Division | User entry; optional |
| E | Success Indicator | User entry; optional |
| F | Means of Verification | User entry; optional |
| G | Account Code | Auto-populated from Account Config |
| H | Object of Expenditure | Searchable dropdown (Account Title); user entry |
| I | Total Appropriation (gross) | Numeric; user entry |
| J | 10% Reserve | Checkbox per line; auto-compute (10% of I) |
| K | Total Appropriation (net) | Auto-computed (I − J) |
| L | 1st Quarter | Numeric; user entry |
| M | 2nd Quarter | Numeric; user entry |
| N | 3rd Quarter | Numeric; user entry |
| O | 4th Quarter | Numeric; user entry |
| P | Total | Auto-computed (L+M+N+O) |
| Q | Source of Fund | Dropdown; default from AIP; editable |

### Expenditure Entry — Popup Design
When a user opens an activity to add/edit expenditures, a popup appears with **3 sections** (PS / MOOE / CO). Each section has an expandable table:

| # | Column | Notes |
|---|---|---|
| 1 | Account Code | Auto-populated when Object of Expenditure is selected |
| 2 | Object of Expenditure | Searchable textbox — displays `Account Title (Account Number)` |
| 3 | Total Appropriation | Numeric; user entry |
| 4 | 10% Reserve | Checkbox; if checked, auto-computes 10% and deducts |
| 5 | Net Total Appropriation | Auto-computed (col 3 − col 4) |
| 6 | Q1 | Numeric; user entry |
| 7 | Q2 | Numeric; user entry |
| 8 | Q3 | Numeric; user entry |
| 9 | Q4 | Numeric; user entry |
| 10 | Total | Auto-computed (Q1+Q2+Q3+Q4) |
| 11 | Source of Fund | Dropdown; default from AIP; editable |

- Each section starts with **1 blank row**. User can add more rows per section.
- If no expenditure under a type, leave the blank row (do not require it to be filled).
- 10% Reserve is per-line (not per-activity).

### Sector Display Order
General → Social → Economic → Others (matches the Excel file order).

### Key Observations
- PPDO alone has ~80 activities → 200–500 rows when expenditure lines are expanded. A save button (always visible) is required — not auto-save on every field change.
- Quarterly amounts are not always equal (front-loaded, back-loaded, or irregular).
- Offline resilience: use localStorage to buffer unsaved changes; sync to server on Save.
- History tracking: log all changes to LDIP, AIP, and WFP records (who changed what and when).

---

## 3. Chart of Accounts (annex_b_charts_of_account.pdf)

**556 total accounts** extracted. Relevant expense accounts (prefix `5-xx`):

| Type | Prefix | Count |
|---|---|---|
| Personal Services (PS) | 5-01-xx | 27 |
| MOOE | 5-02-xx | 72 |
| Capital Outlay (CO) | 5-03-xx | 6 |
| Other (Losses, etc.) | 5-04/05-xx | 38 |
| **Total expense** | 5-xx | **143** |

### Account Config Page
- **Account Title** → Object of Expenditure (col H in WFP)
- **Account Number** → Account Code (col G in WFP, auto-populated)
- Searchable textbox displays: `Account Title (Account Number)` e.g. `Salaries and Wages – Regular (5-01-01-010)`
- Config page to be built; seed data from this PDF
- CSV version (`chart_of_accounts.csv`) already generated — ready for upload to the config page
  - Columns: `account_number`, `account_title`, `display`
  - 556 rows total

---

## 4. Confirmed Design Decisions

| # | Decision |
|---|---|
| 1 | AIP import saves all 4 hierarchy levels: Office, Program, Project, Activity |
| 2 | AIP has no LDIP foreign key — independent records |
| 3 | WFP save: always-visible Save button (not per-field auto-save) + localStorage offline buffer |
| 4 | Expenditure entry via popup, 3 sections (PS/MOOE/CO), each with an editable table |
| 5 | 10% Reserve is per expenditure line, controlled by a checkbox |
| 6 | Sector display order: General → Social → Economic → Others |
| 7 | One-user-at-a-time per office for now |
| 8 | History tracking on LDIP, AIP, and WFP (who changed what, when) |
| 9 | Account config is a separate config page, seeded from chart_of_accounts.csv |
| 10 | Account search shows `Account Title (Account Number)`; Account Code auto-populates from selection |

---

## 5. Configuration Section

### Architecture
A unified **Configuration** section in the portal nav with one page per config type. Each config type has its own database table (different field structures per type) but all share the same reusable page component.

```
Portal Nav
└── Configuration
    ├── Accounts
    ├── Offices
    ├── Funding Sources  ← GF, SEF, LDRRMF, GAD, etc.
    └── [more as needed]
```

> **eSRE Codes** (SS, ES, ID, EN) are removed from the config nav for now. Labels are not yet confirmed — will be added as a config page once the full names are known.

### Per-Config Page Features
Every config page has the same 4 capabilities:

1. **Searchable / sortable data table** — browse all entries
2. **Add / Edit row** — clicking "Add" or the edit icon opens a **popup/modal form** (not inline editing). The modal contains a simple form with the fields for that config type. On submit, the table refreshes with the new/updated entry.
3. **CSV Upload** — bulk import; user uploads a CSV, system previews the column mapping before confirming
4. **CSV Download** — export current config as CSV for bulk editing; user edits offline and re-uploads

### Upload Behavior
- On CSV upload: **upsert by key field** (e.g. `account_number` for Accounts). New rows are inserted; existing rows are updated.
- Preview step before confirming: show row count, highlight new vs. updated vs. skipped rows.
- On conflict with a referenced record (e.g. account_number already used in a WFP): update allowed, but **no hard delete** — use soft delete (deactivate) so existing references remain intact.
- Deactivated entries are hidden from dropdowns but preserved in historical records.

### Seed Data Available
| Config | Seed File | Rows | Notes |
|---|---|---|---|
| Accounts | `chart_of_accounts.csv` | 143 expenditure accounts (5-xx prefix) | Full 571-row CSV also available; 143 are the relevant PS/MOOE/CO expense accounts |
| Offices | `offices.csv` | 16 | Extracted from AIP 2027; sub-offices excluded; columns: `office_code`, `office_name`, `is_active` |
| Funding Sources | `funding_sources.csv` | 6 | GF, GAD, LDRRMF, SEF, 20DF, TF; columns: `code`, `name`, `description`, `is_active` |

> Note: ref code `3000-000-1-01-017` in the AIP file has a data entry issue (value "Janaury") — office excluded from seed. Verify and add manually.

---

## 6. Next Steps

- [ ] Build Configuration section (reusable config page component with CSV upload/download + UI add/edit)
  - Add button → popup/modal form; Edit button → same modal pre-filled
- [ ] Build Account Config page — seed with `chart_of_accounts.csv`
- [ ] Build Office Config page — seed with `offices.csv`
- [ ] Build Funding Source Config page — seed with `funding_sources.csv`
- [ ] Build AIP upload/import page (parse xlsm, 4 sheets, save all 4 levels)
- [ ] Build WFP entry page (office + AIP selector → hierarchical activity grid → expenditure popup)
- [ ] Add history tracking model to LDIP, AIP, WFP
- [ ] Move eSRE Codes from hardcoded to config page (once labels/full names are confirmed)
