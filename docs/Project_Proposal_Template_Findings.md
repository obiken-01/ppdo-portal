# PGOM (Investment) Project Proposal — Template Findings

> **Status:** Exploration / template study. Not yet specced or scheduled to a release.
> **Date:** 2026-09-27
> **Source:** 5-page printed template "PGOM PROJECT PROPOSAL" (Provincial Government of
> Occidental Mindoro letterhead) with pen annotations, plus one hand-drawn sketch of the
> Section H layout. Sample filled-in proposals are to follow.
> **Output format:** Word (`.docx`) export.

---

## 1. What the document is

A **GAD-oriented project proposal** that the province requires per program/project. Its
structure follows the PCW **Harmonized Gender and Development Guidelines (HGDG)** project
design format — beneficiaries sex-disaggregated, gender issues in the rationale, work plan,
implementing team, M&E and risks — plus a DBM-style cost section (PS / MOOE / CO) and the
Climate Change Expenditure Typology.

Much of its header and cost data **already exists in the portal** (AIP, WFP, config
tables). The narrative sections are free text that only the proponent can write. The
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

"**Investment**" is written above "PROJECT" in the title, so the export title is probably
**"PGOM INVESTMENT PROJECT PROPOSAL"**. This ties the document to the **AIP** (Annual
*Investment* Program), which is where most auto fields come from. → *Confirm the exact
title (Q1).*

---

## 2. Section-by-section breakdown

Legend for the **Source** column: `AIP` = `AipOffice/AipProgram/AipProject/AipActivity`,
`WFP` = `WfpExpenditure(+Lines)`, `Cfg` = config tables (`Office`, `FundingSource`,
`Account`).

### A. Project Summary

