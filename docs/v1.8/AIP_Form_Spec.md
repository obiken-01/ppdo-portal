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
