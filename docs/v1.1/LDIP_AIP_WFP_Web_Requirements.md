# LDIP / AIP / WFP Web Application — Requirements & Field Analysis
*Prepared for Claude Code | Province of Occidental Mindoro — PPDO | June 9, 2026*

---

## 1. Overview

> **Context:** This feature is to be integrated into the **existing PPDO Portal Website** already developed with Claude for the Province of Occidental Mindoro. It is not a standalone application — new pages, routes, and database tables should follow the conventions, stack, and UI patterns already established in that codebase.
>
> **Office-scoped entry:** For both LDIP and AIP, the user first selects an office before creating or editing records. All PPA entries are scoped to that selected office — users do not enter data for the full province-wide document in one sitting. Further details on this flow to be provided.

This document describes the data structure, field inventory, and web form requirements for three interrelated planning documents used by Philippine LGUs:

| Document | Full Name | Scope |
|---|---|---|
| **LDIP** | Local/Provincial Development Investment Program | Multi-year (3–6 yrs), province-wide, all offices |
| **AIP** | Annual Investment Program | Single fiscal year, annual slice of LDIP |
| **WFP** | Work and Financial Plan | Per-department, detailed quarterly breakdown |

**Relationship:** LDIP → AIP (annual slice of LDIP) → WFP (department-level execution plan)

**Legal Basis:** RA 7160 (Local Government Code), DBM Local Budget Circular No. 152 (2023), DILG-NEDA-DBM-DOF JMC No. 1 (2016)

---

## 2. AIP Reference Code Structure

All three documents use AIP Reference Codes as primary identifiers. Understanding this is critical for the data model.

**Format:** `SSSS-000-L-CC-OOO[-PPP[-AAAA[-XXXX]]]`

| Segment | Position | Meaning | Values |
|---|---|---|---|
| `SSSS` | 1st | Sector | `1000`=General, `3000`=Social, `8000`=Economic, `9000`=Others |
| `000` | 2nd | Reserved | Always `000` |
| `L` | 3rd | LGU Type | `1`=Province, `2`=City, `3`=Municipality |
| `CC` | 4th | Office Category | `01`=Mandatory, `02`=Optional, `03`=Others |
| `OOO` | 5th | Office Code | `001`–`022` (see Annex D) |
| `PPP` | 6th | Program Sequence | `001`, `002`… |
| `AAAA` | 7th | Sub-program/Activity Group | `001`, `002`… |
| `XXXX` | 8th | Activity/Line Item | `001`, `002`… |

**Hierarchy by number of segments:**

| Segments | Level | Description | Used In |
|---|---|---|---|
| 5 | Office Header | `1000-000-1-01-005` = Provincial Treasurer's Office | LDIP, AIP |
| 6 | Program | `1000-000-1-01-005-001` = Program name | LDIP, AIP, WFP |
| 7 | Sub-program / Activity Group | `1000-000-1-01-005-001-001` = Activity group | AIP, WFP |
| 8 | Activity / Line Item | `1000-000-1-01-005-001-001-001` = Specific activity | AIP, WFP |

> **Note:** LDIP only uses 5-segment (office) and 6-segment (program) codes. AIP and WFP go deeper to 7–8 segments.

---

## 3. Reference Data (Dropdown Sources)

### 3.1 Sector Codes
| Code | Sector |
|---|---|
| `1000` | General Public Services |
| `3000` | Social Services |
| `8000` | Economic Services |
| `9000` | Other Services |

### 3.2 Office Category Codes
| Code | Category |
|---|---|
| `01` | Mandatory |
| `02` | Optional |
| `03` | Others |

### 3.3 Mandatory Office Codes (Category 01)
| Code | Office |
|---|---|
| `001` | Office of the Governor |
| `002` | Office of the Vice-Governor |
| `003` | Sangguniang Panlalawigan |
| `004` | SP Secretariat |
| `005` | Provincial Treasurer's Office |
| `006` | Provincial Assessor's Office |
| `007` | Provincial Accountant's Office |
| `008` | Provincial Engineer's Office |
| `009` | Provincial Budget Office |
| `010` | Provincial Planning and Development Office |
| `011` | Provincial Legal Office |
| `012` | Office of the Provincial Administrator |
| `013` | Provincial Health Office |
| `014` | Provincial Social Welfare and Development Office |
| `015` | General Services Office |
| `016` | Office of the Provincial Agriculturist |
| `017` | Office of the Provincial Veterinarian |
| `018` | Provincial DRRMO |
| `019` | Provincial Internal Audit Service |
| `020` | PDAO |
| `021` | PESO |
| `022` | Provincial Youth Development Office |

