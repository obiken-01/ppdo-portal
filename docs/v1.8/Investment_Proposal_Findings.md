# PGOM Investment Proposal — Template Findings

> **Status:** Findings — complete. Promoted to
> [Investment_Proposal_Spec.md](Investment_Proposal_Spec.md) (accepted 2026-09-29, v1.8.0 / Demo 2.15),
> which is authoritative from here on.
> **Date:** 2026-09-27
> **Samples reviewed 2026-09-27:** three filled FY2027 proposals, see §8.
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
| Project Type | A (deferred) | **None for now.** Settled 2026-09-27: the row prints with a **blank value**. No field is stored and nothing is entered | Revisit later. Candidates noted for then: `AipOffice.Sector`, `AipActivity.EsreCode` |
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

**Component groups (settled 2026-09-27, Q16 option b).** The samples put activities under
sub-headers. These are **not** in the AIP; they are stored on the proposal:

- The user may create **component groups** (a free-text label and a sort order) and assign each
  of the project's AIP activities to at most one group. Order of activities within a group is
  also set in the proposal.
- The **same grouping drives both G and H-1**, so the two sections always list the same rows in
  the same order.
- Groups are optional. With no groups, rows print flat in AIP ref-code order. Activities left
  unassigned when groups exist print after the grouped ones, under no header.
- A group with no activities is not printed.
- Group headers carry **no amounts in G**. In H-1 they print as a header row; whether they also
  get a group sub-total is a small layout choice for the spec (samples show none).
- If an AIP activity is added to the project after the proposal was drafted, it appears
  unassigned; if one is removed, its group assignment is dropped with it.

