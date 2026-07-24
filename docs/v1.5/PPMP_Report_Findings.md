# v1.5 — PPMP Report: Findings & Initial Draft

> Research + gap analysis for adding a **Project Procurement Management Plan (PPMP)** report to
> Budget Planning › Report, alongside the existing WFP report.
> Status: **findings only** — no tickets cut yet. Open questions at the end need Ralph's / the
> PBO's answers before implementation scope is fixed.
> Date: 2026-07-23 · Milestone: `v1.5 — PPMP Report` · Branch: `release/1.5.0-ppmp`

---

## 1. Headline finding

**The portal already stores real procurement line items.** The v1.4 WFP Rework
(RAL-118/119/120/125/127/138) shipped `WfpProcurementItem` — name, unit, unit price, quantity,
number of days, and a computed line total — snapshotted from the GSO price index at save time and
hanging off every WFP expenditure.

That means the PPMP is **not a new data-entry module**. It is largely a **re-projection of data the
WFP entry wizard already captures**, regrouped from "by expense class / account" into "by
procurement project", plus a small set of genuinely procurement-specific fields that nothing in the
portal records today (mode of procurement, procurement timeline, pre-proc conference flag).

Rough split against the official 12-column form: **5 columns fully derivable, 2 partially
derivable, 5 genuinely new.** Detail in §4.

---

## 2. What the PPMP is, in the Philippine setting

The PPMP is the **end-user unit's** procurement plan for its own programs, activities and projects
(PAPs). Each implementing unit prepares one; the BAC/Procurement Unit then **consolidates all the
units' PPMPs into the agency's Annual Procurement Plan (APP)**. The PPMP is therefore the *input*
document, the APP the *output* — they are not the same form, and this ticket is about the PPMP.

Two flavours, distinguished by a checkbox on the form itself:

| Flavour | When | Budget column means |
|---|---|---|
| **Indicative** | Prepared alongside the budget proposal, before the appropriation is approved | "Estimated Budget" |
| **Final** | Prepared after the General Appropriations Act / appropriation ordinance is passed | "Authorized Budgetary Allocation" |

For a provincial LGU like Occidental Mindoro the approving instrument is the **appropriation
ordinance**, not the GAA — the form's Column 9 guidance explicitly allows for this.

Each successive submission is numbered: the first Indicative PPMP is **No. 1**, the next
update/Final is **No. 2**, and so on. That numbering is per end-user unit, per fiscal year.

### Legal basis — use RA 12009, not RA 9184

Procurement moved from **RA 9184** (Government Procurement Reform Act) to **RA 12009** (New
Government Procurement Act) — its IRR was issued via **GPPB Resolution No. 02-2025**. The PPMP
requirements now sit in **IRR §7.7**, and GPPB has published a new NGPA PPMP form to match.

**This draft is based on the RA 12009 form**, not the older RA 9184 one. The two differ materially:
the new form adds the Indicative/Final flag, the PPMP number, the pre-procurement conference
column, the "Attached Supporting Document/s" column (with a **mandatory Market Scoping Checklist**),
and it drops the old form's PS/MOOE/CO budget split in favour of a single budget column.

⚠️ Worth confirming with the PBO/BAC which form the province is actually filing on right now — some
LGUs are still on the RA 9184 layout during the transition. See Q1 in §7.

---

## 3. The official form structure (RA 12009 / IRR §7.7)

Source: <https://www.gppb.gov.ph/wp-content/uploads/2025/08/NGPA_PPMP.pdf>

### Header

```
[Agency Letterhead with Logo]
PROJECT PROCUREMENT MANAGEMENT PLAN (PPMP) NO. ___        [ ] INDICATIVE   [ ] FINAL
Fiscal Year : ______
End-User or Implementing Unit : ______
```

### Table — 12 numbered columns under 3 group headings