### 3.4 Optional Office Codes (Category 02)
| Code | Office |
|---|---|
| `001` | Provincial Population Office |
| `002` | ENRO |
| `003` | Provincial Architect |
| `004` | Provincial Information Office |
| `005` | Provincial Agricultural and Biosystems Engineer |
| `006` | Provincial Cooperatives Development Office |
| `007` | Provincial Tourism Office |

### 3.5 Funding Sources
- General Fund (GF)
- GF – 20% Development Fund
- GF – LDRRMF (Local Disaster Risk Reduction Management Fund)
- GF – Special Education Fund (SEF)
- GF – 5% GAD Fund
- Transfers from NGAs
- Transfers from GOCCs
- Transfers from Other LGUs
- Local Economic Enterprise Income
- Borrowings/Loans
- Grants/Donations

### 3.6 Objects of Expenditure (for WFP)
- **Personal Services (PS):** Salaries and Wages – Regular, PERA, RA, TA, Clothing Allowance, PIA, Overtime, Year-End Bonus, Cash Gift, RLIP, Pag-IBIG, PhilHealth, ECIP, Other Personnel Benefits
- **MOOE:** Traveling Expenses, Training and Scholarship, Office Supplies, Utility Expenses, Communication Expenses, Representation Expenses, Subscription Expenses, Repairs and Maintenance, Other MOOE
- **Capital Outlay (CO):** Land, Land Improvement, Buildings, Office Equipment, Furniture and Fixtures, IT Equipment, Motor Vehicles, Other CO

---

## 4. LDIP — Field Inventory & Web Form Design

### 4.1 Document Header Fields

| Field | Type | Notes |
|---|---|---|
| Province/LGU Name | `text` (read-only) | Pre-filled: "Occidental Mindoro" |
| Planning Period (Start Year) | `number` / year select | e.g., 2027 |
| Planning Period (End Year) | `number` / year select | e.g., 2029 |
| Date Prepared | `date` | |
| Prepared By | `text` | |
| Reviewed By | `text` | |
| Approved By | `text` | |

### 4.2 Office Header Row Fields (5-segment code)

| Field | Col | Type | Notes |
|---|---|---|---|
| AIP Reference Code | A | `text` (auto-generated) | Built from sector + LGU type + category + office code |
| Sector | — | `select` | General / Social / Economic / Others |
| LGU Type | — | `select` (hidden/fixed) | Always `1` for Province |
| Office Category | — | `select` | Mandatory / Optional / Others |
| Office Code | — | `select` | Dropdown from office list |
| Office Name (Sub-label) | B | `text` | For sub-offices of same code (e.g., "OFFICE OF THE GOVERNOR - HOUSING") |

### 4.3 Program/PPA Row Fields (6-segment code)

| Field | Col | Type | Notes |
|---|---|---|---|
| AIP Reference Code | A | `text` (auto-generated) | Parent code + sequence number |
| PPA Name | C | `textarea` | Program/Project/Activity description; no line breaks |
| Implementing Office/Dept | F | `text` or `select` | Abbreviation (e.g., PGO, PPDO) |
| Start Year | G | `number` | Fiscal year start |
| Completion Year | H | `number` | Fiscal year end |
| Expected Outputs / MFO | I | `textarea` | MFO for programs; immediate outputs for activities |
| Funding Source | J | `select` (multi) | See §3.5 |
| Personal Services (PS) | K | `number` | In thousand pesos |
| MOOE | L | `number` | In thousand pesos |
| Capital Outlay (CO) | M | `number` | In thousand pesos |
| Total | N | `number` (auto-calculated) | PS + MOOE + CO |
| CC Adaptation Amount | O | `number` | Climate Change Adaptation; in thousand pesos |
| CC Mitigation Amount | P | `number` | Climate Change Mitigation; in thousand pesos |
| CC Typology Code | Q | `text` or `select` | Per DILG JMC 2015-01 |
| PDP/RDP Alignment | R | `text` | |
| SDGs | S | `text` or `multiselect` | SDG numbers |
| Sendai Framework | T | `text` | |
| NDRRM Plan | U | `text` | |
| NSP | V | `text` | National Security Plan |
| PDPDFP | W | `text` | Provincial Dev. & Physical Framework Plan |

### 4.4 Form Behavior Notes
- **Hierarchy:** Office headers are parents; programs are children. The UI should allow adding programs under an office.
- **Auto-generation of AIP code:** Assembled from sector + "000" + LGU type + category + office code + sequence number.
- **Multiple sub-offices under one code:** The same 5-segment code (e.g., `3000-000-1-01-001`) can appear multiple times with different office sub-labels (e.g., "OFFICE OF THE GOVERNOR - HOUSING").
- **Total is always computed:** PS + MOOE + CO; never manually entered.
- **Amounts in thousands:** Display as "In Thousand Pesos."
- **Office appears in multiple sectors:** An office (e.g., OPVet code `017`) can appear in General, Social, Economic, and Others — each sector has its own row for the same office.

---