| Field | Mark | Proposed source | Notes |
|---|---|---|---|
| **Program Title** (renamed from "Project Title") | A | `AipProgram.Name` (or `AipProject.Name`, see Q2) | Arrow from "**Pull down / Office**": the user picks the office, then the program from a dropdown, and the title fills in |
| Project Proponent | A | `AipOffice.Name` / `Office.OfficeName` | The office picked in the dropdown |
| Project Type | A | Unclear. Candidates: `AipOffice.Sector` (General/Social/Economic/Others), `AipActivity.EsreCode` (SS/ES/ID/EN), or "Program / Project / Activity" | **Q3** |
| Project Location | Input? | Not in AIP | Free text, e.g. municipality/barangay list. Could become a municipality multi-select later |
| Implementation Schedule: Start / End | A | `AipActivity.StartDate` / `EndDate` (earliest start, latest end across the program's activities) | AIP stores **month names as strings** ("January"), not dates. Output would be "January 2027 – December 2027" (fiscal year appended) |
| Project Cost | A | Σ `AipActivity.Total` under the program | ⚠️ AIP amounts are **₱ thousands**, so ×1000 for display. Or Σ WFP totals (pesos) if the cost should come from WFP (Q5) |
| Attributed GAD Budget | A | **Computed** = Project Cost × HGDG attribution % (see §4) | Only possible once the HGDG score is known |
| Funding Source | A | `AipActivity.FundingSourceSnapshot` → `FundingSource.Name` | A program can mix funds (e.g. "GF/20% DF"), so show the distinct list |
| HGDG Checklist Used | \* | Picklist of HGDG checklists | §4 |
| HGDG Score | (none) | Input (0–20) | Drives Attributed GAD Budget |

**Disaggregated Data of Intended Beneficiaries** (Input): repeating rows of
*Indicator / Male / Female / Total*. **Total = Male + Female should be auto-computed.**

### B. Project Description (Input)

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

- **General Goals/Objectives**: one text field (unmarked, so Input).
- Grid of **Impact / Outcome / Output / Input-Activities** × *Performance Target and/or
  Indicator* / *Means of Verification*. Marked **A**.

What the portal can actually fill:

| Row | Available data |
|---|---|
| Input/Activities | AIP activity names under the program ✅ |
| Output | `AipActivity.ExpectedOutputs` ✅ |
| Performance Target / Indicator | `WfpExpenditureLine.SuccessIndicator` (legacy WFP lines only) ⚠️ partial |
| Means of Verification | `WfpExpenditureLine.MeansOfVerification` (legacy only) ⚠️ partial |
| Impact, Outcome | **Not stored anywhere**, so these have to be Input |

→ In practice this is **auto-prefill + editable**, not purely auto (Q6).

### F. Target Beneficiaries (unmarked, so Input)

Two groups, **Direct** and **Indirect**, each with repeating rows *Beneficiary / Male /
Female / Total* (Total auto-summed). Overlaps with the Section A disaggregated table. Ask
whether one can feed the other (Q11).

### G. Implementation Schedule / Work Plan (A)

| Column | Source |
|---|---|
| Inputs/Activities/Project Components | AIP activities under the program ✅ |
| Performance Target and/or Indicator | Same partial source as E ⚠️ |
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

**Data source: this is the most important finding.** The AIP only holds **one PS / MOOE /
CO total per activity**. The **account-code breakdown exists only in the WFP**
(`WfpExpenditure.AccountNumberSnapshot / AccountTitleSnapshot`, amounts, fund per line). So:

- Full Annex H-1 = **WFP-sourced**. The proposal can only be fully generated **after that
  office's WFP for the fiscal year has expenditures entered**.
- Fallback when no WFP exists: one line per activity with the AIP PS/MOOE/CO totals and no
  account codes.
- WFP stores the **account number but not an expenditure class**. To place a WFP line in the
  PS vs MOOE vs CO column we need a class per `Account`. The `Account` entity comment says
  classification is *no longer derived from the AccountNumber prefix*, so check where the
  class lives today before relying on it. (Q5)

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
stored, so this can be **auto** (distinct codes across the program's activities).
Suggest marking it A (Q12).

### Signatories

*Prepared by*, *Submitted by* (each Name + Position/Designation) and *Noted by: Local Chief
Executive*. Prepared-by could default to the logged-in user. The LCE name is the same on
every proposal, so it should be a **config value**, not retyped each time (Q13).

---

## 3. Field tally

| Kind | Count (approx.) | Examples |
|---|---|---|
| Auto from AIP | ~9 | title, proponent, schedule, cost, fund source, activities, OPR, outputs, CC typology |
| Auto from WFP | 1 section | Annex H-1 account-code breakdown |
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
- **Money formatting:** pesos with thousands separators, 2 decimals. Remember the AIP
  ×1000 conversion. The project already has a rule that this conversion lives in one place
  (`WfpCeilingService` / `AllocationService`), so reuse that pattern.
- Endpoint shape would mirror the existing report exports (`ReportFunctions.cs`), e.g.
  `GET /api/project-proposals/{id}/export` returning `application/vnd.openxmlformats-officedocument.wordprocessingml.document`.

---

## 6. Dependencies and risks

| Risk | Detail |
|---|---|
| **v1.8.0 AIP redesign** | FY2028+ uses a new AIP format (clean break, no migration). Every "A" field reads the AIP, so the proposal must either wait for the v1.8 model or support both formats. **Build against whichever FY the office will first use this for (Q14).** |
| WFP dependency for Annex H-1 | No WFP expenditures, no account-code breakdown. Needs the AIP-totals fallback described above |
| Units | AIP = ₱ thousands, WFP = pesos. A missed ×1000 would show a ₱5M project as ₱5,000 |
| Snapshot vs live | If the AIP is edited after the proposal is drafted, do auto fields refresh? Recommend **live on export while Draft, snapshot on Finalize**, matching the AIP/WFP Draft→Final pattern |
| Scope / roles | Office-scoped like WFP: an office user sees only their office's proposals. SuperAdmin/Admin see all. Needs the per-role table in the spec |
| New tables | ~8–10 child tables (beneficiaries, benefits, logframe, work plan, team, trainings, M&E, risks). Snake_case per `NAMING_CONVENTIONS.md`. ⚠️ Migration needs a manual prod run |

---

## 7. Open questions (to answer with the samples)

1. **Title**: "PGOM Investment Project Proposal"? Or "Investment *Program* Proposal"?
2. **Unit of a proposal**: one per AIP **Program** (the title was changed to "Program
   Title") or per **Project** (level 3)? This decides what "Activity 1, 2…" in the work plan and
   Annex H-1 means.
3. **Project Type**: what values? Sector (Social/Economic/…), ESRE code, or something else?
4. **HGDG**: confirm the checklist list and the score → % attribution scale in §4. Typed score
   or computed from a checklist?
5. **Annex H-1 source**: WFP lines (full account-code breakdown) with AIP-totals fallback?
   Should Project Cost in Section A then equal the Annex H grand total?
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
14. **Which fiscal year** will the office first prepare these for, i.e. FY2027 (old AIP format)
    or FY2028 (v1.8 format)?
15. Workflow: is there a review/approval step (e.g. PPDO/GAD Focal Point checks it) before
    export, or just Draft → Final?

---

## 8. Suggested next steps

1. Review the sample `.docx` files. Confirm page setup, fonts, what gets printed, and how real
   proposals fill Annex H-1 and the logframe.
2. Answer §7, then write the spec per `docs/SPEC_STANDARD.md` (per-role cases, UI states,
   non-goals, acceptance list).
3. Likely ticket split: **(a)** entity + migration + CRUD for the proposal record,
   **(b)** auto-fill service (AIP/WFP reader + HGDG attribution), **(c)** Word export,
   **(d)** entry page (sectioned form, one tab per letter A–M), **(e)** proposal list page.

**Manual-implementation candidate:** the **HGDG score → attribution % → attributed budget**
calculator is a good one for Ralph. It is a pure function with a tiny blast radius and fully
testable with `dotnet test`, and `WfpExpenditureCalculator` is the sibling to copy the shape
from. The Word export itself is **not** a candidate, for the same reasons as
`ExcelService.cs` (poor feedback loop, layout-heavy).