| # | Column heading (verbatim) | Group |
|---|---|---|
| 1 | General Description and Objective of the Project to be Procured | PROCUREMENT PROJECT DETAILS |
| 2 | Type of the Project to be Procured (whether Goods, Infrastructure and Consulting Services) | PROCUREMENT PROJECT DETAILS |
| 3 | Quantity and Size of the Project to be Procured | PROCUREMENT PROJECT DETAILS |
| 4 | Recommended Mode of Procurement | PROCUREMENT PROJECT DETAILS |
| 5 | Pre-Procurement Conference, if applicable (Yes/No) | PROCUREMENT PROJECT DETAILS |
| 6 | Start of Procurement Activity | PROJECTED TIMELINE (MM/YYYY) |
| 7 | End of Procurement Activity | PROJECTED TIMELINE (MM/YYYY) |
| 8 | Expected Delivery/Implementation Period | PROJECTED TIMELINE (MM/YYYY) |
| 9 | Source of Funds | FUNDING DETAILS |
| 10 | Estimated Budget / Authorized Budgetary Allocation (PhP) | FUNDING DETAILS |
| 11 | Attached Supporting Document/s | — |
| 12 | Remarks | — |

Closing row: **`TOTAL BUDGET:`** — sum of Column 10.

### Footer

```
Prepared by:                              Submitted by:
_______________________________           _______________________________
Signature over Printed Name               Signature over Printed Name
Position/Designation                      Position/Designation
[End-User or Implementing Unit]           [Head of the End-User or Implementing Unit]
Date : ______                             Date : ______
```

### Per-column rules worth encoding

- **Col 1** — concise description *and* intended purpose. Must state whether the project is
  implemented **through procurement or by administration**.
- **Col 2** — `Goods` / `Infrastructure Projects` / `Consulting Services`. General Support Services
  is filed as `Goods (General Support Services)`.
- **Col 3** — *both* quantity and size. **Each lot goes on its own row.** If the item list is too
  long for the cell, the form explicitly permits a separate attachment.
- **Col 4** — a mode valid under RA 12009. Competitive Bidding is the default; the alternative
  methods (Limited Source Bidding, Direct Contracting, Repeat Order, Shopping, Negotiated
  Procurement and its sub-types) are exceptional. By-administration rows are filled `N/A`.
- **Col 5** — `Yes` / `No` / `N/A`.
- **Cols 6–8** — `MM/YYYY`. Col 7 is specifically the projected month of **Notice of Award / Purchase
  Order issuance**. Col 8 accepts a range (`06/2026 to 06/2028`).
- **Col 10** — Estimated Budget on an Indicative PPMP, Authorized Budgetary Allocation on a Final one.
- **Col 11** — the **Market Scoping Checklist is a mandatory attachment for every project**; plus
  Technical Specifications / Detailed Engineering / Scope of Work / Terms of Reference as applicable.

---

## 4. Column-by-column gap analysis against the current schema

Relevant chain already in the database:

```
AipRecord → AipOffice → AipProgram → AipProject → AipActivity
                                                       ↑
                                            WfpRecord → WfpActivity  (FK AipActivityId)
                                                             ↓
                                                       WfpExpenditure          (Account, FundingSource,
                                                             ↓                  Nature, Frequency, Q1–Q4)
                                                       WfpProcurementItem      (PeriodNo, Name, Unit,
                                                                                UnitPrice, Qty,
                                                                                NumberOfDays, LineTotal)
```

Legend: ✅ derivable today · ⚠️ partially derivable · ❌ not stored anywhere

| # | Column | Status | Source / what's missing |
|---|---|---|---|
| — | Fiscal Year | ✅ | `WfpRecord.FiscalYear` / `AipRecord` |
| — | End-User or Implementing Unit | ✅ | `Office` + `Division` (also `AipActivity.ImplementingOffice`) — see Q3 |
| — | PPMP No. | ❌ | New. Per (unit, FY) sequence |
| — | Indicative / Final | ❌ | New. Could map off `WfpRecord` status (Draft→Indicative, Finalized→Final) — see Q4 |
| 1 | General Description and Objective | ⚠️ | `AipActivity.Name` + program/project names + `ExpectedOutputs` give a description; **objective** and the *procurement vs. by-administration* statement are not captured |
| 2 | Type (Goods / Infra / Consulting) | ❌ | New field. Could be *suggested* from the account's expense class (MOOE→Goods, CO→Infrastructure) but that is a guess, not data — must be user-set |
| 3 | Quantity and Size | ⚠️ | **Quantity ✅** — `WfpProcurementItem.Qty` + `.Unit`. **Size ❌** — no spec field; `PriceIndexItem.Name` often embeds it as free text ("15-inch monitor"), which is not reliably parseable |
| 4 | Recommended Mode of Procurement | ❌ | New field + a config list of RA 12009 modes |
| 5 | Pre-Procurement Conference | ❌ | New field (Yes/No/N/A) |
| 6 | Start of Procurement Activity | ❌ | New field. Not inferable — depends on the mode's prescribed timeline |
| 7 | End of Procurement Activity | ❌ | New field. Same |
| 8 | Expected Delivery/Implementation Period | ✅ | Derivable from `WfpProcurementItem.PeriodNo` + `WfpExpenditure.Frequency` (M=1–12, Q=1–4, B=1–2, A=1). `AipActivity.StartDate`/`EndDate` are a fallback but are month-name strings ("January"), not dates |
| 9 | Source of Funds | ✅ | `WfpExpenditure.FundingSourceNameSnapshot` — already fund-scoped since v1.4.3 |
| 10 | Estimated Budget / Authorized Allocation | ✅ | `SUM(WfpProcurementItem.LineTotal)` at whatever grain a row ends up being |
| 11 | Attached Supporting Document/s | ❌ | New. Text list for the draft; real file upload is a later call |
| 12 | Remarks | ❌ | New free text |
| — | TOTAL BUDGET | ✅ | Computed |
| — | Prepared by / Submitted by | ⚠️ | `User.FullName` + `Position` exist; who signs as "Submitted by" (head of unit) is not modelled — see Q5 |