## 5. AIP — Field Inventory & Web Form Design

The AIP is the **annual slice** of the LDIP. It uses the same column structure but adds:
- A specific **Fiscal Year** (single year, not a range)
- An **eSRE Code** column
- A deeper **4-level hierarchy** (up to 8-segment codes)

### 5.1 Document Header Fields

| Field | Type | Notes |
|---|---|---|
| Province/LGU Name | `text` (read-only) | Pre-filled |
| Fiscal Year | `number` / year select | e.g., 2027 |
| "As of" Date | `date` | Date of last update |
| Prepared By | `text` | |

### 5.2 Hierarchy Levels (AIP is more granular than LDIP)

| Level | Segments | Col Used | Description |
|---|---|---|---|
| 1 — Office | 5 | B | Office name; has budget subtotals (PS, MOOE, CO computed from children) |
| 2 — Program | 6 | C | Program name |
| 3 — Sub-program/Activity Group | 7 | D | Activity group or sub-program name |
| 4 — Activity/Line Item | 8 | E | Specific activity with full budget detail |

### 5.3 Fields Common to All Levels

| Field | Col | Type | Notes |
|---|---|---|---|
| AIP Reference Code | A | `text` (auto-generated) | |
| Description | B/C/D/E | `text`/`textarea` | Column depends on level |

### 5.4 Additional Fields at Activity Level (8 segments)

| Field | Col | Type | Notes |
|---|---|---|---|
| eSRE Code | F | `text` | Electronic Statement of Receipts and Expenditures code; submitted to DOF-BLGF |
| Implementing Office/Dept | G | `text` or `select` | |
| Start Date / Month | H | `select` (month) | e.g., "January" |
| Completion Date / Month | I | `select` (month) | e.g., "December" |
| Expected Outputs | J | `textarea` | Specific, measurable output |
| Funding Source | K | `select` | GF, LDRRMF, SEF, etc. |
| Personal Services (PS) | L | `number` | In thousands |
| MOOE | M | `number` | In thousands |
| Capital Outlay (CO) | N | `number` | In thousands |
| Total | O | `number` (auto-calculated) | |
| CC Adaptation Amount | P | `number` | |
| CC Mitigation Amount | Q | `number` | |
| CC Typology Code | R | `text`/`select` | |
| PDP/RDP | S | `text` | |
| SDGs | T | `text`/`multiselect` | |
| Sendai Framework | U | `text` | |
| NDRRM Plan | V | `text` | |
| NSP | W | `text` | |

### 5.5 Rollup Logic
- **Office totals:** Sum of all programs/activities under it
- **Program totals:** Sum of all activities under it
- All `Total` fields are computed (PS + MOOE + CO)
- Office rows show aggregated budget for the entire office; individual activities show their own budget

---

## 6. WFP — Field Inventory & Web Form Design

The WFP is a **per-department** document with a detailed breakdown of each expenditure item by quarter and object of expenditure. It uses the same AIP reference code hierarchy as the AIP (up to 8 segments).

### 6.1 Document Header Fields

| Field | Type | Notes |
|---|---|---|
| Department/Office | `text` or `select` | e.g., "Provincial Planning and Development Office" |
| Fiscal Year | `number` | e.g., 2026 |
| Fund Type | `select` | General Fund / 20% DF / GAD Fund / LDRRMF / etc. |
| Prepared By | `text` | |
| Date Prepared | `date` | |

### 6.2 PPA Hierarchy Row Fields

| Field | Col | Type | Notes |
|---|---|---|---|
| AIP Reference Code | 1 | `text` (auto/lookup) | Must match approved AIP |
| Programs, Projects and Activities | 2 | `textarea` | PPA description |

### 6.3 Expenditure Line Item Fields (per account code row)

| Field | Col | Type | Notes |
|---|---|---|---|
| Resources Needed | 3 | `text` | e.g., "Manpower", "Meals and snacks", "Traveling expenses" |
| Responsible Unit/Division | 4 | `text` or `select` | e.g., "Admin Division", "Planning Division" |
| Success Indicator | 5 | `textarea` | Measurable target or output |
| Means of Verification | 6 | `textarea` | e.g., "Payroll, Disbursement Vouchers", "Attendance sheet" |
| Account Code | 7 | `text` | COA account code, e.g., `5-01-01-010` |
| Object of Expenditure | 8 | `text` or `select` | e.g., "Salaries and Wages – Regular", "PERA", "Traveling Expenses" |
| Q1 Amount | 9 | `number` | 1st Quarter |
| Q2 Amount | 10 | `number` | 2nd Quarter |
| Q3 Amount | 11 | `number` | 3rd Quarter |
| Q4 Amount | 12 | `number` | 4th Quarter |
| Total Appropriation (Gross) | 13 | `number` (auto-calculated) | Q1+Q2+Q3+Q4 |
| 10% Reserve | 14 | `number` (auto-calculated) | Total × 10% |
| Net Total Appropriation | 15 | `number` (auto-calculated) | Total − Reserve |
| Source of Fund | 16 | `select` | GF, 20% DF, GAD Fund, LDRRMF, etc. |

