# v1.5 — PPMP Report: Findings & Initial Draft

> Research + gap analysis for adding a **Project Procurement Management Plan (PPMP)** report to
> Budget Planning › Report, alongside the existing WFP report.
> Milestone: `v1.5 — PPMP Report` · Branch: `release/1.5.0-ppmp`
>
> **Revised 2026-07-24** after Ralph supplied a real filed PPMP (`ppmp admin2027.xlsx`, PPDO
> Administrative Division, GENERAL FUND). That file **answered Q1 and Q2 and overturned this
> document's original two main conclusions.** The superseded GPPB-form analysis is kept as
> Appendix A — it is still the national standard and still matters, just not for what we build first.

---

## 1. Headline findings

**1. The province files its own working format, not the GPPB form.** The original draft assumed the
official GPPB PPMP form under RA 12009 (12 numbered columns, Indicative/Final flag, per-project
rows). The real file uses a **different, item-level layout** with an AIP-anchored hierarchy and a
quarterly schedule. Build against the real one. *(Q1 — answered.)*

**2. The row grain is one row per procurement ITEM, not per procurement project.** The original
draft recommended "Option A" — one row per WFP expenditure. The real file puts **every catalogue
item on its own row** (104 item rows in one division's file), with hierarchy and account rows as
interleaved section headers. Option A is wrong for this form. *(Q2 — answered.)*

**3. This makes the report substantially cheaper.** The real form's columns line up almost exactly
with `WfpProcurementItem` — including the quarterly qty/amount split, which maps directly onto
`PeriodNo`. **11 of 13 columns are derivable from data the portal already holds.** The remaining
two are Stock Card No. (added this round — see §6) and Mode of Procurement (which is **blank in
every one of the 104 rows** of the reference file).

The PPMP is a **re-projection of WFP data**, grouped by procurement item instead of by expense
class. It is not a new data-entry module.

---

## 2. The real form (`ppmp admin2027.xlsx`)

### Header

```
PROJECT PROCUREMENT MANAGEMENT PLAN (PPMP)
CY 2025                                          ← note: file is the FY2027 plan (see §5)
END-USER/UNIT :  PROVINCIAL PLANNING AND DEVELOPMENT OFFICE (PPDO)
Charged to: GENERAL FUND                         ← one file per fund source
```

### Columns (two-tier header; cols L–W sit under "SCHEDULE/MILESTONE OF ACTIVITIES")

| Col | Heading | Source in the portal |
|---|---|---|
| E | Item No. | ✅ derived (row counter) — blank in 93 of 104 reference rows anyway |
| F | AIP REFERENCE CODE | ✅ `aip_programs` / `aip_projects` / `aip_activities` `.ref_code` |
| G | DESCRIPTION | ✅ dual-purpose — hierarchy/account name on section rows, `wfp_procurement_items.name` on item rows |
| H | Stock Card No. | ✅ **added this round** — `price_index_items.stock_card_no` (§6) |
| I | Short Category | ✅ `price_index_items.category` |
| J | Unit | ✅ `wfp_procurement_items.unit` |
| K | Unit price | ✅ `wfp_procurement_items.unit_price` |
| L | QTY. | ✅ `wfp_procurement_items.qty` |
| M | Est. Budget | ✅ `wfp_procurement_items.line_total` (= qty × unit price × days) |
| N | Mode of Proc. | ❌ not stored — **and blank in all 104 reference rows** |
| O–V | 1ST / 1ST QRTR AMOUNT … 4TH / 4TH QRTR AMOUNT | ✅ `wfp_procurement_items.period_no` + qty + line_total |
| W | TOTAL | ✅ computed — never filled in the reference file |

### Row structure

Section rows carry no unit price; item rows do. That single rule cleanly separates them:

```
ADMINISTRATIVE DIVISION GEN. FUND              ← division banner
  1000-000-1-01-010-002-001   Project name     ← AIP project (7-segment ref)
    1000-…-002-001-001        Activity name    ← AIP activity (8-segment ref)
      Office supplies expenses 5-02-03-010     ← account (WfpExpenditure), M = account total
        Bond paper A4 80gsm | OS-PAP-0000004 | Paper | ream | 494 | 40 | 19,760 | …quarters
        Bond Paper Short 80 gsm …
      Other Supplies and Materials 5-02-03-990 ← next account
        …
TOTAL ADMINISTRATIVE DIVISION GEN. FUND
Prepared by: …   NOTED BY: …
```

Six-segment refs appear too (e.g. `1000-000-1-01-010-004` = program), so the full
Program → Project → Activity → Account → Items nesting is present — exactly the WFP report's
hierarchy, one level deeper.

### Confirmed by arithmetic

- **Est. Budget = Qty × Unit Price × Days.** 98 of 104 rows are a plain qty × price; the 6
  "exceptions" are day-multiplied (e.g. ₱295 × 26 × 4 days = ₱30,680) — which is precisely what
  `WfpProcurementItem.NumberOfDays` (RAL-127/138) exists for. No mismatches once days are included.
- **The quarterly pairs are `PeriodNo` under quarterly frequency.** O/P = Q1 qty/amount, Q/R = Q2,
  S/T = Q3, U/V = Q4 — a direct projection of the portal's period rows.

---

## 3. The Summary sheet

A second sheet reconciles, per AIP activity, **PER AIP (MOOE / CO)** against **ACTUAL BUDGET
(MOOE / CO)** — i.e. the AIP allocation versus what the PPMP actually plans to spend, with the
variance visible per line and a grand total.

This is a working/reconciliation aid rather than part of the PPMP proper, but it is worth noting
that **the portal already computes both sides** (`AipActivity.Ps/Mooe/Co` and the WFP expenditure
rollups that `WfpCeilingService` uses). It could be generated for free as a second sheet of the
export. Whether it should be is Q13.

---

## 4. Revised implementation plan

1. **Report page** — add `{ value: "PPMP", label: "Project Procurement Management Plan (PPMP)" }`
   to `REPORT_TYPES` in
   [report/page.tsx](frontend/src/app/(portal)/budget-planning/report/page.tsx). **WFP stays the
   default.** Same fiscal-year / office / division selectors, same RAL-136 division clamp, same
   `canAccessBudgetPlanning` gate.
2. **One report per fund source**, mirroring the reference file's "Charged to: GENERAL FUND" header
   — and mirroring what the WFP report already does with `fundSourceReports`.
3. **Backend** — `PpmpReportService` + `GET /api/budget-planning/ppmp/report/preview`, shaped like
   `WfpReportService`, `{ data, error, message }` envelope. Slim DTO, server-side aggregation, no
   `GetAllAsync()`-then-filter (`docs/PERFORMANCE_GUIDELINES.md`) — the v1.4.6 N+1 round is recent.
4. **`.xlsx` export** — confirmed scope (§4.1).
5. **Mode of Procurement** — deliberately last. It is the only genuinely missing column, and the
   province has never filled it in. Ship the report without it, confirm whether it is actually
   wanted (Q12), and only then add the field.

### 4.1 Excel export — confirmed

The report exports to `.xlsx`, same as WFP; it is not print-only. Preview and export read the same
`PpmpReportDto`, so the preview's columns should map 1:1 onto the real form's columns rather than
being a convenient web-shaped subset.

**Reuse v1.4.4's approach and its hard-won lesson.** `WfpReportExcelService` deliberately does
**not** clone rows out of a reference workbook — the province's `WFP-NEW.xlsx` turned out to be a
*filled sample*, not a blank template (293 merged ranges, borders hand-touched row-to-row).
`ppmp admin2027.xlsx` is likewise a filled working file, with hand-maintained quirks (§5). Build the
sheet **programmatically in ClosedXML** from a documented style catalog.

Conventions to carry over: `IPpmpReportExcelService` (named distinctly, as `IWfpReportExcelService`
was, to avoid colliding with the legacy `IWfpExcelService`); endpoint
`GET /api/budget-planning/ppmp/report/export`; filename built client-side, matching
`buildExportFilename`; unit tests against **structure** (row counts, section nesting, totals), not
styling — that is why v1.4.4's tests survived every colour iteration.

---

## 5. Data-quality observations from the reference file

These are arguments *for* generating the PPMP rather than maintaining it by hand — each is a class
of error the portal removes by construction.

| Observation | Detail |
|---|---|
| Stale header | The FY2027 file's title block still reads **"CY 2025"** — copied forward and not updated. |
| Quarterly splits don't reconcile | In **31 of 104 rows** the quarterly quantities don't sum to the row's total QTY. |
| `TOTAL` column never filled | Column W is empty in all 104 rows. |
| `Mode of Proc.` never filled | Column N is empty in all 104 rows. |
| `Item No.` mostly abandoned | Populated in 11 of 104 rows, non-contiguously. |
| Free-text categories drift | `Meals and Snacks` vs `Meals and nacks`; `Pen` / `pen` / `ballpen`; `Paper` / `paper`. The config table's `category` fixes this by construction. |
| Stock Card No. partially adopted | Only **22 of 104** rows carry one (§6). |
| Duplicated ref code | `1000-000-1-01-010-002-002-003` appears twice, on two differently-named activity rows. |

---

## 6. Stock Card No. — shipped this round

Added `price_index_items.stock_card_no` (migration `AddPriceIndexItemStockCardNo`) so the report's
Column H can be populated from config rather than retyped per plan.

- **Optional and not unique** — only a minority of catalogue items have one, and enforcing
  uniqueness would fail the CSV import that is this table's primary ingestion path.
- **Editable in the config UI**, shown as its own sortable column, and **searchable** — typing a
  code prefix (e.g. `OSE-`) filters the catalogue to matching items.
- **CSV round-trips**, with `stock_card_no` appended **last** so a file exported before the column
  existed still imports cleanly. A CSV *without* the column leaves existing values untouched; a row
  *with* the column but blank clears it (matching how `category` behaves). Both cases are covered
  by tests.

### Which code goes in the field — DECIDED: the GSO Item Code

There are **two different coding schemes** in play, and this caused a false start:

- The **PPMP working file's "Stock Card No."** column uses codes like `OS-PAP-0000004` (22 of them
  in the Admin Division file). The first seed used these.
