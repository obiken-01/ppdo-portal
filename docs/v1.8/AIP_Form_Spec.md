# AIP Printable Form — Specification (V18-60)

> The document this system must produce for the **PDC** and then the **Sangguniang Panlalawigan**,
> under a **June 7** statutory deadline. Not an internal report — see `Phase_Plan.md` §12.6.
>
> **Two sources of truth, and they are different things:**
> - **DBM Budget Operations Manual for LGUs, 2023 Ed. (2024 reprint)** — Annex B is the prescribed
>   form; Figure 4 + Annexes C/D are the reference-code rules. This is what the province is
>   *required* to produce.
> - **`D:\RalphFiles\PPDO\AIP_2027_PGOM_Test.xlsm`** — the province's real FY2027 AIP. This is what
>   the province *actually* produces, deviations included. Ralph, 2026-08-26: "that report will look
>   similar to the attached excel."
>
> Where they differ, this spec follows the province's file for **layout** and the v1.8.0 decisions
> for **content**. Every deviation is called out rather than silently copied.

---

## 1. Workbook structure

Four sheets, one per sector, named `<SECTOR>_FY<year>`:

| Sheet | FY2027 rows | Sector code |
|---|---|---|
| `GENERAL_FY2027` | 810 | `1000` General Public Services |
| `SOCIAL_FY2027` | 722 | `3000` Social Services |
| `ECONOMIC_FY2027` | 1160 | `8000` Economic Services |
| `OTHERS_FY2027` | 343 | `9000` Other Services |

**Page setup — identical on all four:** landscape · print titles `$8:$9` (the two header rows repeat
on every page) · print area `$A$1:$R$<total row>`.

---

## 2. Preamble (rows 1–7)

| Cell | Merge | Content |
|---|---|---|
| `A1` | `A1:R1` | `Annex B` |
| `A2` | `A2:R2` | `ANNUAL INVESTMENT PROGRAM (AIP) FY <year>` |
| `A3` | `A3:R3` | `By Program/Project/Activity by Sector` |
| `A4` | `A4:R4` | `As of <MONTH YEAR>` |
| `A6` | — | `Province/City/Municipality/Barangay: OCCIDENTAL MINDORO` |

Row 5 and row 7 are blank spacers.

---

## 3. Column map (A–R) — two-tier header, rows 8–9

Bracketed numbers are the **DBM column numbers printed on the form itself**.

| Col | Merge | Header | DBM # |
|---|---|---|---|
| `A` | `A8:A9` | AIP Reference Code | **(1)** |
| `B`–`E` | `B8:E9` | Program/Project/Activity Description | **(2)** |
| `F` | `F8:F9` | **eSRE Code** — encoder-supplied, see §3.1 | **— none** |
| `G` | `G8:G9` | Implementing Office/Department | **(3)** |
| `H` | `H8:I8` → `H9` | Schedule of Implementation → Start Date | **(4)** |
| `I` | `H8:I8` → `I9` | Schedule of Implementation → Completion Date | **(5)** |
| `J` | `J8:J9` | Expected Outputs | **(6)** |
| `K` | `K8:K9` | Funding Source | **(7)** |
| `L` | `L8:O8` → `L9` | AMOUNT → Personal Services (PS) | **(8)** |
| `M` | `L8:O8` → `M9` | AMOUNT → Maintenance and Other Operating Expenses (MOOE) | **(9)** |
| `N` | `L8:O8` → `N9` | AMOUNT → Capital Outlay (CO) | **(10)** |
| `O` | `L8:O8` → `O9` | AMOUNT → **Total** — labelled `8+9+10` on the form | **(11)** |
| `P` | `P8:R8` → `P9` | AMOUNT of Climate Change expenditure → CC Adaptation | **(12)** |
| `Q` | `P8:R8` → `Q9` | AMOUNT of Climate Change expenditure → CC Mitigation | **(13)** |
| `R` | `P8:R8` → `R9` | AMOUNT of Climate Change expenditure → CC Typology Code | **(14)** |

