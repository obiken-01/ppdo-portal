---
status: accepted — 2026-09-29
version: v1.8.0 (Demo 2.15)
tickets: PPDO-153 (parent), PPDO-154 … PPDO-161, PPDO-173
revised: 2026-10-01 — per-section save, one-section-at-a-time editor, selector, AIP Entry strip (decisions 24, 28, 29; §4, §6.2, §6.4)
revised: 2026-10-01 — aligned with the province template and nine FY2027 samples, PPDO-173 (decisions 3, 6, 14, 16, 17, 19, 30, 31; §4, §5, §6.2, §10)
supersedes: —
---

# v1.8.0 — PGOM Investment Proposal (Demo 2.15)

Governed by [SPEC_STANDARD.md](../SPEC_STANDARD.md). Built from
[Investment_Proposal_Findings.md](Investment_Proposal_Findings.md), which records the template
study, the three FY2027 samples and every question answered on 2026-09-27.

> **How to read the decisions.** **(R)** decisions are Ralph's, from the 2026-09-27 session.
> **(P)** decisions were proposed in this spec with their reasoning and **accepted by Ralph on
> 2026-09-29**, so both kinds are settled.
>
> **Where it lands.** Filed under the *Demo 2 — change requests & fixes* milestone, so it follows
> that milestone's convention for spec'd items (PPDO-130, PPDO-136): spec in `docs/v1.8/`, feature
> branches off `release/1.8.0`, PRs into `release/1.8.0`. Related: **PPDO-132** (Demo 2.10, attach a
> proposal file per project). Generating the proposal covers the Word document that request was
> about; whether PPDO-132's upload is still needed for scanned/signed copies is its own question.

---

## 1. Goal

Every AIP project needs a **PGOM Investment Proposal**: a 5-page Word document (summary,
rationale, logframe, work plan, cost annex, team, M&E, risks) that offices write by hand today.
The figures in it (schedule, cost, fund, activities, the cost annex) are already in the v1.8.0
AIP. Typing them again causes errors: all three samples reviewed contain arithmetic mistakes,
and one prints a project cost that disagrees with its own annex. This feature lets an office open
an AIP project, write only the narrative, and export a `.docx` whose numbers come from the AIP
and therefore always agree with it.

---

## 2. Decisions (settled)

### What a proposal is

1. **One proposal per AIP Project (level 3). (R)** The project is where the v1.8.0 AIP keeps
   `Description` and `Objective`, which were added for "a separate report that is not yet
   specified". This is that report.
2. **FY2028 onward only. (R)** Proposals exist only for projects where
   `AipFiscalYears.IsEntered(fiscalYear)` is true. FY2027 AIPs have no expenditure lines or
   procurement items, so H-1 could not be generated.
3. **Title "PGOM INVESTMENT PROPOSAL". (R)** The template and every sample say "PGOM PROJECT
   PROPOSAL". The new title is a PPDC change, confirmed 2026-10-01, and one of the two exceptions
   to decision 30.
4. **Draft → Final. (R)** Editable while Draft; exportable in either state. Finalize locks the
   proposal and **snapshots** every AIP-sourced value, so a Final proposal prints the same
   document even after the AIP changes. While Draft, AIP values are read **live** on every open
   and export. No in-portal review/return flow; review stays on paper.
5. **Reopen (Final → Draft) is allowed to the office's department head
   (`CanReviewBudgetPlanning`, own office) and to host-office Admin/SuperAdmin. (P)** Encoders
   can finalize but not reopen, so "Final" means something. Reopening discards the snapshot; the
   next read is live again.

### Where the content comes from

6. **Section A. (R)** Program Title (new row, above Project Title) = `AipProgram.Name`; Project
   Title = `AipProject.Name`; Proponent = the owning office's name; Schedule = earliest
   `StartDate` / latest `EndDate` across the project's activities, printed "January 2028";
   Project Cost = Σ `AipActivity.Total`; Funding Source = distinct fund **names** across the
   project's activities. **Project Type prints blank (R)** and is not stored. Five of the nine samples fill it
   ("Environmental & Natural Resources Development", "Institutional Development Project"), but the
   PDC has no list of values yet and asked for it blank (2026-10-01). The **Program Title** row is
   not on the template: it is a PPDC addition for reference (confirmed 2026-10-01), the other
   exception to decision 30. Project Location is
   typed.
7. **Amounts are Full value (₱), not the Annex B form figure. (P)** The proposal is a cost
   estimate whose computation lines must add up to the centavo, so it uses the amounts exactly
   as encoded. It is **not** rounded up and has **no +30%**
   ([AIP_Amount_States.md](AIP_Amount_States.md)). A proposal's Project Cost will
   therefore differ from the same project's figure on the Consolidated AIP. A one-line note under
   the Section A cost in the portal says so. The export itself carries no note.
8. **H-1 total always equals Section A Project Cost.** This holds by construction: since v1.8.0,
   an activity's PS/MOOE/CO are recomputed from its expenditure lines whenever it has any
   (`AipRepository.ApplyActivityTotalsAsync`). Both sections read the same rows.
9. **HGDG. (R)** Checklist Used (picklist) and HGDG Score (0–20, one decimal) are optional.
   When a score is present, **Attributed GAD Budget = Project Cost × attribution %** and is never
   typed:

   | Score | % |
   |---|---|
   | 0 – 3.9 | 0 |
   | 4.0 – 7.9 | 25 |
   | 8.0 – 14.9 | 50 |
   | 15.0 – 19.9 | 75 |
   | 20.0 | 100 |

   With no score, all three print blank. The scale and the checklist list come from the PCW HGDG
   guidelines and **must be confirmed against the manual** before the calculator ticket merges
   (open follow-up).
10. **Beneficiaries. (R)** Section A's disaggregated table is entered once. F's **Direct** rows
    start as **"Same as Section A"** (a checkbox, on by default): while it is on, F-Direct shows and
    prints A's rows and has no rows of its own. Unticking it copies A's current rows into F-Direct,
    which from then on is edited independently. Ticking it again (after a confirm) discards
    F-Direct's own rows. F's **Indirect** rows are always entered separately. Every row's Total = Male + Female, computed. Each table's TOTAL row is
    computed. Rows may leave counts blank (label-only rows appear in the samples).
11. **Logframe (E). (R)** General Goals/Objectives is pre-filled from `AipProject.Objective`, and
    the Input/Activities *Performance Target* cell from the project's activity names (one per
    line). Everything else is typed. Pre-fill happens **once, at creation**. After that the text
    belongs to the proposal and is never overwritten by later AIP edits.
12. **Section B. (P, from finding)** Project Description is pre-filled from
    `AipProject.Description` at creation, then independent, the same as decision 11.
13. **Section M. (R/assumed)** Printed from the project's activities' `CcTypologyCode` values,
    distinct, as "Code – Name" from `ClimateChangeTypology`, comma-separated. "N/A" when there
    are none. Not editable.

### Work plan (G) and cost annex (H-1)