- GSO's authoritative **Items Export ("Item Code")** uses a different scheme entirely —
  `OSE-1714343441`, `OSAMFD-3025181986`, `TREX-…`, etc. **Zero `OS-…` codes exist in it.**

**Decision (Ralph, 2026-07-24): `stock_card_no` holds the GSO Items-Export Item Code**, not the
`OS-` code hand-typed on the PPMP working file. The 22 `OS-` codes from the first seed were
overwritten. (So the PPMP form's "Stock Card No." column, when the report renders it, will show the
GSO Item Code — the two labels refer to the same underlying identifier; the `OS-` values on the
paper form were a local shorthand, not the system code.)

**Merge method (reproducible):** match each price-index row to the Items Export on
**`name` + `unit` + `category`**, where the export's **Account Name** is the category. Falling back
to `name` + `unit` alone. Only rows with **exactly one** candidate code are filled; anything
ambiguous or unmatched is left blank rather than guessed.

Result over the full 6,398-row price index (imported via the standard CSV endpoint —
*6,099 updated, 299 skipped, 0 errors*):

| Outcome | Count | Handling |
|---|---|---|
| Unambiguous code (name+unit+category) | 6,097 | filled |
| Unambiguous code (name+unit only) | 2 | filled |
| **Ambiguous** — same name+unit+category → multiple codes | 177 | left blank |
| **No match** in the export | 122 | left blank |

