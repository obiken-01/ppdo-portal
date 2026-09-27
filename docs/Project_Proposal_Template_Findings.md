# PGOM Investment Proposal — Template Findings

> **Status:** Exploration / template study. Not yet specced or scheduled to a release.
> **Date:** 2026-09-27
> **Source:** 5-page printed template "PGOM PROJECT PROPOSAL" (Provincial Government of
> Occidental Mindoro letterhead) with pen annotations, plus one hand-drawn sketch of the
> Section H layout. Sample filled-in proposals are to follow.
> **Output format:** Word (`.docx`) export.
> **Target branch / data model:** `release/1.8.0`, i.e. the redesigned AIP (peso amounts,
> `AipExpenditure` lines, `AipProject.Description/Objective`). All field sources below refer to
> the v1.8.0 entities, not the v1.7 ones on `main`.

---

## 1. What the document is

A **GAD-oriented project proposal** that the province requires **per AIP Project** (settled 2026-09-27). Its
structure follows the PCW **Harmonized Gender and Development Guidelines (HGDG)** project
design format — beneficiaries sex-disaggregated, gender issues in the rationale, work plan,
implementing team, M&E and risks — plus a DBM-style cost section (PS / MOOE / CO) and the
Climate Change Expenditure Typology.

Much of its header and cost data **already exists in the portal** (the v1.8.0 AIP and
config tables). The narrative sections are free text that only the proponent can write. The
annotations draw that line.

### Annotation legend (from the top-right of page 1)

| Mark | Meaning |
|---|---|
| **A** / "auto" | Auto-generated from portal data |
| "Input" | Typed in by the user |
| **\*** (asterisk) | Special handling. Most likely a picklist, see §4 |
| "?" | Undecided |
| Unmarked | No decision written. Treated as **Input** below unless portal data obviously covers it |

### Title change

**Settled 2026-09-27:** the export title is **"PGOM INVESTMENT PROPOSAL"**. The pen note
replaces "PROJECT" with "Investment". This ties the document to the **AIP** (Annual
*Investment* Program), which is where most auto fields come from.

---

## 2. Section-by-section breakdown

Legend for the **Source** column: `AIP` = `AipOffice/AipProgram/AipProject/AipActivity`
plus `AipExpenditure` (v1.8.0), `Cfg` = config tables (`Office`, `FundingSource`, `Account`).
The WFP is **not** needed as a source anywhere.

### A. Project Summary