14. **Rows are AIP Activities; component groups are proposal-only. (R)** The user may create
    groups (free-text label, order) and assign each row to at most one group. **One grouping and
    order drives both G and H-1.** With no groups, rows print flat in activity ref-code order.
    Ungrouped rows print after grouped ones. Empty groups are not printed. Group headers carry no
    amounts in G. In H-1 they are header rows with **no sub-total (P)**, matching the samples.
    **G's rows are numbered 1, 2, 3… straight through, ignoring groups (R, 2026-10-01)**, in the
    editor and in the export, proposal-only steps included. Group headers take no number.
15. **Proposal-only rows. (R)** The user may add rows that are not AIP activities ("Preparation
    of Travel Order", "Liquidation"). They carry a name and the four G text columns, all typed,
    and they **never carry money**. In H-1 their amount and fund columns print blank.
16. **AIP rows follow the AIP.** An activity added to the project later appears ungrouped at the
    end. An activity deleted from the AIP disappears from the proposal, along with its typed G
    text. For AIP rows, Timeline (`StartDate`–`EndDate` + fiscal year, printed with full month names as
    the samples do: "January–December 2028", or "March 2028" when both are the same month) and OPR
    (`ImplementingOffice`) are automatic. Performance Target and Gender Issues are typed per row.
17. **H-1 columns are MOOE | PS | CO | Total | Source of Fund**, in the template's order (↩️ revised
    2026-10-01, PPDO-173: this first said PS | MOOE | CO, which no sample uses). Headings word for
    word: *Input/Activities/Project Components* · *Budgetary Requirements and Other Inputs*
    (over MOOE, PS, CO) · *Total* · *Source of Fund*. **One row per activity**, as every sample
    prints it. Each money cell holds that activity's expenditures of that class, one block each:
    - heading: `AccountTitleSnapshot` followed by a colon (the samples' "TEV:", "Office Supplies:");
    - one computation line per `AipProcurementItem`:
      `{Name}  {UnitPrice:N2} x {Qty} {Unit}[ x {NumberOfDays} days] = {LineTotal:N2}`
      (the `x … days` part only when `NumberOfDays` ≠ 1);
    - an expenditure with no procurement items prints its heading and amount only;
    - the cell ends with its class total.

    Total = the activity's total. Source of Fund prints the fund **name** ("General Fund"), as every
    sample does, not the code: the distinct names of the activity's expenditure funds, resolved from
    `FundingSourceSnapshot` through the funding-source list while Draft and frozen in the snapshot
    on Finalize. A TOTAL row closes the table. Account titles come from the snapshot columns, never
    a live config join, so a renamed account doesn't rewrite history.
18. **Activity with no expenditure lines. (R)** Prints one row with the activity's own
    PS/MOOE/CO/Total and fund. The editor shows a **warning banner** listing such activities, and
    the export dialog repeats it. Export is not blocked.

### Everything else

19. **Signatories. (R)** **Four slots** (↩️ revised 2026-10-01, PPDO-173: the samples use two to four,
    labelled Prepared / Reviewed / Submitted / Noted / Approved by), each with an editable label,
    name and position. **A slot with no name is not printed.** The export lays them out two per row,
    as the template does. Defaults on creation:

    | Slot | Label | Name / position |
    |---|---|---|
    | 1 | Prepared by | the creating user's name and position |
    | 2 | Submitted by | from settings (PPDC) |
    | 3 | Noted by | from settings (Local Chief Executive) |
    | 4 | (blank) | (blank), so not printed until someone fills it |

    The defaults live in `investment_planning_settings` (decision 25).
20. **Rich text (R):** bold, italic, bulleted and numbered lists. Applies to B, C, J, the General
    Objective and the D cells. Stored as HTML restricted to `p, strong, em, ul, ol, li, br`. The
    server **strips anything else** on save, using the `HtmlSanitizer` package Announcements
    already uses, configured with this allow-list and no attributes. The TipTap editor is
    configured to produce only these.
21. **No guidance text in the export (R, samples).** The template's italic instructions become
    helper text under each input in the editor.
22. **Not included in v1 (R/assumed):** image or map upload in B; structured SDG/PDP pickers in
    C; tables inside narrative sections.
23. **Optimistic concurrency on the whole proposal. (P)** A proposal usually has one author, so
    the proposal row is the unit of conflict. It gets a `row_version` (`rowversion`). Every write
    sends the version it loaded; a stale one gets **409, nothing written, and the editor keeps
    the user's input**. This is the approved pattern of
    [AIP_Concurrent_Edit_Spec.md](AIP_Concurrent_Edit_Spec.md) (🅐, 🅒), applied from
    the first release rather than retrofitted.
24. **One PUT saves the whole editable document. (P)** Content is a few KB; per-section endpoints
    would multiply the concurrency surface for no gain. No autosave in v1.
    ↩️ **The editor saves per section (R, 2026-10-01, PDC request).** Each section has its own
    **Save section**. It sends that section's edits **plus every other section's last-saved
    values** in the same single PUT, so unsaved work elsewhere is never saved by accident and the
    API is unchanged. The header adds **Save all (n)** for every section with unsaved edits, and
    Ctrl+S saves the section in view. One `row_version` still covers the whole proposal (decision
    23), so two people saving *different* sections at once still get the 409. That is accepted,
    because a proposal usually has one author. **Finalize is disabled while any section has unsaved
    edits.** Wireframes: [wireframes/investment-proposal/](wireframes/investment-proposal/README.md).
25. **Signatory defaults in the existing settings row. (P)** Four nullable columns on
    `investment_planning_settings`: `ppdc_name`, `ppdc_position`, `lce_name`, `lce_position`.
    They use the same gate as the default fiscal year (`CanManageInvestmentPlanningSettings`,
    [Default_Fiscal_Year_Spec.md](Default_Fiscal_Year_Spec.md) decision 6). Only PPDO
    sets province-wide values. If unset, the slot is created with the label only.
26. **Deleting a project that has a proposal is refused. (P)** `AipService.DeleteProjectAsync`
    returns **409** "This project has an investment proposal. Delete the proposal first." The
    proposal is a person's narrative work, and a cascade would destroy it silently. Deleting
    *activities* is still allowed (decision 16).
27. **Word generation uses the Open XML SDK (`DocumentFormat.OpenXml`)** with a checked-in
    `.docx` **template** (letterhead header with both logos, "Page X of Y" footer, A4, Verdana
    10 pt, 1.78 cm margins, as measured from the samples). Code fills the body. The SDK is
    already resolved transitively through ClosedXML; the Infrastructure project adds an
    **explicit** `PackageReference` pinned to the same version. **No new download.**
28. **Sections that read another section show what is on screen. (R, 2026-10-01: "the less
    complicated one")** F-Direct's "Same as Section A" lists A's rows as currently entered, saved
    or not, and the H-1 preview follows G's current grouping and order. Neither marks unsaved
    values. The **export always uses saved data**.
29. **Two ways in besides the list. (R, 2026-10-01)** AIP Entry's project panel gets an
    **Investment proposal** strip (§6.4). The editor has its own **Fiscal year → (Office) → Program
    → Project** selector, each project showing its proposal status, plus **Open in AIP Entry ↗**.
    Both read `GET /proposals/projects` (§4). The Office picker appears only for callers who can
    see more than one office.

30. **Headings and labels follow the template word for word. (R, 2026-10-01)** Section titles,
    column headings and fixed row labels are the template's, in the editor and the export. They
    include E's "Project Structure" column and its "Input/Activities" row; D's five sectors
    (Social, Economic, Environmental, Institutional, Infrastructure/Land Use) with "PROJECT
    BENEFIT" and "PROJECT COST"; F's "Direct Beneficiaries" and "Indirect Beneficiaries" rows;
    K's PRE-IMPLEMENTATION, DURING IMPLEMENTATION and POST-IMPLEMENTATION rows; and L's "Strategies to
    avoid/minimize negative impact on women's status and welfare" box. The stored codes (`Input`,
    `Pre`, …) are not what prints. **Two exceptions, both PPDC's:** the title (decision 3) and the
    Program Title row (decision 6).
31. **Section I's capacity training is one cell per team member. (R, 2026-10-01)** The template's
    "Required Capacity Development Training of the Implementation Team" table lists every member of
    the team table, and the samples often give them all the same training in one merged cell. So
    the training is stored on the member (`required_training`), the editor lists the members
    automatically, and the export **merges identical neighbouring cells** vertically. A member with
    no training prints an empty cell. The template's Section I head rows are "Overall Project
    Supervisor:", "Project Manager" and one blank row, which prints blank.

### Open follow-ups (not blocking)

- **HGDG scale and checklist list**: confirm against the PCW HGDG manual (decision 9). Blocks
  merging PPDO-156 (T3), not starting it.
- **Project Type**: deferred (decision 6). The PDC will supply the list. The samples' values look
  like development-sector names ("Environmental & Natural Resources Development").
- **Map/photo upload in B**: revisit if an office asks.
- **PDF export**: not requested; Word is the deliverable.

---

## 3. Behaviour

### 3.1 Lifecycle

| Case | Given | When | Then |
|---|---|---|---|
| Happy path: create | An FY2028 AIP project in the caller's scope, no proposal | Caller clicks **Create proposal** | 201. Draft created. B, E-objective and E-input pre-filled; signatories defaulted (decision 19); A-beneficiaries empty, F-Direct "Same as Section A". Editor opens |
| Happy path: save | Draft, caller loaded version *v* | Caller saves | 200, new version *v+1*. Toast "Proposal saved" |
| Happy path: export | Draft or Final | Caller clicks **Export Word** | `.docx` downloads, named `Investment Proposal - {ProjectRefCode} - {ProjectName}.docx` (sanitized, ≤ 120 chars) |
| Happy path: finalize | Draft | Caller confirms Finalize | 200. Status Final, AIP values snapshotted, editor read-only, `finalized_at/by` set |
| Reopen | Final; caller is dept head (own office) or host Admin/SuperAdmin | Reopen | 200. Draft, snapshot cleared, next read live |
| Delete | Draft | Caller confirms Delete | 204. Proposal and all child rows removed. The project shows "No proposal" again |
| Edge: duplicate create | Proposal already exists | Create (e.g. two tabs) | 409 "This project already has a proposal." UI navigates to the existing one |
| Edge: FY2027 project | Project in an FY ≤ 2027 AIP | Create | 400 "Investment proposals start with FY 2028." The list never offers Create for these |
| Edge: project with no activities | Project exists, zero activities | Open / export | Allowed. A cost 0.00, G shows only proposal-only rows, H-1 prints header + TOTAL 0.00 |
| Edge: activity without lines | ≥1 activity has no expenditure lines | Open / export | Banner "{n} activities have no expenditure detail. Their totals are printed without a breakdown." H-1 per decision 18 |
| Edge: AIP changes while Draft | Encoder edits an activity amount | Proposal re-opened / exported | New figures appear. Nothing stored in the proposal changes |
| Edge: AIP changes while Final | Same | Proposal opened / exported | Snapshot figures. A notice: "The AIP has changed since this proposal was finalized," shown only when a live read differs from the snapshot (P, cheap: compare Project Cost and activity count) |
| Edge: activity added in AIP | Draft | Open | New activity appears ungrouped at the end of G/H-1 |
| Edge: activity deleted in AIP | Draft with typed G text on it | Open | Row and its typed text are gone (FK cascade) |
| Edge: HGDG score boundaries | Score 3.9 / 4.0 / 7.9 / 8.0 / 14.9 / 15.0 / 19.9 / 20.0 | Save | Attributed 0 / 25 / 25 / 50 / 50 / 75 / 75 / 100 % of Project Cost |
| Edge: HGDG score, no cost | Score 20, Project Cost 0 | Save | Attributed GAD Budget 0.00 |
| Failure: validation | Score 21, or −1, or two decimals | Save | 400 with field errors (§4). Nothing written |
| Failure: stale version | Another user saved since load | Save / finalize / reopen / delete | 409 "{Name} saved this proposal at {time}. Reload to see their changes." Editor keeps the user's input and offers **Reload** |
| Failure: edit Final | Status Final | PUT | 409 "This proposal is final. Reopen it to make changes." |
| Failure: delete Final | Status Final | DELETE | 409 same message |
| Failure: delete AIP project | Project has a proposal | AIP delete project | 409 (decision 26) |
| Failure: not found / out of scope | Id doesn't exist, or the caller's scope can't see its office | Any call | **404** "Proposal not found." Same response for both, so existence is never confirmed |

### 3.2 Roles and scope

Scope uses **`BudgetPlanningScope`** on the proposal's project's `AipOffice.OfficeId`, the same as
AIP Entry. Writes go through **`ConfigHttp.AuthorizeWriteAsync`**, so `ReviewerWriteGuard`
applies.

| Caller | List / open / export | Create / save / finalize / delete | Reopen |
|---|---|---|---|
| SuperAdmin | ✅ every office | ✅ | ✅ |
| Admin, host office (PPDO) | ✅ every office | ✅ | ✅ |
| Admin, guest office | ✅ own office only | ✅ own office | ❌ 403 |
| Staff, host office, `CanAccessBudgetPlanning` | ✅ per `BudgetPlanningScope` (PPDO division axis applies) | ✅ same scope | ❌ 403 |
| Staff, guest office (budget planning defaults ON) | ✅ own office | ✅ own office | ❌ 403 |
| Department head (`CanReviewBudgetPlanning`), own office | ✅ own office | ✅ (the guard allows dept heads to edit) | ✅ own office |
| PPDO cross-office reviewer (`CanReviewAllOffices`) | ✅ every office, **read only** (`OfficeScope.ResolveForReview`) | ❌ 403 (`ReviewerWriteGuard`) | ❌ 403 |
| Staff, host office, divisioned (PPDO-130) | projects whose program `ProgramDivision` gives their division, i.e. **the same programs AIP Entry shows them** | ✅ those projects | ❌ 403 |
| Staff in a divisioned office with **no division** | ✅ read (as AIP Entry) | ❌ 403 "You are not assigned to a division in this office…", matching AIP Entry | ❌ 403 |
| Staff without `CanAccessBudgetPlanning` | ❌ 403 | ❌ 403 | ❌ 403 |
| User with null `office_id` | Sees nothing (list empty; by-id 404) | 404 | 404 |
| Any caller, record in another office outside scope | 404 | 404 | 404 |

**Division submit does not lock proposals.** A proposal is not AIP content: a division that has
submitted its AIP work (PPDO-130) can still write its projects' proposals, and a department head's
return doesn't touch them. The proposal's own Draft → Final is its only lock. A project whose
activities span two divisions is editable by encoders of **either** division (the scope is the
program, as in AIP Entry's read scope), and optimistic concurrency (decision 23) covers the overlap.

⚠️ **The department-head reopen check is an office comparison against `users.office_id`, not
`OfficeScope.Resolve`.** A host-office department head resolves to `SeeAll` and must not gain
reopen over every office. This is the same trap as [Permission_Matrix.md](Permission_Matrix.md)
§4a. A new `PermissionService.CanReopenInvestmentProposalAsync(user, officeId)` carries the rule,
and it gets a matrix row and a pinning test.

---

## 4. API contract

All routes are **JWT-protected** and gated by `PermissionService.CanAccessBudgetPlanningAsync`
unless stated otherwise. Envelope: `ApiResponse<T>` (`{ data, error, message }`); services return
`ServiceResult<T>`. File: `InvestmentProposalFunctions.cs`. Amounts are pesos (decision 7).

Common errors: **401** no/invalid JWT · **403** `{ error: "Forbidden" }` (flag missing, or
reviewer write) · **404** `{ error: "Proposal not found." }` · **409** `{ error: "<message>",
data: { currentRowVersion, updatedByName, updatedAt } }` for a stale version.

### `GET /api/budget-planning/proposals?fiscalYear={int}&officeId={int?}&search={string?}&page={int=1}&pageSize={int=25}`

Lists **AIP projects** in scope for the year, each with its proposal status. This is how users find
a project to start from. Server-side paginated; `pageSize` ≤ 100.

200 → `PagedResult<ProposalListItemDto>`:

| Field | Type |
|---|---|
| `aipProjectId` | int |
| `projectRefCode` | string |
| `projectName` | string |
| `programName` | string |
| `officeName` | string |
| `projectCost` | decimal (Σ activity totals, live) |
| `proposalId` | int? (null = none yet) |
| `status` | `"None" \| "Draft" \| "Final"` |
| `updatedAt` | datetime? |
| `updatedByName` | string? |

400 when `fiscalYear` < first entered year: "Investment proposals start with FY 2028."
`officeId` outside scope is **clamped** to the caller's scope, not refused (same as the other
budget-planning lists).

### `GET /api/budget-planning/proposals/projects?fiscalYear={int}&officeId={int?}`

**Added 2026-10-01 (decision 29).** The editor's Program → Project selector and AIP Entry's
proposal strip. **One office, unpaged:** an office holds tens of projects, and both callers need
the whole set at once. Same scope rules as the list: guest callers are pinned to their own office
(`officeId` ignored), and callers who see more than one office must pass `officeId`, else
**400** "Choose an office." FY < 2028 → 400 as above. Ordered by program ref code, then project
ref code.

200 → `ProposalProjectOptionDto[]`:

| Field | Type |
|---|---|
| `aipProjectId` | int |
| `projectRefCode`, `projectName` | string |
| `programId` | int |
| `programRefCode`, `programName` | string |
| `proposalId` | int? |
| `status` | `"None" \| "Draft" \| "Final"` |
| `updatedAt` | datetime? |
| `updatedByName` | string? |

One query, a projection over AIP projects left-joined to `investment_proposals` on the unique
`aip_project_id`. No per-project round trips.

### `POST /api/budget-planning/proposals` — write

Request `{ aipProjectId: int }`. 201 → `ProposalDto` (below). 400 FY2027 · 404 project
missing/out of scope · 409 "This project already has a proposal." with `data.proposalId`.

### `GET /api/budget-planning/proposals/{id}`

200 → `ProposalDto`:

```
{
  id, status, rowVersion (base64), finalizedAt?, finalizedByName?, aipChangedSinceFinal (bool),
  header: { fiscalYear, programTitle, projectTitle, proponent, scheduleStart, scheduleEnd,
            projectCost, fundingSources: string[], climateTypology, attributedGadBudget? },
  warnings: { activitiesWithoutLines: [{ activityId, refCode, name }] },
  content: {                                   // everything the user edits (PUT body)
    projectLocation, hgdgChecklist?, hgdgScore?,
    beneficiariesSummary: [{ id?, indicator, male?, female? }],
    description, rationale,                    // sanitized HTML
    benefits: [{ sector, benefit, cost }],     // always the 5 template sectors, fixed order (decision 30)
    generalObjective,
    logframe: [{ level: "Impact"|"Outcome"|"Output"|"Input", target, verification }],
    directSameAsSummary: bool,
    targetBeneficiaries: [{ id?, kind: "Direct"|"Indirect", name, male?, female? }],  // Direct ignored while directSameAsSummary
    groups: [{ id?, clientKey, label }],       // order = array order
    workPlan: [{ id?, aipActivityId?, name?, groupKey?, performanceTarget, genderIssues,
                 timeline?, opr? }],           // order = array order; name/timeline/opr only
                                               // for proposal-only rows (aipActivityId null)
    projectSupervisor, projectManager,
    teamMembers: [{ id?, name, sex: "M"|"F", gadTrainings, expertise, requiredTraining }],  // decision 31
    partnershipSustainability,
    monitoring: [{ id?, phase: "Pre"|"During"|"Post", activity, schedule, tools }],
    risks: [{ id?, risk, prevention, monitoring }],
    womensImpactStrategy,
    signatories: [{ slot: 1|2|3|4, label, name, position }]
  },
  aipRows: [{ activityId, refCode, name, timeline, opr, ps, mooe, co, total, fundNames: string[],
              expenditures: [{ accountCode, accountTitle, ps, mooe, co, total, fundCode,
                               items: [{ name, unitPrice, qty, unit, numberOfDays, lineTotal }] }] }]
}
```

`workPlan` in the response always contains **every current AIP activity** of the project
(activities with no stored row are appended ungrouped), merged with stored rows.

### `PUT /api/budget-planning/proposals/{id}` — write

Request `{ rowVersion, content }`, where `content` has the shape above. Child collections are
**replaced** (rows missing from the body are deleted). 200 → `ProposalDto`.

Validation (400, `{ error: "Validation failed", errors: { "<path>": ["<message>"] } }`):

| Field | Rule | Message |
|---|---|---|
| `hgdgScore` | null or 0–20, max 1 decimal | "HGDG score must be between 0 and 20." |
| `hgdgChecklist` | null or a known code | "Choose a checklist from the list." |
| male / female counts | null or integer 0–1,000,000 | "Enter a whole number, 0 or more." |
| `teamMembers[].sex` | `M` or `F` | "Choose M or F." |
| `workPlan[].aipActivityId` | belongs to this proposal's project; each at most once | "This activity is not part of the project." |
| proposal-only row `name` | required, ≤ 500 | "Enter the step's name." |
| `groups[].label` | required, ≤ 300; `groupKey` must reference a group in the body | "Enter a group name." |
| `benefits` | exactly the 5 sectors of decision 30 | (400, programming error) |
| `teamMembers[].requiredTraining` | ≤ 1,000 | "Too long." |
| `signatories` | exactly slots 1–4; label ≤ 100, name ≤ 200, position ≤ 200 | "Too long." |
| rich-text fields | ≤ 100,000 chars after sanitizing | "This section is too long." |
| plain text cells | ≤ 4,000 chars | "Too long." |

409 stale version; 409 Final.

### `POST /api/budget-planning/proposals/{id}/finalize` — write

`{ rowVersion }` → 200 `ProposalDto`. 409 stale / already Final.

### `POST /api/budget-planning/proposals/{id}/reopen` — write + `CanReopenInvestmentProposalAsync`

`{ rowVersion }` → 200 `ProposalDto`. 403 not permitted · 409 stale / not Final.

### `DELETE /api/budget-planning/proposals/{id}?rowVersion={base64}` — write

204. 409 stale / Final.

### `GET /api/budget-planning/proposals/{id}/export`

200, `Content-Type: application/vnd.openxmlformats-officedocument.wordprocessingml.document`,
`Content-Disposition: attachment; filename*=UTF-8''…` (decision in §3.1). Same scope and 404 rules
as GET. Read-only, so reviewers may export.

### `GET` / `PUT /api/config/investment-planning/signatory-defaults` — `CanManageInvestmentPlanningSettingsAsync`

`{ ppdcName?, ppdcPosition?, lceName?, lcePosition? }`, each ≤ 200. PUT is audited like the
default fiscal year. `GET` is also readable with `CanAccessBudgetPlanning`, because create needs
it.

### Changed: `DELETE` AIP project

`AipService.DeleteProjectAsync` → **409** "This project has an investment proposal. Delete the
proposal first." when one exists (decision 26).

---

## 5. Data model changes

Migration: **`AddInvestmentProposals`** — ⚠️ MIGRATION. All new tables are **snake_case**
([NAMING_CONVENTIONS.md](../NAMING_CONVENTIONS.md)). Child tables cascade from
`investment_proposals`.

### `investment_proposals`

| Column | Type | Null | Notes |
|---|---|---|---|
| `id` | int identity | no | PK |
| `aip_project_id` | int | no | FK → AIP projects, **unique**, `ON DELETE NO ACTION` (decision 26) |
| `status` | nvarchar(10) | no | `Draft` / `Final` |
| `project_location` | nvarchar(500) | yes | |
| `hgdg_checklist` | nvarchar(50) | yes | code from the constant list |
| `hgdg_score` | decimal(3,1) | yes | 0–20 |
| `description` | nvarchar(max) | yes | sanitized HTML |
| `rationale` | nvarchar(max) | yes | sanitized HTML |
| `general_objective` | nvarchar(max) | yes | sanitized HTML |
| `partnership_sustainability` | nvarchar(max) | yes | sanitized HTML |
| `womens_impact_strategy` | nvarchar(4000) | yes | |
| `project_supervisor` | nvarchar(200) | yes | |
| `project_manager` | nvarchar(200) | yes | |
| `direct_same_as_summary` | bit | no | default 1 (decision 10) |
| `signatory{1,2,3,4}_label` | nvarchar(100) | yes | twelve signatory columns in all, flat. Always exactly four slots (decision 19). Slot 4 added by `AlignInvestmentProposalsWithTemplate` |
| `signatory{1,2,3,4}_name` | nvarchar(200) | yes | |
| `signatory{1,2,3,4}_position` | nvarchar(200) | yes | |
| `snapshot_json` | nvarchar(max) | yes | set on Finalize: the `header` + `aipRows` blocks of §4, serialized with `"schemaVersion": 1`. Readers must accept every version ever written. Null while Draft |
| `finalized_at` | datetime2 | yes | UTC |
| `finalized_by_id` | uniqueidentifier | yes | FK → Users, `SET NULL` |
| `created_at` / `updated_at` | datetime2 | no | UTC |
| `created_by_id` / `updated_by_id` | uniqueidentifier | yes | FK → Users, `SET NULL` |
| `row_version` | rowversion | no | `.IsRowVersion()` (decision 23) |

Why a JSON snapshot rather than copied tables: it is written once, read whole, never queried by
field, and has the exact shape the renderer consumes. Copying six AIP tables for a frozen
printout would double the schema for no query benefit.

### Child tables

| Table | Columns (all also have `id` PK and `proposal_id` FK cascade) |
|---|---|
| `investment_proposal_beneficiaries` | `section` nvarchar(10) (`Summary` / `Direct` / `Indirect`), `label` nvarchar(1000), `male` int null, `female` int null, `sort_order` int |
| `investment_proposal_benefits` | `sector` nvarchar(30) CHECK in the five sectors of decision 30, `benefit` nvarchar(4000) null, `cost` nvarchar(4000) null. Unique (`proposal_id`, `sector`) |
| `investment_proposal_logframe` | `level` nvarchar(10), `target` nvarchar(4000) null, `verification` nvarchar(4000) null. Unique (`proposal_id`, `level`) |
| `investment_proposal_groups` | `label` nvarchar(300), `sort_order` int |
| `investment_proposal_work_plan_rows` | `aip_activity_id` int null FK → AIP activities **`ON DELETE CASCADE`** (decision 16), `group_id` int null FK → groups `NO ACTION` (see note), `name` nvarchar(500) null, `performance_target` nvarchar(4000) null, `gender_issues` nvarchar(4000) null, `timeline` nvarchar(200) null, `opr` nvarchar(300) null, `sort_order` int. Filtered unique (`proposal_id`, `aip_activity_id`) where not null. Check: `aip_activity_id IS NOT NULL OR name IS NOT NULL` |
| `investment_proposal_team_members` | `name` nvarchar(200), `sex` char(1), `gad_trainings` nvarchar(1000) null, `expertise` nvarchar(500) null, `required_training` nvarchar(1000) null (decision 31), `sort_order` int |
| `investment_proposal_monitoring` | `phase` nvarchar(10), `activity` nvarchar(1000), `schedule` nvarchar(500) null, `tools` nvarchar(1000) null, `sort_order` int |
| `investment_proposal_risks` | `risk` nvarchar(1000), `prevention` nvarchar(2000) null, `monitoring` nvarchar(2000) null, `sort_order` int |

⚠️ **SQL Server multiple-cascade-path rule.** `work_plan_rows` is reachable from
`investment_proposals` both directly and via `groups`, so only one of those may cascade: `group_id`
is `ON DELETE NO ACTION`, and the service clears `group_id` before deleting a group (the PUT's
replace-all does this naturally). From the AIP side, `aip_projects → aip_activities →
work_plan_rows` is the only cascading path, because `investment_proposals.aip_project_id` is
`NO ACTION` (decision 26). **Verify the generated migration SQL applies cleanly on a copy of the
database before merging**; EF reports this conflict only at `database update`, not at
`migrations add`.

↩️ **`investment_proposal_capacity_trainings` was dropped** by `AlignInvestmentProposalsWithTemplate`
(PPDO-173) in favour of `team_members.required_training` (decision 31). It never held data: no
code wrote to it before the drop.

Indexes: unique `aip_project_id`; `(proposal_id)` on every child table (FK indexes); the list query
joins AIP projects → proposals by `aip_project_id`, already covered by the unique index.

### `investment_planning_settings` (existing, snake_case)

Add `ppdc_name`, `ppdc_position`, `lce_name`, `lce_position`: nvarchar(200), null.

**Backfill:** none. No existing rows change. The single settings row keeps nulls until set.

### Repository

`IInvestmentProposalRepository` with scoped queries only (no `GetAllAsync` filtering):
`GetByIdAsync(id)` (proposal + children, `Include` depth 1), `GetByProjectIdAsync`,
`ExistsForProjectAsync` (`AnyAsync`), `ListProjectsWithProposalsAsync(fy, officeFilter, search,
skip, take)` (projection to the slim DTO, `CountAsync` for the total), `SaveAsync`. AIP rows for a
proposal are read through the existing AIP repository methods (activities by project, expenditures
+ items by activity ids): **sequential awaits, no `Task.WhenAll`**.

---

## 6. UI states

Routes (static export, so query-string ids like the AIP pages):
`/budget-planning/proposals` (list) and `/budget-planning/proposals/edit?id={id}` (editor).
Sidebar: **"Investment Proposals"** under Investment Planning, after AIP, shown to anyone with
`CanAccessBudgetPlanning` (the rule lives in `lib/budget-planning-access`, like the others).

### 6.1 List — `/budget-planning/proposals`

| State | Content |
|---|---|
| Loading | Page header + filter bar render immediately; `TableSkeleton` with 8 rows at the loaded row height |
| Empty (no projects) | "No FY {year} projects in the AIP yet. Proposals are created from AIP projects, so add projects in AIP Entry first." + link to AIP Entry |
| Empty (search) | "No projects match "{search}"." + Clear search |
| FY2027 selected | "Investment proposals start with FY 2028." No table |
| Error | Inline error panel "Couldn't load projects." + Retry. Filters stay usable |
| Success | `DataTable`: Ref code · Project (program as a second muted line) · Office (host-office users only) · Cost (₱, right-aligned) · Status (`StatusPill`: None grey / Draft amber / Final green) · Updated · `RowActions` (**Create** when None, **Open** otherwise, **Export** when a proposal exists) |
| Read-only | Cross-office reviewer: **Create** hidden; Open opens the read-only editor |
| Validation | n/a |

Filters: fiscal year (defaults from `useDefaultFiscalYear()`), office (`OfficeSelect`, host-office
users only), search (ref code or name, debounced 300 ms).

### 6.2 Editor — `/budget-planning/proposals/edit?id=`

Routes: `?id={proposalId}` opens a proposal; `?projectId={aipProjectId}` opens the selector on a
project (the Create panel when it has none, else it redirects to `?id=`). **Open in AIP Entry ↗**
goes to `/budget-planning/aip/entry?fiscalYear=&programId=&projectId=`.

↩️ **Layout revised 2026-10-01 (decisions 24, 28, 29; PDC request).** Wireframes:
[wireframes/investment-proposal/](wireframes/investment-proposal/README.md), artboard 1 is the
default view.

- **Sticky header:** title, status pill, an "{n} sections unsaved" chip, **Save all (n)**, **Export
  Word**, **Finalize** (disabled while anything is unsaved) / **Reopen**, overflow **Delete**.
  Under it, the **selector row**: Fiscal year · Office (only for callers who see more than one) ·
  Program `Lookup` · Project `Lookup` (each option shows its status None/Draft/Final) · **Open in
  AIP Entry ↗**, plus the **One section at a time** switch. Picking a project with no proposal
  shows a **Create proposal** panel in place of the sections.
- **Left rail:** the sections A–M and Signatories, each with a status dot: saved · unsaved · filled
  from the AIP · not started (a legend sits under the rail). The rail is how you move between
  sections.
- **One section at a time (the default):** only the current section's card shows, with **← previous**
  and **next →** in its footer. Switching it off shows **every section as a collapsible card**
  (Expand all / Collapse all), each folded card showing a one-line summary ("3 of 5 sectors
  filled", "Direct: same as Section A").
- **Each section card:** template heading and guidance (muted helper text), its status pill, and a
  footer with **Discard** and **Save section** (decision 24). A folded card with unsaved edits keeps
  a **Save** in its header. Collapsing never discards. **H and M** are filled from the AIP and have
  no Save. Auto fields render as read-only text with a "From AIP" tag; proposal-only work-plan rows
  get a "Proposal only" tag.
- **Leaving a section with unsaved edits** (rail, previous/next, project selector, any other
  navigation) asks **Save and continue · Discard · Keep editing**. Reload/close gets the browser's
  own prompt. Reuse the unsaved-changes guard from AIP Entry (`AipUnsavedChanges`, PPDO-166) and
  add the third button.

| State | Content |
|---|---|
| Loading | Header and left rail render; each section card shows a skeleton of its own shape (summary table rows, 3 text lines, table rows). No full-page spinner |
| Empty | Not reachable: a proposal always has content after create. Empty tables show one blank row and "+ Add row" |
| Error (load) | "Couldn't load this proposal." + Retry. 404 → "Proposal not found. It may have been deleted or is outside your office." + Back to list |
| Error (save) | Toast "Couldn't save. Your changes are still here." Input kept. 409 stale → the section footer turns amber with the §3.1 message and **Reload** (discards local edits after a second confirm) / **Keep editing** |
| Success | Toast "Section C saved" (or "{n} sections saved" from Save all); the section's pill turns **Saved** and its rail dot green. Finalize → toast "Proposal finalized" and the page turns read-only |
| Warning | Amber banner under the header when `activitiesWithoutLines` is non-empty (decision 18), listing ref codes |
| Final | All inputs read-only (rendered as text, not disabled inputs); banner "Final — {date} by {name}"; Reopen shown only to permitted callers; "The AIP has changed since this proposal was finalized" notice when flagged |
| Read-only / forbidden | Cross-office reviewer: same read-only rendering as Final, no Save/Finalize/Delete (**hidden**, not disabled). Export stays |
| Validation | Messages under each field in `text-red-600`; the section pill reads "{n} error(s) — not saved" and the rail marks it; Save section scrolls to the first error in that section |
| Unsaved changes | Leaving a section: **Save and continue · Discard · Keep editing**. Leaving the page: the same dialog for in-app navigation, the browser's prompt for reload/close |
| No proposal yet | Selector on a project with status None: a panel "This project has no investment proposal yet." + **Create proposal** (hidden for cross-office reviewers) |

Section specifics:
- **A:** auto rows as text. Location input. HGDG checklist `Lookup`, score number input. The
  computed GAD budget updates live as the score changes. Beneficiary table with computed Total
  column and TOTAL row.
- **D:** fixed 5 sector rows; the cost column is labelled **"Project Cost (negative effects)"**.
- **G:** drag-free ordering with ↑/↓ buttons (consistent with the rest of the portal). Group
  headers are editable rows with "Remove group" (their rows fall back to ungrouped). "+ Add group",
  "+ Add step (proposal only)". A row's group is chosen from a select on the row.
- **H-1:** read-only preview rendered the same as the export, grouped like G, MOOE | PS | CO, one
  row per activity (decision 17).
- **I, K, L:** repeating tables with "+ Add row" and row delete via `RowActions`.
- **I:** Supervisor and Manager inputs; the member table (Name · Sex M/F · GAD-related Trainings
  Attended · Expertise); then the capacity table, which lists the members automatically with one
  training input each and a **Same for all members** action that copies the first filled training
  into every empty cell. Blank member rows are dropped on save.
- **Signatories:** four slot cards with label/name/position inputs. An empty slot shows "Not
  printed".

Components: reused `DataTable`, `TableSkeleton`, `RowActions`, `StatusPill`, `OfficeSelect`,
`Lookup`, `Modal`, `ConfirmDialog`, `MessageDialog`, `useToast`, `MoneyInput` (display only), the
TipTap setup from Announcements (**extract** `RichTextToolbar` into `components/ui/RichTextEditor.tsx`
restricted to bold/italic/lists). New: `ProposalSectionCard`, `ProposalWorkPlanEditor`,
`AnnexH1Preview`. Flat design, PPDO tokens only, no `text-slate-700`
([DESIGN_SYSTEM.md](../DESIGN_SYSTEM.md)).

### 6.3 Config — signatory defaults

A card on the existing `/config/investment-planning` page: four inputs, Save. Same
loading/error/success states as the default-fiscal-year card beside it. Hidden (not disabled) for
anyone without `CanManageInvestmentPlanningSettings`.

### 6.4 AIP Entry — the Investment proposal strip (added 2026-10-01, decision 29)

In AIP Entry's project panel (`AipProjectPanel`), between the project details and its activities.
FY 2028+ projects only. Data: one `GET /proposals/projects` call for the page's office and year,
mapped by `aipProjectId`, and refreshed when the panel is opened, not after every edit. Wireframe
artboard 4.

| State | Content |
|---|---|
| None | "Investment proposal" · `StatusPill` None · "Creates it with the description, objective and activity names already filled in." · **Create proposal** (creates, then opens the editor) |
| Draft | Pill Draft · "Last saved {time} by {name}" · **Open proposal →** |
| Final | Pill Final · "Finalized {date}" · **Open proposal →** |
| Read-only caller | Cross-office reviewer: **View proposal →**, never Create. A project outside the caller's division lock shows the strip like the rest of the panel, read-only |
| FY 2027 | No strip |
| Loading / error | The strip is left out until the call settles; on error it is left out silently (the panel still works, and the list page is the fallback) |

---

## 7. Non-goals

- **FY2027 and earlier.** No legacy-format support (decision 2).
- **Editing AIP data from the proposal.** Names, schedules, amounts and lines are read-only here.
  Change them in AIP Entry. Two editors for one number is how the samples ended up disagreeing
  with themselves.
- **Project Type, image upload, SDG/PDP pickers, tables in narrative.** Decisions 6 and 22.
- **In-portal review, comments or approval routing.** Paper review stays (decision 4). Don't wire
  proposals into the AIP comment system.
- **Computing the HGDG score from a checklist.** The score is typed; only the GAD budget is
  computed.
- **PDF export, multi-proposal batch export, a proposal per activity or per program.**
- **Autosave and real-time co-editing.** Explicit save + optimistic concurrency (decisions 23–24).
- **Importing existing `.docx` proposals.** FY2028 proposals start in the portal.

---

## 8. Deployment notes

- **Migration `AddInvestmentProposals`**: run `dotnet ef database update` manually against Azure
  SQL **before** the code deploys. CI does not run migrations. It ships inside v1.8.0, so it joins
  the list in [Pre_Deployment_Checklist.md](Pre_Deployment_Checklist.md) (T1 adds the line). It
  runs after `AddInvestmentPlanningSettings` (PPDO-143), whose table it extends.
- **Dependency:** explicit `PackageReference` to `DocumentFormat.OpenXml` in
  `PPDO.Infrastructure`, pinned to the version ClosedXML 0.104.2 already resolves (check
  `obj/project.assets.json`). Nothing new is downloaded at runtime.
- **Template asset:** `PPDO.Infrastructure/Templates/InvestmentProposalTemplate.docx`, embedded
  resource. **Compress the seal image first** (the samples carry a 2.1 MB PNG; target < 200 KB).
- **Config after deploy:** a PPDO config manager sets the PPDC and LCE names on
  `/config/investment-planning`. Until then, slots 2–3 are created with labels only.
- No new environment variables, no CORS change, no Azure resources.

---

## 9. Ticket split

Parent **PPDO-153** (child of the Demo 2 epic PPDO-122). Prompts:
[Investment_Proposal_Ticket_Prompts.md](Investment_Proposal_Ticket_Prompts.md).

| Ticket | Scope | Blocked by |
|---|---|---|
| **PPDO-154** T1 Data model | Entities, configurations, `AddInvestmentProposals` migration, settings columns, repository. ⚠️ MIGRATION | — |
| **PPDO-173** T1b Template alignment | Fourth signatory slot, `team_members.required_training` (drops `capacity_trainings`), sector CHECK; `AlignInvestmentProposalsWithTemplate`. ⚠️ MIGRATION | PPDO-154 |
| **PPDO-155** T2 Service + API | `InvestmentProposalService` (create with pre-fill, get with live/snapshot merge, PUT replace + sanitize, finalize/reopen/delete, concurrency), `CanReopenInvestmentProposalAsync` + matrix row, `InvestmentProposalFunctions`, list endpoint, **`GET /proposals/projects` (added 2026-10-01)**, AIP project-delete 409, signatory-defaults endpoints. Ships `attributedGadBudget = null` | PPDO-154 |
| **PPDO-156** T3 HGDG calculator | Pure `HgdgAttribution` + tests + the one-line wiring into T2. Merge needs the PCW scale confirmed | PPDO-155 |
| **PPDO-157** T4 Document builder | Pure `InvestmentProposalDocumentBuilder` (Application): header, grouped G rows, H-1 blocks with computation strings, totals, rich-text model. No Open XML | PPDO-155 |
| **PPDO-158** T5 Word export | `IInvestmentProposalWordService` / `InvestmentProposalWordService`, template asset, export endpoint | PPDO-157 |
| **PPDO-159** T6 List page + ways in | §6.1, sidebar entry via `lib/budget-planning-access`, **§6.4 AIP Entry strip (added 2026-10-01)** | PPDO-155, PPDO-145 |
| **PPDO-160** T7 Editor page | §6.2 (revised 2026-10-01: one section at a time, per-section Save, selector, leave dialog), `RichTextEditor` extraction, H-1 preview | PPDO-155, PPDO-157 |
| **PPDO-161** T8 Signatory defaults card | §6.3 | PPDO-155, PPDO-144 |

T3 deliberately follows T2 instead of blocking it, so the hand-coding ticket is never on the
critical path.

**PPDO-156 (T3) is a good hand-coding candidate for Ralph:** small blast radius, a pure function,
testable entirely with `dotnet test`, and `WfpExpenditureCalculator` is the sibling to copy the
shape from. Guidance starts at ladder level 1 (this spec + the sibling). **Not candidates:** T2
(scope and permission logic, where a missing check isn't caught by the compiler), T1 (migration),
T5 (layout-heavy Open XML with a slow feedback loop, the same reasons as `ExcelService.cs`) and
T7 (a large page component).

---

## 10. Acceptance checklist

Lifecycle
- [ ] On the Proposals list for FY 2028, an AIP project without a proposal shows status **None** and a **Create** action
- [ ] Creating a proposal opens the editor with Section B filled from the project's AIP Description and the General Objective from its AIP Objective
- [ ] Section A shows Program Title above Project Title, both matching AIP Entry, and Project Type blank
- [ ] Opening two tabs, saving in one, then saving in the other shows "{Name} saved this proposal at {time}…" and the second tab keeps its typed text
- [ ] After Finalize, every input renders as text and Save/Delete are gone
- [ ] Changing an activity amount in AIP Entry changes the Draft proposal's Project Cost on reload, but not a Final one's
- [ ] A Final proposal whose AIP changed shows "The AIP has changed since this proposal was finalized"
- [ ] Selecting FY 2027 on the list shows "Investment proposals start with FY 2028."
- [ ] Deleting an AIP project that has a proposal is refused with "This project has an investment proposal…"

Content rules
- [ ] Entering HGDG score 8 on a ₱200,000.00 project shows Attributed GAD Budget ₱100,000.00; clearing the score blanks it
- [ ] Entering score 21 shows "HGDG score must be between 0 and 20." under the field and nothing saves
- [ ] On a new proposal, Section F's Direct rows show "Same as Section A" ticked and list A's rows; unticking it and editing F doesn't change A
- [ ] Male 3 + Female 5 shows Total 8, and the TOTAL row sums the column
- [ ] Section D's cost column is labelled "Project Cost (negative effects)"
- [ ] Pasting a table into Rationale saves as plain paragraphs (no table in the saved text)

Work plan and cost annex
- [ ] Creating group "Capability Building" and moving two activities into it shows them under that header in both G and the H-1 preview, in the same order
- [ ] Adding a proposal-only step "Reporting" shows it in G with typed Timeline/OPR, and in H-1 with blank amounts
- [ ] An activity added in AIP Entry after the proposal was created appears ungrouped at the end
- [ ] H-1 columns read MOOE | PS | CO | Total | Source of Fund, one row per activity, Source of Fund showing fund names
- [ ] G's rows are numbered 1, 2, 3… straight through, groups included
- [ ] Every heading and fixed label matches the template word for word, except the title and the Program Title row
- [ ] Section I's capacity table lists every team member; identical neighbouring trainings print as one merged cell
- [ ] A procurement item Lunch ₱394.00 × 30 pax × 3 days prints "Lunch  394.00 x 30 pax x 3 days = 35,460.00"
- [ ] The H-1 grand total equals the Section A Project Cost
- [ ] An activity with no expenditure lines triggers the amber banner and prints as a single totals row

Export
- [ ] **Export Word** downloads `Investment Proposal - {RefCode} - {Name}.docx`, which opens in Word without a repair prompt
- [ ] The document is A4, Verdana, with the provincial letterhead in the header and "Page X of Y" in the footer, and is titled "PGOM INVESTMENT PROPOSAL"
- [ ] No template guidance text (italic instructions) appears in the export
- [ ] Section M prints the AIP typology codes, or "N/A" when there are none
- [ ] Signatures print the filled slots (up to four), two per row, with the labels, names and positions entered; an empty slot does not print

Roles and scope
- [ ] A guest-office Staff user sees only their office's projects and cannot open another office's proposal by editing the URL id (sees "Proposal not found")
- [ ] A PPDO cross-office reviewer can open and export any office's proposal but sees no Save, Finalize, Delete or Create
- [ ] An encoder can Finalize but sees no Reopen; their department head sees Reopen for their own office only
- [ ] A host-office department head sees no Reopen on another office's Final proposal
- [ ] A Staff user without Budget Planning access doesn't see "Investment Proposals" in the sidebar

UI states
- [ ] The list shows an 8-row skeleton on first load, not a spinner
- [ ] The editor shows section-shaped skeletons while loading
- [ ] Navigating away with unsaved edits asks **Save and continue · Discard · Keep editing**

Editor layout and saving (added 2026-10-01)
- [ ] The editor opens one section at a time; switching the toggle off shows every section as a collapsible card with a one-line summary
- [ ] Editing Section C and clicking its **Save section** saves C only: unsaved edits in E stay unsaved (rail dot stays amber) and are not in the saved document
- [ ] Folding a section with unsaved edits keeps them, and its header shows a **Save**
- [ ] With two sections unsaved, the header shows **Save all (2)**, and **Finalize** is disabled until both are saved
- [ ] Sections H and M show "From AIP" and have no Save
- [ ] Ticking "Same as Section A" in F lists A's rows including an unsaved row just typed in A; exporting before saving A prints A's saved rows
- [ ] The Program → Project selector lists the office's FY 2028 projects with their proposal status, and picking a project without a proposal offers **Create proposal**
- [ ] **Open in AIP Entry ↗** opens AIP Entry on the same project

AIP Entry strip (added 2026-10-01)
- [ ] An FY 2028 project with no proposal shows **Create proposal**; clicking it opens the new proposal with B and the objective filled in
- [ ] A project with a Draft proposal shows "Last saved {time} by {name}" and **Open proposal →**
- [ ] A cross-office reviewer sees **View proposal →** and never **Create proposal**
- [ ] FY 2027 projects show no strip

---

## 11. Test focus

TDD for the service, calculator, builder and permission rule (`CLAUDE.md`).

- **`HgdgAttributionTests`**: every boundary in decision 9 (3.9/4.0/7.9/8.0/14.9/15.0/19.9/20.0),
  null score → null, cost 0, and the rounding of the resulting peso amount (2 decimals, away from
  zero).
- **`InvestmentProposalServiceTests`**
  - Create: pre-fills B/objective/input row once; `directSameAsSummary` true by default and F-Direct
    prints A's rows while it is; turning it off copies A's rows; FY2027 → 400;
    duplicate → 409; out-of-scope project → 404.
  - Get: live merge appends new activities ungrouped; deleted activities vanish; Final returns
    the snapshot, not live values; `aipChangedSinceFinal` flips only when cost or activity count
    differs.
  - Put: replace-all semantics; a foreign `aipActivityId` → 400; duplicate activity row → 400;
    HTML sanitizer strips `<table>`, `<script>`, attributes; stale row version → 409 with the
    other user's name; Final → 409.
  - Finalize/reopen/delete: state rules, snapshot written/cleared.
  - Scope (red-test each against the office comparison): guest Staff can't read another office;
    null-office user sees nothing; cross-office reviewer reads but every write → 403.
- **`PermissionMatrixTests`**: new rows for `CanReopenInvestmentProposalAsync`, including the
  host-office department head **not** reopening another office (the §4a trap).
- **`InvestmentProposalDocumentBuilderTests`**: grouping order shared by G and H-1; ungrouped
  after grouped; empty groups skipped; proposal-only rows blank in H-1; computation string format
  (with and without days, `NumberOfDays` = 1 omitted); expenditure without items; activity
  without lines; H-1 total = Σ activity totals; Section M "N/A"; schedule min/max across months
  in calendar order (not alphabetical).
- **`AipServiceTests`**: `DeleteProjectAsync` with a proposal → 409; without → unchanged.
- **`InvestmentProposalWordServiceTests`**: the output opens with `WordprocessingDocument.Open`
  and validates with `OpenXmlValidator` (zero errors); it contains the title and one H-1 row per
  expected line.
- **`ReviewerWriteGuardCoverageTests`**: the five new write endpoints are covered by the guard.

---

*Investment Proposal Spec — v1.8.0 (Demo 2.15) — drafted 2026-09-27, accepted 2026-09-29.*