The `name`+`unit` key alone was ambiguous for 2,340 rows (the same item is coded differently per
account — e.g. "Battery AA" is `OSAME-…` as an expense, `OSAMFD-…` for distribution, `TREX-…` under
training); adding `category`=Account Name resolved all but 177 of those. The 299 blanks can be
filled manually in config, or by a later, smarter match. **Do not auto-fill them by loosening the
match** — a wrong GSO code on an item is worse than a blank.

The 22-row `OS-` seed file was removed once this superseded it. Re-seeding is done by re-running the
export→match→import above against a fresh Items Export, not from a committed file.

### How the code reaches the report — DECIDED: join live

`WfpProcurementItem` snapshots `Name`/`Unit`/`UnitPrice` at save time but **not** `Category`, and
now not `StockCardNo` either. So the report either joins live via `PriceIndexItemId` or snapshots
these too.

**Decision (Ralph, 2026-07-24): join live via `PriceIndexItemId`.** No schema change on
`WfpProcurementItem`.

The reasoning, worth keeping because it looks inconsistent with the snapshot rule next to it: a
stock card number is an **identifier**, not a value. If GSO corrects it, or someone fixes a typo in
config, every report should immediately show the corrected code — including for WFPs saved last
year. A unit *price* is the opposite: it must never drift retroactively, because a saved WFP is a
budget commitment at the price that was current when it was made. Same table, opposite requirements,
hence the deliberate asymmetry. `Category` follows the same logic as the stock card number.