| Field | Mark | Proposed source | Notes |
|---|---|---|---|
| **Program Title** (new row, added by pen note) | A | parent `AipProgram.Name` | Settled 2026-09-27: printed **above** Project Title so the reader sees which program the project belongs to. Not in the printed template, so the export adds this row |
| **Project Title** | A | `AipProject.Name` | Arrow from "**Pull down / Office**": the user picks the office, then the project (grouped under its program) from a dropdown, and both title rows fill in |
| Project Proponent | A | `AipOffice.Name` / `Office.OfficeName` | The office picked in the dropdown |
| Project Type | A | Unclear. Candidates: `AipOffice.Sector` (General/Social/Economic/Others), `AipActivity.EsreCode` (SS/ES/ID/EN), or "Program / Project / Activity" | **Q3** |
| Project Location | Input? | Not in AIP | Free text, e.g. municipality/barangay list. Could become a municipality multi-select later |
| Implementation Schedule: Start / End | A | `AipActivity.StartDate` / `EndDate` (earliest start, latest end across the project's activities) | AIP stores **month names as strings** ("January"), not dates. Output would be "January 2027 – December 2027" (fiscal year appended) |
| Project Cost | A | Σ `AipActivity.Total` under the project | v1.8.0 stores AIP amounts in **pesos** (`MigrateAipAmountsToPesos`), so there is no ×1000. Should equal the Annex H-1 grand total |
| Attributed GAD Budget | A | **Computed** = Project Cost × HGDG attribution % (see §4) | Only possible once the HGDG score is known |
| Funding Source | A | `AipActivity.FundingSourceSnapshot` → `FundingSource.Name` | A project can mix funds (e.g. "GF/20% DF"), so show the distinct list |
| HGDG Checklist Used | \* | Picklist of HGDG checklists | §4 |
| HGDG Score | (none) | Input (0–20) | Drives Attributed GAD Budget |

**Disaggregated Data of Intended Beneficiaries** (Input): repeating rows of
*Indicator / Male / Female / Total*. **Total = Male + Female should be auto-computed.**

### B. Project Description (marked Input, but prefillable)

v1.8.0 added **`AipProject.Description`** and **`AipProject.Objective`** (PPDO-99, requested at
the 2026-09-15 PDC demo). The entity comment says they are deliberately kept off Annex B and
*"feed a separate report that is not yet specified"*. **This proposal is almost certainly that
report.** So:

- Section B **prefills from `AipProject.Description`** (editable in the proposal).
- Section E *General Goals/Objectives* **prefills from `AipProject.Objective`**.
- Both fields live on the **Project** (level 3), consistent with the settled rule that one
  proposal = one AIP Project.

Rich text, guideline says 3–5 sentences. "Maps can be used (geotagged)", so the section
needs **an optional image attachment**. This is a storage decision (Q8).

### C. Rationale / Background (Input)

A long narrative (pages 1–2). Guidance covers gender gaps, sources of analysis
(SDD, KIIs, FGDs…), alignment with SDG / PDP chapter / RDP / PDPFP, legal bases.
One large rich-text field is simplest. Optional structured sub-fields (SDG picklist,
PDP chapter) are a later nice-to-have (Q9).

> Page 2 repeats the "LGU may also highlight (if applicable)" block twice, word for word.
> This is a typo in the source template; we should not reproduce it.

### D. Project Benefits and Costs (Input)

Fixed 5-row grid: **Social, Economic, Environmental, Institutional, Infrastructure/Land Use**
× *Project Benefit* / *Project Cost*.

⚠️ The **"PROJECT COST" header is highlighted yellow** on the printout. Per the template's
own note, "Costs" here means **negative effects** (temporary/long-term), **not money**. That
is easy to confuse with the peso Project Cost in Section A. The highlight probably flags
exactly that. Treat both columns as free text, and consider relabelling the UI column
"Project Cost (negative effects)" so encoders don't type amounts. (Q10)

### E. Logical Framework (A)

- **General Goals/Objectives**: one text field, prefilled from `AipProject.Objective` (v1.8.0).
- Grid of **Impact / Outcome / Output / Input-Activities** × *Performance Target and/or
  Indicator* / *Means of Verification*. Marked **A**.

What the portal can actually fill:

| Row | Available data |
|---|---|
| Input/Activities | AIP activity names under the project ✅ |
| Output | `AipActivity.ExpectedOutputs` ✅ |
| Performance Target / Indicator | **Not stored in the AIP**, so Input (legacy WFP lines have `SuccessIndicator`, but pulling from WFP is not worth the coupling) |
| Means of Verification | **Not stored in the AIP**, so Input |
| Impact, Outcome | **Not stored anywhere**, so these have to be Input |

→ In practice this is **auto-prefill + editable**, not purely auto (Q6).

### F. Target Beneficiaries (unmarked, so Input)

Two groups, **Direct** and **Indirect**, each with repeating rows *Beneficiary / Male /
Female / Total* (Total auto-summed). Overlaps with the Section A disaggregated table. Ask
whether one can feed the other (Q11).

### G. Implementation Schedule / Work Plan (A)

| Column | Source |
|---|---|
| Inputs/Activities/Project Components | AIP activities under the project ✅ |
| Performance Target and/or Indicator | Input (could copy from the matching logframe row) |
| Gender Issues to be addressed | **Not stored**, so Input |
| Timeline/Duration | `AipActivity.StartDate`–`EndDate` ✅ |
| OPR (Office of Primary Responsibility) | `AipActivity.ImplementingOffice` ✅ |

One row per AIP activity, with Gender Issues typed in per row.

### H. Estimated Cost / Budgetary Requirements, Annex H-1 (A)

**Column order corrected by hand:** the printed "MOOE | PS | CO" is re-labelled to
**PS | MOOE | CO** (DBM standard order, same as AIP/WFP).

**Row structure** (from the page-3 notes and the separate sketch):

```
Activity 1                                   PS     MOOE    CO     Total   Source of Fund
   <acct code>  <object of expenditure>       –     xx,xxx   –     xx,xxx  GF
   <acct code>  <object of expenditure>       –     xx,xxx   –     xx,xxx  GF
   <acct code>  <object of expenditure>       –       –    xx,xxx  xx,xxx  20% DF
   Sub-total Activity 1                       –     xx,xxx xx,xxx  xx,xxx
Activity 2
   <acct code>  <object of expenditure>      ...
   Sub-total Activity 2
GRAND TOTAL
```

- Each activity is a header row, with **object-of-expenditure / account-code lines**
  indented under it and a **sub-total per activity**.
- The sketch circles "**code**" next to the fund column, so **Source of Fund shows the fund
  code** (GF, 20% DF…), not the full name.

**Data source: already in the AIP (v1.8.0).** The AIP expenditure-line feature was copied
from the WFP, so **`AipExpenditure`** (one row per account line under an `AipActivity`) holds
exactly what the sketch needs:

| Sketch element | `AipExpenditure` / related column |
|---|---|
| Activity header row | parent `AipActivity.Name` |
| Account code + object of expenditure | `AccountNumberSnapshot` + `AccountTitleSnapshot` |
| PS / MOOE / CO | `Ps` / `Mooe` / `Co` (pesos) |
| Total | `Total` (computed on write, never trusted from input) |
| Source of Fund (code) | `FundingSourceSnapshot` (the code, e.g. `GF`), matching the circled "code" in the sketch |
| Activity sub-total | Σ lines per activity |

Notes:

- Use the **snapshot** columns, not the live `Account`/`FundingSource` joins, so an old
  proposal still prints what the AIP said when it was encoded (the entity's own guidance).
- Procurement-itemised lines (`AipProcurementItem`) already roll up into the line's amount.
  Annex H-1 shows the line only, not the items.
- **Fallback:** an activity with no `AipExpenditure` lines (e.g. a synthetic activity, or one
  not yet detailed) prints as a single row with the activity-level PS/MOOE/CO.
- **Consistency check:** Section A *Project Cost* should equal the H-1 grand total. Both
  read the same data, so they agree by construction.

### I. Implementing Team (Input)

- Overall Project Supervisor, Project Manager: text fields.
- Members table: *Name / Sex / GAD-related Trainings Attended / Expertise* (repeating).
- Required Capacity Development Training table: *Member / Required Training* (repeating).

Possible later link: pick members from portal `User`s. For now plain text; no HR data
exists in the portal.

### J. Partnership and Sustainability (Input)

One rich-text field.

### K. Monitoring and Evaluation (Input)

Three fixed phases: **Pre-implementation, During implementation, Post-implementation**.
Each phase has repeating rows *M&E Activity / Schedule-Frequency / Monitoring Tools*.

### L. Risk Management (unmarked, so Input)

- Repeating rows *Possible Risk / Preventive Measures / Mechanisms to Monitor*.
- One field: *Strategies to avoid/minimize negative impact on women's status and welfare*.

### M. Climate Change Expenditure Typology (unmarked, but data exists)

`AipActivity.CcTypologyCode` (+ `CcAdaptation` / `CcMitigation` amounts) is already
stored, so this can be **auto** (distinct codes across the project's activities).
Suggest marking it A (Q12).

### Signatories

*Prepared by*, *Submitted by* (each Name + Position/Designation) and *Noted by: Local Chief
Executive*. Prepared-by could default to the logged-in user. The LCE name is the same on
every proposal, so it should be a **config value**, not retyped each time (Q13).

---

## 3. Field tally

| Kind | Count (approx.) | Examples |
|---|---|---|
| Auto from AIP | ~11 | title, proponent, schedule, cost, fund source, activities, OPR, outputs, CC typology, description + objective (prefill) |
| Auto from AIP expenditure lines | 1 section | Annex H-1 account-code breakdown (`AipExpenditure`) |
| Computed | 3 | Attributed GAD budget, every M/F Total, H sub-totals/grand total |
| Input (short) | ~6 | location, HGDG score, supervisor, manager, signatories |
| Input (long text) | 5 | description, rationale, J, general objective, women's-impact strategy |
| Input (repeating tables) | 8 | beneficiaries ×2, D, logframe gaps, team ×2, M&E, risks |

**Takeaway:** the portal can fill in the numbers and structure, but most of the document is
still narrative written by the proponent. The feature therefore needs a **saved,
editable proposal record** (draft → export). A one-shot "generate" button would make users
retype all the narrative every time the AIP changes.

---

## 4. HGDG: checklist and attributed GAD budget

The PCW HGDG manual has **sector-specific design checklists** (general project design plus
sector boxes such as agriculture, infrastructure, health, education, micro-finance, etc.).
The asterisk on "HGDG Checklist Used" most likely means **a dropdown of those checklists**.

The HGDG score (0–20) converts to the share of the budget attributable to GAD. The standard
PCW scale is:

| HGDG score | Interpretation | Budget attributed to GAD |
|---|---|---|
| 0 – 3.9 | GAD is invisible | 0% |
| 4.0 – 7.9 | Promising GAD prospects | 25% |
| 8.0 – 14.9 | Gender-sensitive | 50% |
| 15.0 – 19.9 | Gender-responsive (partial) | 75% |
| 20 | Fully gender-responsive | 100% |

So **Attributed GAD Budget = Project Cost × attribution %**. This makes the "A" on that
field achievable. ⚠️ Confirm the office uses this exact scale (Q4); some LGUs apply local
variations. This rule is the **only real business logic** in the feature, so it belongs in a
unit-tested Application service.

Open point: should the portal **compute** the score from a checklist the user ticks (much
bigger scope), or just **accept a typed score**? Recommend typed score for v1.

---

## 5. Word export: technical notes

- **No new dependency needed.** `ClosedXML` already pulls in **`DocumentFormat.OpenXml`**
  (the official Open XML SDK), which writes `.docx` as well. Same licence, already approved,
  already deployed.
- **Recommended approach: template-based.** Keep a `.docx` template (letterhead with seal +
  Bagong Pilipinas logo in the page header, "Page X of Y" footer, fonts, section headings)
  as an embedded resource. Fill it via bookmarks/content controls, and insert table rows for
  the repeating sections. Rebuilding the letterhead in code (as the WFP Excel export did,
  v1.4.4) is more brittle for Word layout.
- **Guidance text is not output.** The italic "✓ Provide background on…" bullets are
  instructions for the writer. In the portal they belong as **helper text under each input**,
  and the exported `.docx` should contain only headings, tables and the user's content.
  (Confirm against the samples, Q7.)
- **Page size:** the printout looks like **A4 portrait**. Confirm against a sample `.docx`.
- **Money formatting:** pesos with thousands separators, 2 decimals. On v1.8.0 all AIP
  amounts are already pesos, so **no ×1000 anywhere** in this feature.
- Endpoint shape would mirror the existing report exports (`ReportFunctions.cs`), e.g.
  `GET /api/project-proposals/{id}/export` returning `application/vnd.openxmlformats-officedocument.wordprocessingml.document`.

---

## 6. Dependencies and risks

| Risk | Detail |
|---|---|
| **Built on v1.8.0** | Every "A" field reads the redesigned AIP, so this feature goes on `release/1.8.0` (or a later 1.8.x). Pre-v1.8 AIPs (FY2027) have no `AipExpenditure` lines, so H-1 falls back to activity totals (Q14) |
| Activities without expenditure lines | Annex H-1 degrades to one row per activity. Consider a warning in the UI ("3 activities have no expenditure detail") before export |
| Snapshot vs live | If the AIP is edited after the proposal is drafted, do auto fields refresh? Recommend **live on export while Draft, snapshot on Finalize**, matching the AIP/WFP Draft→Final pattern |
| Scope / roles | Office-scoped like WFP: an office user sees only their office's proposals. SuperAdmin/Admin see all. Needs the per-role table in the spec |
| New tables | ~8–10 child tables (beneficiaries, benefits, logframe, work plan, team, trainings, M&E, risks). Snake_case per `NAMING_CONVENTIONS.md`. ⚠️ Migration needs a manual prod run |

---

## 7. Open questions (to answer with the samples)

1. ~~Title~~ **Settled 2026-09-27: "PGOM Investment Proposal"**, with Program Title above
   Project Title in Section A, both auto.
2. ~~Unit of a proposal~~ **Settled 2026-09-27: one proposal per AIP Project** (level 3).
   "Activity 1, 2…" in the work plan and Annex H-1 are that project's `AipActivity` rows.
3. **Project Type**: what values? Sector (Social/Economic/…), ESRE code, or something else?
4. **HGDG**: confirm the checklist list and the score → % attribution scale in §4. Typed score
   or computed from a checklist?
5. **Annex H-1**: confirmed from `AipExpenditure`. Remaining question: for an activity with no
   expenditure lines, print one row with the activity totals, or block export until detailed?
6. **Logframe / Work plan**: OK as "auto-prefill, then editable"? Impact/Outcome/Gender
   Issues have no data source.
7. Should the italic guidance text appear in the exported Word file? (Assumption: no.)
8. **Maps/geotagged photos** in Section B: do we need image upload in v1?
9. Rationale: one free-text box, or structured SDG / PDP-chapter pickers?
10. Section D "Project Cost" column: negative effects as text (per the template note), not
    pesos. Correct?
11. Section A beneficiaries vs Section F beneficiaries: same data, or entered separately?
12. Section M Climate Typology: auto from AIP `CcTypologyCode`?
13. Signatories: is the LCE name a config value? Prepared-by = logged-in user?
14. **Which fiscal year** will the office first prepare these for? FY2028 (v1.8 format) gets
    the full H-1; FY2027 would only get activity totals.
15. Workflow: is there a review/approval step (e.g. PPDO/GAD Focal Point checks it) before
    export, or just Draft → Final?

---

## 8. Suggested next steps

1. Review the sample `.docx` files. Confirm page setup, fonts, what gets printed, and how real
   proposals fill Annex H-1 and the logframe.
2. Answer §7, then write the spec per `docs/SPEC_STANDARD.md` (per-role cases, UI states,
   non-goals, acceptance list).
3. Likely ticket split: **(a)** entity + migration + CRUD for the proposal record,
   **(b)** auto-fill service (AIP + `AipExpenditure` reader + HGDG attribution), **(c)** Word export,
   **(d)** entry page (sectioned form, one tab per letter A–M), **(e)** proposal list page.

**Manual-implementation candidate:** the **HGDG score → attribution % → attributed budget**
calculator is a good one for Ralph. It is a pure function with a tiny blast radius and fully
testable with `dotnet test`, and `WfpExpenditureCalculator` is the sibling to copy the shape
from. The Word export itself is **not** a candidate, for the same reasons as
`ExcelService.cs` (poor feedback loop, layout-heavy).