**Proposal-only rows (settled 2026-09-27, Q17).** The samples list steps with no budget
("Preparation of Travel Order", "Reporting", "Reimbursement of claims/Liquidation of cash
advance"). These are not AIP activities, so the proposal stores them itself:

- A proposal-only row has a **name** plus the same G columns as an AIP row (Performance Target,
  Gender Issues, Timeline, OPR), all typed in. Unlike AIP rows, its Timeline and OPR are not
  auto-filled.
- It **never carries money.** In H-1 it prints with PS, MOOE, CO, Total and Source of Fund
  **blank**, so the H-1 grand total always equals Σ AIP activity totals (= Section A Project
  Cost).
- It sits in a component group and is ordered like any AIP row. AIP rows and proposal-only rows
  can be mixed in the same group (the Dev Plan sample does exactly this).
- The UI should mark the two kinds differently (e.g. an "AIP" tag vs "Proposal only"), because
  only proposal-only rows can be renamed or deleted in the proposal. An AIP row's name and
  amounts come from the AIP and are read-only here.

### H. Estimated Cost / Budgetary Requirements, Annex H-1 (A)

**Column order corrected by hand:** the printed "MOOE | PS | CO" is re-labelled to
**PS | MOOE | CO** (DBM standard order, same as AIP/WFP). **Settled 2026-09-27**, even though
current submissions still use the template order (Q18).

**Row structure** (from the page-3 notes and the separate sketch; see §8.2 for how the samples
actually fill H, with written computations that `AipProcurementItem` can generate):

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
| Component group header (optional) | proposal-only group label (Q16, see §2-G) |
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
- **Page setup:** A4 portrait, Verdana 10 pt, 1.78 cm margins. Confirmed from the samples (§8.1).
- **Money formatting:** pesos with thousands separators, 2 decimals. On v1.8.0 all AIP
  amounts are already pesos, so **no ×1000 anywhere** in this feature.
- Endpoint shape would mirror the existing report exports (`ReportFunctions.cs`), e.g.
  `GET /api/project-proposals/{id}/export` returning `application/vnd.openxmlformats-officedocument.wordprocessingml.document`.

---

## 6. Dependencies and risks

| Risk | Detail |
|---|---|
| **Built on v1.8.0** | Every "A" field reads the redesigned AIP, so this feature goes on `release/1.8.0` (or a later 1.8.x). **FY2028+ only** (Q14): FY2027 projects cannot have a proposal |
| Activities without expenditure lines | Annex H-1 prints the activity's own totals as one row, with a pre-export warning (Q5) |
| Snapshot vs live | **Settled (Q15):** AIP-sourced values are read live on every export while Draft and **snapshotted on Finalize**, matching the AIP/WFP Draft→Final pattern |
| Scope / roles | Office-scoped like WFP: an office user sees only their office's proposals. SuperAdmin/Admin see all. Needs the per-role table in the spec |
| New tables | ~8–10 child tables (beneficiaries, benefits, logframe, work plan, team, trainings, M&E, risks). Snake_case per `NAMING_CONVENTIONS.md`. ⚠️ Migration needs a manual prod run |

---

## 7. Questions and decisions

> As of 2026-09-27 every question is settled or carries a stated assumption (8, 9, 12). The
> one item still to check is the HGDG checklist list and % scale against the PCW manual (Q4).

1. ~~Title~~ **Settled 2026-09-27: "PGOM Investment Proposal"**, with Program Title above
   Project Title in Section A, both auto.
2. ~~Unit of a proposal~~ **Settled 2026-09-27: one proposal per AIP Project** (level 3).
   "Activity 1, 2…" in the work plan and Annex H-1 are that project's `AipActivity` rows.
3. ~~Project Type~~ **Deferred 2026-09-27: printed blank for now.** Out of scope for v1.
4. ~~HGDG~~ **Settled 2026-09-27: optional, with an auto-computed GAD budget.** Checklist Used is
   a picklist and HGDG Score a typed number (0–20), both optional. When a score is entered,
   Attributed GAD Budget = Project Cost × the PCW attribution % in §4 (0/25/50/75/100) and is
   not typed. With no score, all three print blank (as in every sample). The checklist list and
   the % scale still need confirming against the PCW HGDG manual before the spec is final.
5. ~~Annex H-1 empty activities~~ **Settled 2026-09-27:** an AIP activity with no expenditure
   lines prints **one row with its own PS/MOOE/CO totals**, and the portal shows a **warning
   before export** ("2 activities have no expenditure detail"). Export is not blocked.
6. ~~Logframe prefill~~ **Settled 2026-09-27:** pre-fill **General Goals/Objectives** from
   `AipProject.Objective` and the **Input/Activities** row with the project's activity names.
   Impact, Outcome, Output and every Performance Target / Means of Verification cell are typed.
   Everything stays editable. (Work plan G: activity, Timeline and OPR auto for AIP rows; the
   rest typed.)
7. ~~Guidance text in the export~~ **Answered by the samples: no.** None of the three prints it.
8. **Assumed (2026-09-27): no image upload in v1.** No sample includes a map or photo. Revisit
   if an office asks for one.
9. **Assumed (2026-09-27): one rich-text box for Rationale.** No structured SDG / PDP-chapter
   pickers in v1; the samples write alignment as prose.
10. ~~Section D "Project Cost"~~ **Answered by the samples: negative effects, as text.** Label the
    UI column "Project Cost (negative effects)" so encoders don't type amounts.
11. ~~Beneficiary tables~~ **Settled 2026-09-27:** Section A's table is entered once; F's
    **Direct** rows start as a copy of it and can then be edited independently. F's **Indirect**
    rows are entered separately. Totals (M + F) are computed; a TOTAL row is computed too.
12. **Assumed (2026-09-27): Section M is auto** from the project's AIP `CcTypologyCode` values
    (distinct, comma-separated), printing "N/A" when there are none. Matches the samples, which
    are blank or "N/A".
13. ~~Signatories~~ **Settled 2026-09-27: three editable slots**, each with label, name and
    position. Defaults: *Prepared by* = logged-in user (name + position from their profile);
    *Submitted by* = the PPDC; *Noted by* = Local Chief Executive, with the LCE name from a
    **config value** rather than retyped per proposal.
14. ~~First fiscal year~~ **Settled 2026-09-27: FY2028 onward only.** Proposals can be created
    only for projects in a v1.8-format AIP. FY2027 (old format) is out of scope.
15. ~~Workflow~~ **Settled 2026-09-27: Draft → Final.** Editable while Draft, exportable at any
    time; Finalize locks the proposal and snapshots the AIP-sourced values. No in-portal
    review/return flow (review stays on paper).
16. ~~Component grouping~~ **Settled 2026-09-27: option (b).** Each row in G and H is an **AIP
    Activity**; the sub-headers (e.g. "Capability Building of Women and Planning Activities") are
    **free-text component groups that exist only in the proposal**. See §2-G for the rules.
17. ~~Zero-cost rows~~ **Settled 2026-09-27: allowed.** Proposal-only rows print in G and H-1
    with blank amounts. See §2-G for the rules.
18. ~~H column order~~ **Settled 2026-09-27: PS | MOOE | CO.** The switch from the template's
    MOOE | PS | CO (which all three samples still use) is intended; it matches the AIP and
    Annex B.
19. ~~Rich text~~ **Settled 2026-09-27: bold, italic, bulleted and numbered lists.** No tables
    inside narrative sections in v1 (the PAMB rationale's table would be written as a list).

---

## 8. What the samples show

Reviewed 2026-09-27: three FY2027 proposals, all from PPDO.

| # | File | Project | Cost (A) | Fund |
|---|---|---|---|---|
| 1 | `1. Formulation and Updating of Development Plan Proposal 2027` | Formulation/Updating of Development Plans… | 539,200.00 | General Fund |
| 5 | `5. 2027 Proposal - KAPISAN BAKAJUANAN` | bakaJUANAn Project (Year 4) | 1,871,778.00 | GAD Fund |
| 10 | `10. PAMB Proposal 2027 OK` | PAMB Representations | 118,000.00 (H says 132,000.00) | General Fund |

### 8.1 Confirmed

- **Page and type:** A4 portrait, all margins 1009 twips (≈1.78 cm), header 709, footer 289.
  **Verdana** throughout, body **10 pt** (headings 14–16 pt, table notes 8–9 pt).
- **Letterhead** lives in the page **header** (seal + Bagong Pilipinas logo images, and the four
  lines "Republic of the Philippines / MIMAROPA Region / Province of Occidental Mindoro /
  PROVINCIAL GOVERNMENT OF OCCIDENTAL MINDORO"). **Footer** is "Page {PAGE} of {NUMPAGES}"
  fields. A template `.docx` carrying this header and footer is the right approach (§5).
  ⚠️ The seal image is a **2.1 MB PNG**; compress it before embedding in the template (the same
  lesson as the 17 MB Bagong Pilipinas logo in v1.0.1).
- **No guidance text is printed.** Only headings, tables and the user's content (Q7).
- **Section A** fields match the template. Values: schedule is "January 2027 / December 2027"
  (month + year); Funding Source is the fund **name** ("General Fund", "GAD Fund"); Proponent
  varies ("Provincial Planning and Development Office", "PPDO", "Planning Division-PPDO").
- **Logframe (E):** every row is narrative, including Impact and Outcome. Nothing a system could
  compute. Confirms E is Input, with at most the Input/Activities row prefilled (Q6).
- **Section B** is 1–2 paragraphs; **C** runs 4–12 paragraphs. No maps or photos in any
  sample (Q8 can wait).
- **Section D:** "Project Cost" is filled with **negative effects as text** in the two samples
  that fill it (Q10 answered: yes, text).
- **Implementing Team:** Overall Project Supervisor is the PPDC in all three; Project Manager is
  a Planning Officer IV. Member rows: name, sex (M/F), trainings, expertise.
- **M&E (K):** the three fixed phases hold. KAPISAN uses a Phase column instead of phase
  header rows; the export should pick one layout (template: header rows).

### 8.2 Annex H-1 in practice (the important part)

The samples do **not** list account codes. Each activity's MOOE cell holds a **written
computation**:

```
Mangrove Restoration
  Breakfast        295.00 x 30 pax x 2 days  = 17,700.00
  Snacks AM        155.00 x 50 pax x 2 days  = 15,500.00
  Accommodation  2,072.00 x 14 rooms x 2 days = 58,016.00
  Fuel              75.00 x 66 liters         =  4,950.00
  Planting Stocks and Other Materials           25,000.00
                                        Total  183,946.00
```

This is exactly the shape of v1.8.0 **`AipProcurementItem`** (`Name`, `UnitPrice`, `Qty`,
`Unit`, `NumberOfDays`, `LineTotal`) under each `AipExpenditure`. So the export can generate
these lines instead of the user typing them:

- Line: `{Name}  {UnitPrice} x {Qty} {Unit} [x {NumberOfDays} days] = {LineTotal}`
- Group by expenditure line (account), with the account title as the group heading. This
  combines the pen sketch (account codes) with what offices already write (computations).
- A line typed as a single amount (no procurement items) prints as `{AccountTitle}  {amount}`.
- CO items appear the same way in the CO column ("Procurement of Drone 200,000.00").

### 8.3 Arithmetic errors in the samples

Hand-typed computations have real mistakes. This is the strongest argument for generating H
from AIP data:

| Sample | What's written | Correct |
|---|---|---|
| PAMB | Section A Project Cost **118,000.00**; H total **132,000.00** | A and H must agree; H's own math (2,200 × 3 × 5 × 4) is 132,000 |
| Dev Plan | Venue 15,000 × 5 days = **55,000.00** | 75,000.00 (the 280,000 activity total and the 539,200 project cost carry the error) |
| KAPISAN | Venue 15,500 × 3 days = **45,000.00** (twice) | 46,500.00 |
| KAPISAN | Venue 15,500 × 2 days = **30,000.00** (three times) | 31,000.00 |
| Dev Plan | Total typed as "259.200.00" | 259,200.00 |

The KAPISAN totals are internally consistent (H sums to 1,871,778 = A), so the unit price was
probably 15,000 and mistyped. Either way, a generated document cannot disagree with itself.

### 8.4 Variations to decide on

| Area | Variation | Proposal |
|---|---|---|
| Title | All three still say "PGOM PROJECT PROPOSAL" (one omits it) | Use the settled "PGOM INVESTMENT PROPOSAL" |
| Section A | KAPISAN relabels "Project Cost" to "Cost of Program" and drops the HGDG and GAD rows | Fixed labels; HGDG/GAD rows always printed, blank allowed |
| Money | "539,200.00" vs "PhP 118,000.00" | One format: `#,##0.00`, "PhP" only on the Section A cost |
| Signatories | (1) Prepared / Noted / Approved (LCE); (5) Prepared / Submitted / Noted (LCE); (10) Prepared / Reviewed and Submitted / Approved (Governor) | 3 slots, each with editable label, name and position. Defaults: Prepared by = logged-in user, Submitted by = PPDC, Noted by = Local Chief Executive |
| Beneficiary tables | Some rows are labels only (no numbers), others have a TOTAL row | Allow rows with blank counts; compute the TOTAL row |
| Capacity training (I) | KAPISAN gives one training to all members (merged cell) | Store per member; the export may merge identical consecutive cells |
| Source of Fund in H | Blank in most rows | Auto from the line's fund, so never blank |

---

### 8.5 Images and attachments (study, 2026-10-01 — Demo 2.10 parked)

Asked by Ralph when parking Demo 2.10 (PPDO-132): *if* proposals get attachments, which section do
images belong to? Every body image in the nine samples and the template was listed with the section
heading above it (page-header logos excluded).

| File | Body images | Where |
|---|---|---|
| PAGES | **5** | A **"PHOTO DOCUMENTATION"** heading **after the signature block**, then five JPEGs, each the full printable area (17.3 × 22.8 cm, 96–445 KB). Not inside any lettered section |
| The other eight samples | 0 | — |
| The template | 0 | Section B's guidance says *"Maps can be used to describe the location of the project (geotagged)"*, but no sample does it |

What this suggests, for when the item is picked up:

- **Images are an annex, not a section field.** The one real case is photo documentation appended
  after the signatures. That fits a proposal-level **"Annexes / Photo documentation"** list (caption +
  image, printed one per page after the signatures) better than images inside rich-text sections,
  which decision 20 deliberately excludes.
- **Section B's map** is the only in-section place the template names, and nobody uses it yet.
  Treat it as the same annex ("Location map") rather than an image inside B, unless the PDC asks.
- **A scanned signed copy** is a different thing: the whole printed proposal after wet signatures.
  It is a file *about* the proposal (one per proposal, PDF), not content printed *in* it.
- **Size drives the design**, as PPDO-132 already says: the five PAGES photos alone are ~1 MB, and
  the DB is Azure SQL Basic (2 GB). Images go to Blob Storage (Southeast Asia), never the database,
  and the Word export would embed downsized copies (the seal lesson: a 2.1 MB PNG became 123 KB).

Open for the PDC when this resumes: is photo documentation expected for every proposal or only some
types; who uploads; is a scanned signed copy wanted at all now that the portal produces the document.

## 9. Suggested next steps

1. ~~Review the sample `.docx` files~~ Done, see §8.
2. ~~Answer §7~~ Done. ~~Write the spec~~ Drafted as
   [Investment_Proposal_Spec.md](Investment_Proposal_Spec.md). Original note: write the spec per `docs/SPEC_STANDARD.md` (per-role cases, UI states,
   non-goals, acceptance list).
3. Likely ticket split: **(a)** entity + migration + CRUD for the proposal record,
   **(b)** auto-fill service (AIP + `AipExpenditure` reader + HGDG attribution), **(c)** Word export,
   **(d)** entry page (sectioned form, one tab per letter A–M), **(e)** proposal list page.

**Manual-implementation candidate:** the **HGDG score → attribution % → attributed budget**
calculator is a good one for Ralph. It is a pure function with a tiny blast radius and fully
testable with `dotnet test`, and `WfpExpenditureCalculator` is the sibling to copy the shape
from. The Word export itself is **not** a candidate, for the same reasons as
`ExcelService.cs` (poor feedback loop, layout-heavy).