Consequence to handle in the report: a **free-typed** procurement item has `PriceIndexItemId = null`
and therefore no stock card number and no category — those cells render blank. Currently **64 of 72**
local items are linked; 8 are free-typed. That is expected, not a bug: an item typed by hand has no
GSO code by definition. Do NOT fall back to fuzzy-matching on name to fill it in.

---

## 7. Open questions

**All resolved as of 2026-07-27** — the design is locked; see §8 for the resulting build spec.

Answered by the reference file: ~~Q1~~ (which form), ~~Q2~~ (row grain), ~~Q9~~ (export fidelity —
match the province's own form), ~~Q10~~ (filename — no PPMP No.; the real form has no such field).

Answered by Ralph 2026-07-24: ~~Q14~~ (moot — the `OS-` seed it belonged to was superseded by the
GSO Item Code merge), ~~Q15~~ (join live via `PriceIndexItemId`), ~~Q16~~ (`stock_card_no` holds the
GSO Items-Export Item Code). All in §6.

Answered by Ralph 2026-07-27 (against the re-supplied `ppmp admin2027.xlsx`):

| # | Question | Decision |
|---|---|---|
| **Q3** | End-user unit = Office or Division? | **Both.** Division-scoped users print their own division's PPMP; an **Allocation user (`CanManageAllocation`) or SuperAdmin** can also print the **office-level** (all divisions consolidated) PPMP. Mirrors the WFP report's RAL-136 scoping. *(Note: WFP's `canBypassDivision` also allows plain **Admin** — confirm whether Admin should get office-level here too; the ticket assumes yes, to match WFP.)* |
| **Q4** | One PPMP file per fund source? | **No.** Copy the WFP report's grouping: **one report, a separate table per fund source** stacked within it (reuse the `fundSourceReports` shape). Not one download per fund. |
| **Q5** | Signatories derivable or free text? | Follow the reference file's block ("Prepared by" / "NOTED BY"). Not a blocker for the preview; treat as static/free-text labels for now. |
| **Q8** | Include non-procurement expenditures? | **Procurement only.** Base the report on `WfpProcurementItem` rows. Exclude `Nature = "Non-Procurement"` expenditures entirely; a `"Combined"` expenditure contributes its procurement items only (its typed-period non-proc portion is omitted). |
| **Q11** | Account section-row total = AIP appropriation or sum of items? | **Sum of the items below it.** Computed from the procurement item line totals, never fetched from AIP. |
| **Q12** | Mode of Procurement column? | **No.** Drop column N entirely — no new field, no config list. |
| **Q13** | Generate the Summary sheet? | **No.** The AIP-vs-actual reconciliation sheet is out of scope. |

---

## 8. Locked build spec (2026-07-27)

Follow the province's own `ppmp admin2027.xlsx` layout, with the decisions above applied. Build like
the WFP report did: **preview first** (RAL-132 pattern), **`.xlsx` export second** (v1.4.4 pattern).

### Columns (Mode of Procurement dropped per Q12)

| # | Column | Source |
|---|---|---|
| 1 | Item No. | derived row counter (blank is acceptable — mostly blank in the reference) |
| 2 | AIP Reference Code | `aip_programs`/`aip_projects`/`aip_activities` `.ref_code` (on section rows) |
| 3 | Description | hierarchy/account name on section rows; `wfp_procurement_items.name` on item rows |
| 4 | Stock Card No. | `price_index_items.stock_card_no` via `WfpProcurementItem.PriceIndexItemId` (**live join**, Q15) — blank for free-typed items |
| 5 | Short Category | `price_index_items.category` (same live join) |
| 6 | Unit | `wfp_procurement_items.unit` |
| 7 | Unit price | `wfp_procurement_items.unit_price` |
| 8 | QTY | `wfp_procurement_items.qty` |
| 9 | Est. Budget | `wfp_procurement_items.line_total` |
| 10–17 | 1ST / 1ST QRTR AMOUNT … 4TH / 4TH QRTR AMOUNT | `period_no` (mapped to quarter) + qty + line_total |
| 18 | TOTAL | computed |

### Structure & rules

- **Row grain (Q2):** one row per `WfpProcurementItem`. Hierarchy (program → project → activity →
  account) renders as interleaved section rows; the section rule is "no unit price = section row".
- **Procurement only (Q8):** source only `WfpProcurementItem` rows. Skip `Nature = "Non-Procurement"`
  expenditures; a `"Combined"` expenditure contributes its procurement items only.
- **Account section total (Q11):** SUM of the item line totals beneath it. Computed.
- **Fund-source grouping (Q4):** a separate table per fund source within one report — reuse the WFP
  report's `fundSourceReports` shape and per-fund block rendering.
- **Scope (Q3):** division-scoped callers forced to their own division (RAL-136); `CanManageAllocation`
  or `SuperAdmin` (and Admin — see Q3 note) may pick a division OR office-consolidated (all divisions).
- **No Mode of Procurement (Q12); no Summary sheet (Q13).**

### Build order / tickets

1. **Preview** — `PpmpReportService` + `GET /api/budget-planning/ppmp/report/preview`
   (`{ data, error, message }`; slim DTO; server-side aggregation; **no `GetAllAsync()`-then-filter**,
   per `docs/PERFORMANCE_GUIDELINES.md`). Frontend: add `PPMP` to `REPORT_TYPES` in
   [report/page.tsx](frontend/src/app/(portal)/budget-planning/report/page.tsx) (WFP stays default);
   render the item-grained grid with per-fund tables, reusing the WFP report's scoping controls.
2. **`.xlsx` export** — `IPpmpReportExcelService` (named distinctly, as `IWfpReportExcelService` was) +
   `GET /api/budget-planning/ppmp/report/export`; programmatic ClosedXML build from a style catalog,
   **not** row-cloning from a reference workbook (the province's files are filled samples, not
   templates); client-side filename. Structural unit tests (row counts, section totals, no formulas).

---

## Appendix A — the official GPPB form (RA 12009)

Superseded for *what we build first*, but still the national standard, and the province may have to
migrate to it. Procurement moved from RA 9184 to **RA 12009** (New Government Procurement Act), IRR
via **GPPB Resolution No. 02-2025**; PPMP requirements now sit in **IRR §7.7**.

The official form is **project**-grained (not item-grained) with 12 numbered columns under three
group headings — PROCUREMENT PROJECT DETAILS (1–5), PROJECTED TIMELINE MM/YYYY (6–8), FUNDING
DETAILS (9–10), then Attached Supporting Document/s (11) and Remarks (12) — plus an Indicative/Final
checkbox, a PPMP number, a TOTAL BUDGET row, and Prepared by / Submitted by signatories. It also
mandates a **Market Scoping Checklist** as a supporting document for every project.

A worked sample against this form was built from local dev data on 2026-07-23, before the real
reference file arrived. It is **not committed** — this is a public repository and the workbook
carried real, still-unapproved FY2027 budget figures. Regenerate it locally if the GPPB layout ever
becomes relevant.

**If the province ever has to file the GPPB form**, most of the work carries over: the same
`WfpProcurementItem` data, aggregated up to the expenditure grain instead of listed per item.

### References

- GPPB — official NGPA PPMP form: <https://www.gppb.gov.ph/wp-content/uploads/2025/08/NGPA_PPMP.pdf>
- RA No. 12009: <https://ps-philgeps.gov.ph/home/images/legalbases/2025/New%20Government%20Procurement%20Act%20_%20Republic%20Act%20No.%2012009.pdf>
- IRR of RA 12009 (GPPB Res. 02-2025): <https://www.dbm.gov.ph/wp-content/uploads/Issuances/2025/GPPB-Resolution/IRR-RA-12009-Resolution-No-02-2025.pdf>
- GPPB-TSO — Fit-for-Purpose Procurement Modalities: <https://www.gppb.gov.ph/fit-for-purpose-procurement-modalities-under-ra-12009/>

### Internal

- `frontend/src/app/(portal)/budget-planning/report/page.tsx` — the page PPMP plugs into
- `backend/PPDO.Domain/Entities/WfpProcurementItem.cs` — the line items PPMP is built from
- `backend/PPDO.Domain/Entities/PriceIndexItem.cs` — now carries `StockCardNo`
- `docs/PERFORMANCE_GUIDELINES.md` — query rules for the new endpoint
- `docs/v1.4.4/WFP_Excel_Export_Assessment.md` — the export approach to reuse
- `docs/TICKET_PROMPT_STANDARD.md` — structure for the tickets once Q3–Q15 are answered