### 6.4 Form Behavior Notes
- **Three-level structure:** PPA header → Object of Expenditure group header (PS / MOOE / CO) → individual account code line items
- **Sub-totals:** PS subtotal, MOOE subtotal, CO subtotal — all auto-computed
- **Quarterly allocation:** User enters per-quarter amounts; total is auto-summed
- **Account Code lookup:** Should link to COA (Chart of Accounts) for LGUs (COA Circular 2015-009)
- **Multiple fund types per PPA:** A single PPA may have expenditure lines from GF and LDRRMF simultaneously

---

## 7. Cross-Document Relationships

```
LDIP (multi-year, all offices, 5–6 segment codes)
  └─► AIP (single year, all offices, 5–8 segment codes)
         └─► WFP (per department, up to 8 segment codes, quarterly breakdown)
```

- AIP reference codes in the WFP **must match** codes in the approved AIP
- The WFP is used to prepare the **LBP Form No. 2** (Programmed Appropriation by Object of Expenditure) submitted during budget preparation
- The **LBP Form No. 4** (Mandate/Vision/MFO/Performance Indicators) also maps to the PPA structure

---

## 8. Suggested Database Schema (Entity Overview)

```
Province / LGU
  ├── Sectors (General, Social, Economic, Others)
  ├── Offices (code, name, category, sector)
  │     └── Sub-office labels (same code, different name)
  ├── LDIP
  │     ├── Header (planning_period_start, planning_period_end)
  │     └── PPA_Items (aip_ref_code, level, name, office_id, start_yr, end_yr,
  │                     expected_outputs, funding_source, ps, mooe, co,
  │                     cc_adaptation, cc_mitigation, cc_typology,
  │                     pdp_rdp, sdgs, sendai, ndrrm, nsp, pdpdfp)
  ├── AIP
  │     ├── Header (fiscal_year, as_of_date)
  │     └── PPA_Items (aip_ref_code, level[1-4], name, parent_code,
  │                     esre_code, implementing_office, start_month, end_month,
  │                     expected_outputs, funding_source,
  │                     ps, mooe, co, [cc fields], [alignment fields])
  └── WFP
        ├── Header (fiscal_year, department, fund_type)
        └── PPA_Items (aip_ref_code, ppa_name)
              └── Expenditure_Lines (resources_needed, responsible_unit,
                                      success_indicator, means_of_verification,
                                      account_code, object_of_expenditure,
                                      q1, q2, q3, q4, source_of_fund)
```

---

## 9. Key Business Rules for Validation

1. **AIP code format:** Must match `\d{4}-000-[123]-0[123]-\d{3}(-\d{3}){0,3}`
2. **Total = PS + MOOE + CO:** Always auto-computed; user cannot override
3. **CC amounts ≤ Total:** CC Adaptation + CC Mitigation cannot exceed the PPA total
4. **Office code uniqueness per sector:** Same office may appear in multiple sectors but each sector row is independent
5. **AIP codes in WFP must exist in AIP:** Foreign key validation
6. **WFP quarterly sum = Total:** Q1+Q2+Q3+Q4 must equal the total appropriation
7. **Start date ≤ Completion date:** Enforced at program and activity level
8. **Funding source required at leaf level:** Office and program headers may aggregate; individual activities must have a funding source
9. **Amount units:** All monetary values in **thousand pesos** — the web form should clarify this to users

---

## 10. Notes for Claude Code

- **LDIP is currently an Excel file** (`LDIP 2027-2029 formatted.xlsx`) with 4 sheets (General, Social, Economic, Others). Each sheet uses the same column layout. The file has ~82 columns but most are blank or formula-based beyond column W.
- **AIP is also Excel** (`AIP_2027 as of June 2, 2026.xlsm`) with 4 sector sheets. It has VBA macros for auto-population from the LDIP (being replaced by this web app).
- **WFP is Excel** (`PPDO_WFP 2026_FinalVersion_PPDORevwd_PBORevwd_PTORevwd.xlsx`) with multiple sheets per fund type (GF, GAD, LDRRMF, etc.).
- The existing data should be importable from these Excel files as a migration path.
- The AIP reference code is the **primary key** across all three systems and must be treated with care — especially the sub-office label distinction (same 5-segment code, different name).
- Consider a **tree/accordion UI** for the hierarchical PPA structure rather than a flat table.
- The **LBP Form No. 4** (WFP in the files) maps to the AIP hierarchy and needs a separate view/export that groups by: AIP Reference Code → MFO → Performance Indicator → Budget (PS/MOOE/CO).