⚠️ **Column F is a provincial insertion, not part of Annex B.** It carries no DBM number and sits
between (2) and (3), which is why the printed numbering jumps `(2) … (3)` across two columns. It
stays on the form; the only consequence of it not being an Annex B column is that it must not be
given a DBM number in the header.

### 3.1 Column F — eSRE Code

**✅ Confirmed 2026-08-26 (Ralph): "keep eSRE Code, this will be part of what they enter or
select."** It is an encoder-supplied field, not a derived one, and it is already required at submit
(V18-49's completeness checklist) and cached for offline use (V18-65).

The FY2027 file uses **four** codes across 2,357 filled rows:

| Code | Rows |
|---|---|
| `SS` | 1,069 |
| `ID` | 669 |
| `ES` | 537 |
| `EN` | 81 |

… and **one** row reading `PPDO/PEO` — an implementing-office name typed into the eSRE column. One
bad value in 2,357 is a low error rate, but it is the exact error a pick-list makes impossible, and
it is the evidence behind **V18-10** (`esre_codes` config table + page, replacing today's free-text
`AipActivity.EsreCode`). "Enter **or select**" is satisfied by V18-10; nothing further is needed on
the entry side.

The form itself prints whatever code the activity carries — no transformation.

---

## 4. Hierarchy — the description column IS the level

Columns `B`–`E` are merged in the header but **written individually** in the body. Which of the four
holds the text is what defines the row's level:

| Column | Level | FY2027 GENERAL count |
|---|---|---|
| `B` | Office / Department | 11 |
| `C` | Program | 72 |
| `D` | Project | 151 |
| `E` | Activity | 559 |

⚠️ **Do not derive level from the reference code.** 82 of 2,887 rows in the province's file disagree
with their own code depth — SOCIAL carries 7-segment codes on activity rows, ECONOMIC carries
9-segment ones. This was RAL-238's defect and it is fixed in the importer; the generator must not
reintroduce it from the other direction. The code is *rendered from* the tree (`Phase_Plan.md`
§12.5), and the description column is *written from* the same tree — they agree by construction, not
by inference.

---

## 5. Row types

| Type | `A` | Description col | Amounts | Notes |
|---|---|---|---|---|
| **Office / sub-office group** | office-level code, 5 segments — **shared across the groups** | `B` | subtotal | One office may head **several** such rows under one code, distinguished by name: `OFFICE OF THE GOVERNOR - WARDEN` / `- AKAP-HUB` / `- HOUSING` are three separate rows, all `3000-000-1-01-001`. Group identity is `(Sector, Name)`. **Encoder-created** — see `Phase_Plan.md` §12.6a |
| **Program** | + program segment | `C` | usually blank | |
| **Project** | + project segment | `D` | usually blank | |
| **Activity** | + activity segment | `E` | the money lines | |
| **TOTAL** | literal `TOTAL` in `A` | — | grand total | one per sheet, last row of the print area |

**Totals are built upward, and every level is a `SUM` of the level below it** — row total
`O = SUM(L:N)`, office subtotal = `SUM` of its own lines, sheet `TOTAL` = `SUM` of the office rows.
That is the province's own construction and it is what makes DECISION 9 (round first, then add)
consistent with the printed document.

---

## 6. What changes for FY2028+

These are the v1.8.0 decisions as they land on this form. Each is a deliberate departure from the
FY2027 file.

| # | Change | Decision |
|---|---|---|
| 1 | **Round every figure UP to the thousand, then sum the rounded figures** — across and down. The FY2027 file does not round at all: it carries thousands to two decimals (`8798.65` = ₱8,798,650) | DECISION 9 / A2-1 / A3 |
| 2 | **One fund source per line** in column `K`. 60% of FY2027 money lines name several funds against one un-split amount (`20% DF/NGAs` ×591), which makes a General-Fund ceiling uncomputable | Settled 2026-08-14 |
| 3 | **`K` becomes a pick-list, not free text** — the FY2027 file has 15 spellings of the General Fund | — |
| 4 | **+30% uplift on MOOE and CO**, FY2028+ only, applied at render time from a stored base | DECISION G |
| 5 | **State the units in the `L8:O8` AMOUNT header.** The FY2027 form declares "(In Thousand Pesos)" over the *climate-change* block `P8:R8` and says **nothing** over the main AMOUNT block — the one place the ₱000-vs-pesos ambiguity actually lives | DECISION E |

### ✅ 6.1 The uplift shows in `M` and `N`, not only in `O`

**Answered 2026-08-26 (tracker G7).** Ralph raised this scenario with PPDC directly, and **`M` and
`N` uplifted is what will be followed** — *"as long as the entered value (in round up) follows the
ceiling."*

So the row keeps the structure it has always had:

> `O = SUM(L:N)` — and, because `M` and `N` carry the uplift, `O` also equals
> `PS + 1.3 × (MOOE + CO)`, satisfying tracker G6 at the same time.

The rejected alternative — base `M`/`N` with only `O` uplifted — would have made `L + M + N ≠ O` on
every row carrying MOOE or CO, with column `O` contradicting its own printed label `8+9+10` in a
document read by elected officials.

✅ **The qualifier corroborates two earlier decisions at once.** "The entered value (in round up)
follows the ceiling" means the ceiling is compared against the **entered figures, rounded up** —
i.e. **base, rounded**. That is exactly tracker **G3** (the uplift is not in the ceiling check) and
tracker **A2-4** (the ceiling compares the rounded figures) holding together. Two numbers exist by
design and neither is wrong:

| Number | Rounded from | Used for |
|---|---|---|
| **Base MOOE + CO** | the entered values | the **ceiling check** |
| **Uplifted MOOE + CO** | base × 1.3 | the **printed columns `M` / `N`**, and `O` |

⚠️ **They are not exactly 1.3× each other once rounded, and nothing should assume they are.**
Before rounding the ratio is exactly 1.3; after rounding it is not, and by how much depends on
**tracker G5**, which is still open — uplift-then-round or round-then-uplift. Worked example:
₱1,000,400 base → the ceiling sees `1,001`; the form shows `1,301` under uplift-then-round, or
`1,302` under round-then-uplift. **Do not write a test or a reconciliation that asserts
`printed = 1.3 × checked`.**

### ⚠️ 6.2 The ceiling exceedance is visible — and still needs explaining

With `M` and `N` uplifted (§6.1), an office encoding exactly to a ₱10,000,000 ceiling passes every
check in the system and prints an AIP whose MOOE + CO reads about ₱13,000,000. **That is intended**,
and §6.1 is the reason it is now visible **in the money columns** rather than buried inside a Total
that does not reconcile — which is the better of the two failure modes, but it is still a figure
that reads as an overspend.

A PDC member or board member has no way to know the excess is deliberate. **The form, or a covering
note printed with it, must say so** — this is the room where it would otherwise be raised as an
error. Wording to be agreed with PPDC; see `Phase_Plan.md` §12.2.

ℹ️ **The uplift and the ceiling govern exactly the same figures**, which is why this is sharper than
a general rounding caveat: the uplift applies to MOOE and CO (PS prints as entered), and the ceiling
applies to MOOE and CO (PS is exempt as an expense class, tracker A6-2). There is no dilution — the
gap between the printed figure and the checked figure *is* the ceiling gap.

---

## 7. Defects in the FY2027 file — do not reproduce

Found while parsing it; all are artefacts of hand-maintained spreadsheets and disappear when the
document is generated.

- **The grand total is ₱30,668,000 below its own line items** — a number of rows are missing their
  column `O` formula. Worth telling PBO plainly; it is their current published figure.
- **Rows with a blank or literal `"None"` reference code carrying real money** — 4 rows, ₱29,350,000,
  which the old importer folded into the previous activity's name and discarded.
- **Stray content outside the print area** — `M810` = `"20 pesos"`, `N810` = `"15k"` on the GENERAL
  sheet: someone's scratch note, shipped with the file.
- **Print areas that do not match the content** — `OTHERS` ends its print area at row 292 with content
  to row 343; `ECONOMIC`'s print area runs one row past its last row.
- **Free-text columns are inconsistent** — `"Januray"` ×109 in the schedule columns; 543 of 2,372 rows
  name more than one implementing office in `G`. Supports pick-lists in the redesign.
- **Climate-change columns `P`/`Q`/`R` are filled on 5 rows out of 2,887.** Keep the columns (Annex B
  requires them) but ask PPDC whether the province intends to populate them.

---

## 8. Build notes

- **Generate programmatically from a style catalogue**, not by templating a copy of the file — the
  v1.4.4 WFP export lesson. See `Phase_Plan.md` V18-60 and the `WfpReportExcelService` design.
- The importer (`AipXlsmParser`, RAL-238) reads this same layout. The generator and the parser
  should agree on the column map by sharing one definition rather than each carrying its own.
- FY≤2027 records keep the old shape and are **not** re-rendered under these rules
  (`AIP_Redesign_Notes.md` §4a/§4b).

---

# Part II — The export (V18-60 / PPDO-84) — build spec

> §1–§8 describe the **form**. §9 onward is the **build**: who downloads it, what goes in, how each
> cell is written, and how it is tested. Settled with Ralph on 2026-09-14. Epic **PPDO-83**
> (Phase 5 — Outputs), where this file comes **first**, ahead of the external AIP endpoints.

## 9. Goal

A cross-office reviewer downloads the FY2028+ AIP as the province's Annex B workbook — four sector
sheets, columns A–R — from the Consolidated AIP page, and it matches that page to the peso. The page
(PPDO-73) is the preview of this file.

## 10. Decisions (settled 2026-09-14)

1. **Same offices as the Consolidated AIP grid** — groups at `SubmittedToPpdo` or `Consolidated`. A
   sent-back office is out until re-submitted. While any office is missing, the preamble says how many
   are in (§13.1). The file and the grid can never disagree about which offices they cover.
2. **Cross-office reviewers only** — `CanReviewAllOffices` (SuperAdmin resolves true), the same gate
   as the grid. **One button, on the Consolidated AIP page.** No per-office download.
3. **One workbook, four sheets, always** — `GENERAL_FY<year>`, `SOCIAL_FY<year>`, `ECONOMIC_FY<year>`,
   `OTHERS_FY<year>`, whichever sector the page has open. A sector with no office yet still gets its
   sheet, with a TOTAL of zero, so the workbook's shape never depends on the season.
4. **Totals are real `SUM` formulas** — the province's own construction (§5): each activity's `O` is
   `=SUM(L:N)` of its row, each office row sums its own block, the sheet TOTAL sums the office rows.
   Activity `L`/`M`/`N`/`P`/`Q` hold **values**. Tests pin every formula's result to the system's
   figure, so a formula cannot quietly disagree with the grid.
5. **Printed figures only, from `AipFormRowBuilder` / `AipPrintedFigures`** — never re-derived in the
   writer. Round up per figure, +30% on MOOE and CO, uplift then round (§6, tracker G5 provisional).
   Which figure every screen, API field and file shows — full, rounded, or rounded with +30% — is
   tabulated in [AIP_Amount_States.md](AIP_Amount_States.md).
6. **Amounts in thousands, two decimals** — ₱1,301,000 is written `1301` and shows `1,301.00`, as the
   grid shows it. The AMOUNT header says `(In Thousand Pesos)` (§6 #5).
7. **The over-ceiling note ships with draft wording** (§6.2), held in **one constant** so PPDC's
   wording is a one-line change.
8. **FY2028 onward only.** FY≤2027 is not re-rendered under these rules (§8) — the request is refused.
9. **File name** `AIP_FY<year>_<yyyy-MM-dd>.xlsx`, Manila date. **"As of"** is the download month in
   Manila, upper case (`As of SEPTEMBER 2026`), matching the province's file.

### Open follow-ups (not blocking)

- **Tracker G5** — uplift-then-round vs round-then-uplift. One line in `AipPrintedFigures.ForActivity`.
- **The note's wording** — to be agreed with PPDC.
- **Climate-change columns** are near-empty in FY2027 (§7). Written as entered; ask PPDC whether they
  will be used.

## 11. Behaviour

| Case | Given | When | Then |
|---|---|---|---|
| Happy path | FY2028 open; 3 offices at PPDO or accepted across two sectors | Reviewer presses **Download Excel** | `AIP_FY2028_<date>.xlsx` with four sheets; each office on its sector's sheet with its programs, projects and activities; every figure equal to the grid's |
| Matches the grid | Any sector | Compare the grid with its sheet | Same rows in the same order, same printed figures, same TOTAL |
| Partial season | 3 of 19 offices submitted | Download | Preamble row 5 reads `Includes 3 of 19 offices — offices still being prepared or reviewed are not in these totals.` |
| Complete season | Every office submitted or accepted | Download | Row 5 is the blank spacer the province's file has — no completeness line |
| Sent-back office | An office returned by PPDO | Download | Not in the workbook, as not in the grid |
| Empty sector | No office with PPDO in OTHERS | Download | `OTHERS_FY2028` exists with the preamble, header and `TOTAL` of `-` |
| No office at PPDO yet | Opened year, none submitted | Page loads | The button is **disabled** — "No office has reached PPDO yet." The API still answers with a valid workbook of four empty sheets |
| Year not opened | FY with no AIP record | Page loads / API called | Button disabled; API **404** `FY <year> has not been opened.` |
| FY2027 | Legacy year | API called directly | **400** `FY 2027 and earlier are not rendered as the Annex B export.` (the page only offers FY2028+) |
| A program with nothing under it | LDIP-seeded program, no project or activity | Download | Left off the sheet — the builder's rule, same as the grid |
| An office with nothing under it | An office group with PPDO whose programs are all empty, or that has none | Download | Left off the sheet and the grid (2026-09-14); its office still counts in `N of M` |
| Synthetic project | A project created only to hold a program-level line | Download | No row for it; its activities sit under the program |
| Edited in Excel | PPDO changes one activity's MOOE cell before printing | Excel recalculates | The row Total, the office subtotal and the sheet TOTAL follow — the reason for formulas |
| Two downloads a minute apart | An office is accepted in between | Download twice | The second file includes it. Nothing is cached |

### 11.1 Per role

| Account | When | Then |
|---|---|---|
| Cross-office reviewer (`admin`, `ppdo.finance`, `pdc.user`) | Download | ✅ |
| SuperAdmin | Download | ✅ |
| Department head (`CanReviewBudgetPlanning` only) | Calls the endpoint | **403** — they cannot open the Consolidated AIP page either |
| PPDO division user, budget officer, office encoder | Calls the endpoint | **403** |
| No token | Calls the endpoint | **401** |

## 12. API contract

### `GET /api/budget-planning/aip/consolidated/export?fiscalYear=` — JWT + `CanReviewAllOffices`

**200** — `Content-Type: application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`,
`Content-Disposition: attachment; filename="AIP_FY2028_2026-09-14.xlsx"`.

| Status | When | Body |
|---|---|---|
| 400 | `fiscalYear` missing or not a number | `ApiResponse` — `fiscalYear is required.` |
| 400 | `fiscalYear` < 2028 | `FY 2027 and earlier are not rendered as the Annex B export.` |
| 401 | No or invalid token | — |
| 403 | No cross-office grant | Empty — refused at the endpoint's gate, as the grid read is. The service checks the flag again and says `Only a cross-office reviewer can download the consolidated AIP.` |
| 404 | No AIP record for the year | `FY <year> has not been opened.` |

⚠️ **Route ordering:** `consolidated/export` is a literal segment under `consolidated`, which is
already a literal — no collision with `budget-planning/aip/{aipId:int}`.

⚠️ **One tree load for all four sheets**, not four calls to the sheet read: groups, then programs,
projects, activities and fund codes once, awaited in sequence (one `DbContext`), then the builder once
per sector. The FY2027 file ran to ~3,000 rows; that is a workbook ClosedXML writes synchronously in
well under the function timeout. Log the build duration at `Information` (`PPDO-45`).

## 13. Sheet construction

Column indices, the header text and the row styles live in **one column map** in the writer. The
importer (`AipXlsmParser`) reads the same columns; sharing the map is a follow-up, not a blocker (§8).

### 13.1 Preamble — rows 1–7

As §2, from the province's file: `A1` `Annex B` (right-aligned) · `A2` `ANNUAL INVESTMENT PROGRAM (AIP) FY <year>` ·
`A3` `By Program/Project/Activity by Sector` · `A4` `As of <MONTH YEAR>` — each merged across `A:R`,
bold, centred · **`A5`** the completeness line (§11) when incomplete, italic, merged `A5:R5`; otherwise
a 6-pt spacer · `A6` `Province/City/Municipality/Barangay: OCCIDENTAL MINDORO` bold · row 7 a 6-pt spacer.

### 13.2 Header — rows 8–9

The two-tier header of §3, bold, centred, wrapped, thin borders, with the DBM numbers printed in the
labels — **except column F**, which carries none. `L8:O8` reads `AMOUNT (In Thousand Pesos)`; `P8:R8`
`AMOUNT of Climate Change Expenditure (In Thousand Pesos)`.

### 13.3 Body — from row 10

| Row | `A` | Text | Style | Amounts |
|---|---|---|---|---|
| Office | group ref code | `B`, the group name | bold, light fill | `L`–`Q` = `=SUM(<col><first>:<col><last>)` over the office's own block |
| Program | ref code | `C`, merged `C:E` | bold | blank |
| Project | ref code | `D`, merged `D:E` | bold italic | blank |
| Activity | ref code | `E` | regular, wrapped | `L` `M` `N` `P` `Q` values; `O` = `=SUM(L<r>:N<r>)`; `F` eSRE, `G` implementing office, `H`/`I` schedule as entered, `J` expected outputs, `K` fund codes joined `/`, `R` CC typology |
| TOTAL | `TOTAL` | — | bold, top border | `L`–`Q` = `=SUM(` every office row's cell `)` |

- **Office blocks never overlap**, so an office `SUM` over its range counts only its own activities —
  headings in the range are blank, not zero.
- **Zero shows as `-`** — accounting format `_(* #,##0.00_);_(* \(#,##0.00\);_(* "-"??_);_(@_)`, the
  province's own.
- Font **Arial Narrow 10** (preamble and TOTAL 11), top-aligned, text wrapped in `A`, `E`, `G`, `J`.
- Column widths from the province's file: `A` 12.4 · `B` 1.6 · `C` 2.6 · `D` 4.2 · `E` 38 · `F` 9.9 ·
  `G` 11.2 · `H`/`I` 10.2 · `J` 18.2 · `K` 9.8 · `L`–`O` 13.6 · `P`/`Q` 9.9 · `R` 8.8.

### 13.4 The note — below TOTAL

Two rows under `TOTAL`, merged `A:R`, italic, wrapped, **inside the print area** so it prints with the
form. Draft wording (PPDC to agree), from one constant:

> Note: Maintenance and Other Operating Expenses (MOOE) and Capital Outlay (CO) include a 30%
> adjustment. Budget ceilings are checked against the amounts before the adjustment, so an office's
> printed MOOE and CO may exceed its ceiling by up to 30%. Amounts are rounded up to the nearest
> thousand pesos, and every total is the sum of the rounded figures.

### 13.5 Page setup — every sheet

Landscape · **fit to one page wide**, pages tall as needed · margins left/right 0.04″, top/bottom
0.28″ · print titles `$8:$9` · print area `$A$1:$R$<note row>`. Paper size is left to the printer:
the province's file carries a custom size that is not portable.

## 14. UI states — Consolidated AIP page

A **Download Excel** button in the page header, beside the fiscal-year picker. It downloads **all four
sectors**, whichever tab is open — the label's title says so.

| State | Content |
|---|---|
| **Ready** | `Download Excel` — secondary button (white, slate border), download icon |
| **Preparing** | Disabled, `Preparing…`; the grid stays usable |
| **Disabled — nothing to export** | Year not opened, or `submittedOffices` is 0: disabled with the reason as its title |
| **Error** | The page's existing error line: `The Excel file could not be prepared.` (or the API's message); the button returns to Ready |
| **Loading the page** | The button renders disabled with the header, so nothing shifts when the sheet lands |

## 15. Non-goals

- **FY≤2027 export** under these rules (§8).
- **A per-office download**, for the department head or anyone else.
- **PDF**, and **signature blocks** — the province's file has none after TOTAL.
- **Re-importing the export** — FY2028+ upload is frozen (V18-38); the shared column map is hygiene,
  not a round-trip feature.
- **The external AIP API** and API-key pages — the next items in PPDO-83, separately specified.
- **V18-61 Project Profile, V18-62 canonical dataset, V18-63 office files.**

## 16. Deployment notes

No migration, no new package (ClosedXML 0.104.2 is already in `PPDO.Infrastructure`), no new setting,
no CORS change.

## 17. Ticket split

One ticket, **PPDO-84**, under **PPDO-83** — small enough to review whole: service + writer + endpoint
+ button + tests.

## 18. Acceptance checklist

```
- [ ] A cross-office reviewer downloads FY2028 from the Consolidated AIP page and gets
      AIP_FY2028_<today>.xlsx with GENERAL, SOCIAL, ECONOMIC and OTHERS sheets
- [ ] Every sheet's rows, order and figures match the Consolidated AIP grid for that sector,
      including TOTAL
- [ ] An activity with ₱1,000,400 MOOE shows 1,301.00 in column M
- [ ] Changing one activity's MOOE cell in Excel updates that row's Total, its office subtotal and
      the sheet TOTAL
- [ ] With 3 of 19 offices submitted, row 5 says so; with every office in, row 5 is blank
- [ ] A sent-back office is not in the workbook
- [ ] A sector with no office still has its sheet, showing TOTAL as -
- [ ] The over-ceiling note prints under TOTAL on the last page
- [ ] Print preview: landscape, one page wide, the two header rows repeat on every page
- [ ] The button is disabled before any office reaches PPDO, and for an unopened year
- [ ] A department head calling the endpoint directly gets 403
```

## 19. Test focus

| Class | Cover |
|---|---|
| `AipConsolidatedServiceTests` (export) | Four sheets always, in sector order; only `SubmittedToPpdo` / `Consolidated` groups; the counts for the completeness line; a non-reviewer 403; an unopened year 404; FY2027 400; the tree loaded once, not per sector |
| `AipFormExcelServiceTests` (ClosedXML, read back) | Sheet names; preamble text incl. `As of` and the completeness line present/absent; header merges and labels (no DBM number on F); each row's text in the right description column (B/C/D/E) and its merges; amounts in thousands; **every formula's calculated value equals the builder's figure** — activity `O`, office subtotals, TOTAL; office `SUM` ranges do not overlap; the note below TOTAL and inside the print area; print titles, landscape, fit-to-width |
| Functions integration | 200 with the xlsx content type and file name; 400 / 401 / 403 / 404 |
