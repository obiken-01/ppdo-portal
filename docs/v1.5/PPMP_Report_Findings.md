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
- **Editable in the config UI**, shown as its own sortable column, and **searchable** — typing
  `OS-PEN` filters to the pen items.
- **CSV round-trips**, with `stock_card_no` appended **last** so a file exported before the column
  existed still imports cleanly. A CSV *without* the column leaves existing values untouched; a row
  *with* the column but blank clears it (matching how `category` behaves). Both cases are covered
  by tests.

**Seeded from the reference file**: `docs/v1.5/price_index_stock_card_no_seed.csv` — all 22 stock
card numbers, matched to their catalogue items by normalised name and verified against the live
price index (6,398 items). Imported cleanly.

Two needed judgement:

- `OS-PAP-0000034` "Sticky Notes (Sign Here) **(pack)**" → matched to "Sticky Notes (Sign Here)";
  the plan file had appended the unit to the name. Confident.
- `OS-BAT-0000002` "Battery AAA…" → two candidates: id 2906 `Battery AAA, 1.5Volts, Alkaline …
  (Lubang)` [pc] matched by name, id 4297 `Battery AAA, 1.5Volts, Max Alkaline, …, 4pieces per pack`
  [pack] matched by unit. Left out of the first import and **resolved by Ralph (2026-07-24): id 4297,
  the pack** — now in the seed.

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

Answered by the reference file: ~~Q1~~ (which form), ~~Q2~~ (row grain), ~~Q9~~ (export fidelity —
match the province's own form), ~~Q10~~ (filename — no PPMP No.; the real form has no such field).
Answered by Ralph 2026-07-24: ~~Q14~~ (`OS-BAT-0000002` = id 4297, the pack — seeded), ~~Q15~~
(join live via `PriceIndexItemId`, see §6).

| # | Question | Why it matters |
|---|---|---|
| **Q3** | Is the end-user unit the **Office** or the **Division**? The reference file is one division ("ADMINISTRATIVE DIVISION GEN. FUND") — does each division file its own, or does PPDO consolidate? | Decides whether division is a filter or part of the report's identity. The file suggests per-division. |
| **Q4** | One file **per fund source** — confirmed by "Charged to: GENERAL FUND"? So a division with GF + LDRRM money files two PPMPs? | Drives whether the report loops fund sources like the WFP report does. |
| **Q5** | Signatories — "Prepared by" (Admin. Assist. III) and "NOTED BY" (PPDC). Derivable from `User`/`Division`, or free text? | Small, but it's on the form. |
| **Q8** | Include **non-procurement** expenditures? `WfpExpenditure.Nature` is Procurement / Non-Procurement / Combined. The reference file has account rows with a total but no item rows beneath (e.g. "Travelling Expenses"), suggesting non-procurement lines DO appear as section rows without detail. | Decides the base query's filter. |
| **Q11** | The account section rows carry a total (e.g. ₱700,000 for Office Supplies). Is that the **AIP/WFP appropriation** for that account, or the sum of the items below it? In the reference they don't always agree. | Determines whether that cell is computed or fetched. |
| **Q12** | **Mode of Procurement** — blank in all 104 rows. Do you want the field at all? If yes, seeded config list or free text? | The only genuinely missing column. Deferring it costs nothing. |
| **Q13** | Generate the **Summary sheet** (AIP vs actual budget reconciliation) as a second sheet of the export? The portal already has both sides. | Nice-to-have; could ship free. |
**Resolved:** ~~Q14~~ — `OS-BAT-0000002` is **id 4297** ("Battery AAA … 4pieces per pack", unit
`pack`), not id 2906. Added to the seed and imported (Ralph, 2026-07-24). ~~Q15~~ — join Stock Card
No./Category live via `PriceIndexItemId`, no snapshot (Ralph, 2026-07-24). Rationale and consequences
in §6.

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