**Summary: 5 ✅ · 3 ⚠️ · 7 ❌** (counting header/footer rows). The five ❌ table columns (2, 4, 5, 6,
7) plus 11 and 12 are all **procurement-officer judgement fields** — they can't be computed from
budget data no matter what, so some entry surface is unavoidable.

---

## 5. The central design question: what is one PPMP row?

The form's row grain is **one procurement project (or one lot)**. The portal's finest grain is
**one `WfpProcurementItem`, per period**. Dumping items 1:1 would produce thousands of rows for a
single office — a monthly-frequency expenditure with 12 items yields 144 rows on its own.

Three candidate groupings:

| Option | Row = | Rows/office (rough) | Notes |
|---|---|---|---|
| **A (recommended)** | one `WfpExpenditure` (= one activity × one account × one fund source) | tens | Account title becomes the natural Col 1 description; the expenditure's items are aggregated into Col 3; budget = the expenditure's own total. Aligns with how a procurement *package* is actually let. |
| B | one `AipActivity` | few | Too coarse — one activity mixes goods, training, and CO, which need different modes and types in Cols 2/4. |
| C | one `WfpProcurementItem` | hundreds–thousands | Matches no real procurement project; unusable as a filed document. |

**Recommendation: Option A.** It lands on the same grain the procurement-specific fields (mode,
type, pre-proc conference, timeline) naturally attach to, keeps the row count filable, and the
form's own Col 3 guidance — *"If the number of items is too large to fit in this column, a separate
attachment may be used"* — anticipates exactly the many-items-in-one-row shape it produces.

Where an expenditure genuinely needs splitting into lots, that is an explicit user action (a
"split into lots" affordance), not an automatic rule.

---

## 6. Proposed shape of the initial draft

Deliberately mirroring how the WFP report was built (RAL-132 preview first, Excel export later in
v1.4.4) rather than inventing a new pattern:

1. **Report page** — add `{ value: "PPMP", label: "Project Procurement Management Plan (PPMP)" }`
   to the existing `REPORT_TYPES` array in
   [report/page.tsx](frontend/src/app/(portal)/budget-planning/report/page.tsx). **WFP stays the
   default.** Same fiscal-year / office / division selectors, same division-clamp rule (RAL-136:
   a division-scoped caller is forced server-side to their own division), same
   `canAccessBudgetPlanning` gate.
2. **Backend** — `PpmpReportService` + `GET /api/budget-planning/ppmp/report/preview`, following
   `WfpReportService`'s shape and the `{ data, error, message }` envelope. Slim DTO, server-side
   aggregation, **no `GetAllAsync()`-then-filter** (`docs/PERFORMANCE_GUIDELINES.md`) — the v1.4.6
   round of N+1 fixes is recent enough that this must be right the first time.
3. **Read-only first pass** — render the 5 ✅/⚠️ derivable columns with real data and leave the 7
   procurement-officer columns visibly blank (an em-dash placeholder), exactly as the WFP report
   omitted its uncaptured narrative columns rather than faking them. This gets a reviewable
   artefact in front of the PBO/BAC fast and lets *them* confirm the row grain before we build an
   entry surface for the missing fields.
4. **Then** — a `PpmpProjectDetail` table keyed to the chosen grain to hold Cols 2, 4, 5, 6, 7, 11,
   12 + the header's PPMP No./Indicative-Final, and an entry screen for it.
5. **Then** — **`.xlsx` export** (confirmed requirement, see §6.1 below).

Splitting 3 from 4 matters: step 3 is cheap, has zero schema cost, and de-risks step 4's schema
decision by getting the grain validated against a real filed document first.

### 6.1 Excel export — confirmed

The PPMP report **will export to `.xlsx`**, same as WFP. It is not a print-only view. This is
confirmed scope, not a maybe, and it changes two things about the earlier steps:

- **The DTO must serve both surfaces from day one.** The preview and the export read the same
  `PpmpReportDto`; the preview's columns should map **1:1 onto the official form's 12 columns**
  rather than being a convenient web-shaped subset. Getting this wrong means reshaping the DTO
  later.
- **Row grain (§5) is now load-bearing for the export too.** A filed `.xlsx` has to be a document
  a BAC will accept — which is a much harder constraint than a browser preview, and another reason
  to validate Option A against a real filed PPMP before building.

**Reuse v1.4.4's approach, and its hard-won lesson.** `WfpReportExcelService` deliberately does
**not** clone rows out of a reference workbook — the province's `WFP-NEW.xlsx` turned out to be a
*filled sample*, not a blank template (293 merged ranges, borders hand-touched row-to-row), and
cloning from it was a merge-corruption risk. The shipped design instead **builds the sheet
programmatically in ClosedXML** from a documented style catalog (fills/fonts/borders/number-formats/
column-widths as C# constants) applied per row type, with merges computed from each block's actual
emitted row count. **Do the same for PPMP** — assume any PPMP sample the province hands over is
likewise a filled sample, not a template.

Conventions to carry over:

- New interface `IPpmpReportExcelService` in Application, implementation in Infrastructure —
  **named distinctly on purpose**, the same way `IWfpReportExcelService` was named to avoid
  colliding with the older legacy `IWfpExcelService`.
- Endpoint `GET /api/budget-planning/ppmp/report/export`, sibling of the preview endpoint, same
  office/fiscalYear/divisionId scoping and the same division clamp.
- Filename built **client-side**, not read from `Content-Disposition` — matching
  `buildExportFilename` in [report/page.tsx](frontend/src/app/(portal)/budget-planning/report/page.tsx).
  Proposed: `PPMP{fiscalYear}_{officeCode}[_{divisionCode}]_{ppmpNo}_yyyyMMddHHmmss.xlsx` — needs
  Ralph's call on whether the PPMP No. belongs in the name.
- Unit tests against the **structural** logic (row counts, merge spans, totals, no formulas), not
  the styling — v1.4.4's 7 tests stayed green through every colour/width iteration precisely
  because they tested structure. Expect the same iterate-on-styling loop here.

Form-specific things the WFP exporter did not have to handle:

- The header block's **checkbox pair** (Indicative / Final) — a checked box in a cell, plus the
  PPMP No.
- The **agency letterhead with logo** the form calls for at the top.
- The three **column group headings** (PROCUREMENT PROJECT DETAILS / PROJECTED TIMELINE (MM/YYYY) /
  FUNDING DETAILS) spanning cols 1–5, 6–8, 9–10 — merged header cells above the 12 column names.
- The **signatory footer** (Prepared by / Submitted by), which the WFP export did not render at all.
- `MM/YYYY` text formatting in cols 6–8, including the range form (`06/2026 to 06/2028`).

---

## 7. Open questions

| # | Question | Why it blocks |
|---|---|---|
| **Q1** | Is the province filing on the **RA 12009** form or still on the older **RA 9184** layout? Can we get a real filled-in provincial PPMP to check against? | Different columns. This draft assumes RA 12009. A real sample would settle Q2/Q3 too. |
| **Q2** | Confirm the row grain — is **Option A** (one row per activity × account × fund source) what the BAC expects? | Drives the whole DTO and the new table's key. |
| **Q3** | Is the "End-User or Implementing Unit" the **Office** (PPDO) or the **Division**? Does each division file its own PPMP, or does PPDO file one consolidated? | Decides whether division is a filter or part of the report's identity. |
| **Q4** | Should Indicative/Final derive from the WFP record's Draft/Finalized status, or be an explicit user choice per submission? | Affects whether PPMP No. can be auto-sequenced. |
| **Q5** | Who signs "Submitted by" — is the head of the end-user unit derivable from `Division`/`Office`, or free text? | Small, but it's on the form. |
| **Q6** | Should the RA 12009 **modes of procurement** be a seeded config table (like `funding_sources` / `accounts`) or a hardcoded enum? | Config table is consistent with the rest of the app; enum is cheaper. Leaning config table. |
| **Q7** | Column 11 — text list of attachment names for now, or real file upload from the start? | Text is the cheap draft; upload is a much bigger ticket. |
| **Q8** | Is PPMP scope **procurement items only**, or must non-procurement expenditures appear too? `WfpExpenditure.Nature` is `Procurement` / `Non-Procurement` / `Combined` — the natural filter is `Nature != "Non-Procurement"`, but a by-administration project still belongs on a PPMP (the form has explicit `N/A` handling for it). | Decides the base query's filter. |
| **Q9** | How exact does the `.xlsx` need to be — a **faithful reproduction of the GPPB form** (letterhead, checkboxes, merged group headings, signatory block) that gets printed and signed as-is, or a clean data export the unit pastes into their own copy of the form? | Large effort difference. v1.4.4's WFP export went faithful; assuming the same here unless told otherwise. |
| **Q10** | Export filename — include the PPMP No.? Proposed `PPMP{FY}_{office}[_{division}]_{ppmpNo}_{timestamp}.xlsx`. | Cosmetic, but the WFP convention was Ralph's own call so this should be too. |
| **Q11** | **Column 10 — items total or total appropriation?** Where a WFP reserve is applied the two differ: `SUM(WfpProcurementItem.LineTotal)` is the true cost of the goods, while `WfpExpenditure.TotalAppropriation` adds the reserve on top. Found live in the sample: ₱30,000.00 vs ₱33,000.00 and ₱41,991.00 vs ₱46,190.10. | Changes every budget figure and the TOTAL BUDGET. The sample uses the **items total** (the ABC basis) — needs confirming. |

### Worked sample

A sample of this form was built from real local dev data (`wfp_records.id = 6`, PPDO / Planning
Division, FY2027, Draft) to test the §5 recommendation concretely. It confirmed that **44
procurement line items collapse to 5 filable rows** at the Option A grain, and it is what surfaced
Q11.

The sample workbook is deliberately NOT committed — this is a public repository and the file
carried real (draft, unapproved) budget figures. Regenerate it locally if needed.

---

## 8. References

- GPPB — official NGPA PPMP form (RA 12009): <https://www.gppb.gov.ph/wp-content/uploads/2025/08/NGPA_PPMP.pdf>
- RA No. 12009, New Government Procurement Act: <https://ps-philgeps.gov.ph/home/images/legalbases/2025/New%20Government%20Procurement%20Act%20_%20Republic%20Act%20No.%2012009.pdf>
- IRR of RA 12009, GPPB Resolution No. 02-2025: <https://www.dbm.gov.ph/wp-content/uploads/Issuances/2025/GPPB-Resolution/IRR-RA-12009-Resolution-No-02-2025.pdf>
- GPPB-TSO — Fit-for-Purpose Procurement Modalities under RA 12009: <https://www.gppb.gov.ph/fit-for-purpose-procurement-modalities-under-ra-12009/>
- GPPB-TSO — Understanding Competitive Bidding under RA 12009: <https://www.gppb.gov.ph/understanding-competitive-bidding-under-ra-12009/>

### Internal

- `frontend/src/app/(portal)/budget-planning/report/page.tsx` — the page PPMP plugs into
- `backend/PPDO.Domain/Entities/WfpProcurementItem.cs` — the line items PPMP is built from
- `backend/PPDO.Domain/Entities/WfpExpenditure.cs` — proposed row grain (Option A)
- `docs/PERFORMANCE_GUIDELINES.md` — query rules for the new endpoint
- `docs/v1.4.4/WFP_Excel_Export_Assessment.md` — the export approach to reuse later
- `docs/TICKET_PROMPT_STANDARD.md` — structure for the tickets once Q1–Q8 are answered
