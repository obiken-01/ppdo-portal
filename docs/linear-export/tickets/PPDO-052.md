---
id: PPDO-52
title: 'V18-42 — Three-stage AIP entry UI (sub-office group → project/activity → expenditures)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-07
parent: PPDO-48
blocked_by: PPDO-50, PPDO-51, PPDO-62, PPDO-64
related: PPDO-1, PPDO-61, PPDO-63, PPDO-65, PPDO-67, PPDO-69
url: https://linear.app/ralphoksiprojects/issue/PPDO-52
uuid: 9fb0d423-d0fa-4caa-a1a9-226fb3172ff4
---

# PPDO-52 — V18-42 — Three-stage AIP entry UI (sub-office group → project/activity → expenditures)

> ↩️ **Premise changed 2026-09-05 — re-read the spec before starting.**
>
> This ticket was written against the office-owned record shape (one record per office per year). That is **withdrawn**: there is now **one base AIP record per fiscal year holding every office**, opened by an Admin, with each office's programs already populated from its LDIP before an encoder arrives.
>
> What changes for this ticket:
>
> * The encoder never creates a record or an office row — both exist by the time they open the page. Entry starts at **sub-office group → project → activity → expenditures**.
> * `workflow_status` lives on `aip_offices`, not `aip_records` (spec §5.1).
> * **Carry Forward Office is gone** ([PPDO-63](https://linear.app/ralphoksiprojects/issue/PPDO-63/remove-carry-forward-office)) — do not build around it.
> * The "no FY2028 AIP yet" empty state becomes "this fiscal year has not been opened yet; an Admin must open it" — the encoder gets **no create action**.
>
> Blocked by [PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates) rather than [PPDO-51](https://linear.app/ralphoksiprojects/issue/PPDO-51/v18-41-aip-programs-sourced-from-a-valid-ldip-closed-list) now.

> ➕ **Scope added 2026-09-07 — this ticket now carries the** `workflow_status` **migration** (spec §2 decision 17, §5.1).
>
> `AIP_Entry_Spec.md` §5.1 filed `AddAipOfficeWorkflowStatus` under [PPDO-59](https://linear.app/ralphoksiprojects/issue/PPDO-59/v18-49-completeness-checklist-before-submit-this-is-the-ceiling-gate). That was wrong ordering: **this** ticket ships the submit action and the read-only state that follows it, and neither can be built against a column that does not exist.
>
> * **Add** `aip_offices.workflow_status NVARCHAR(30) NOT NULL DEFAULT 'Draft'` + `IX_aip_offices_workflow_status (office_id, workflow_status)`. ⚠️ **MIGRATION** — CI does not run it.
> * ⚠️ **Introduce all five states in one migration** — `Draft`, `DepartmentReview`, `SubmittedToPpdo`, `ReturnedByPpdo`, `Consolidated` — and **implement only** `Draft → DepartmentReview`. Phase 4 uses the rest. Adding them piecemeal means a second migration on the same column.
> * ⚠️ **Not** `aip_records.status`, which is `PlanningStatus` (`Draft`/`Final`/`Archived`) and shared with LDIP and WFP. The year's state and one office's review progress are different things at different levels — spec §5.1.
> * [PPDO-59](https://linear.app/ralphoksiprojects/issue/PPDO-59/v18-49-completeness-checklist-before-submit-this-is-the-ceiling-gate) **keeps the *rules*** for when submitting is allowed. This ticket adds the column and the transition; that one gates it.

> ↩️ **Scope WITHDRAWN 2026-09-07, after it was added the same day — there is nothing to build.** Read this before the block below it, which is now superseded.
>
> **This ticket does not renumber program ref codes, so there is no re-link.** AIP program ref codes are inherited verbatim from the LDIP (`RefCode = p.RefCode`) and are never allocated or rewritten in the AIP. The numbering this ticket's own Done-when list describes already exists in `LdipService.BuildHierarchy` — it numbers continuously across groups sharing a ref code and renumbers on removal, because LDIP saves full-replace the hierarchy.
>
> ⚠️ **Consequently one Done-when line is withdrawn:** *"Program numbering runs continuously across groups sharing a ref code, and renumbers on removal without gaps."* Ralph's call, 2026-09-07: **no renumbering; AIP codes keep matching the LDIP's.** Renumbering here would break the correspondence between an AIP program and its LDIP source, which is what makes the closed list mean anything. A gap in a printed sequence is cosmetic; a program that no longer maps back to its source is not. **Deleting a program leaves a gap. Accepted.**
>
> Held in place by `AipProgramGroupTests`. Spec decision 16 superseded, decision 18 added, §10 acceptance and §11 test focus corrected.
>
> ---
>
> ➖ **SUPERSEDED — scope added and withdrawn 2026-09-07 — re-link** `ProgramDivision` **when renumbering** (spec §2 decision 16, §5.4).
>
> [PPDO-65](https://linear.app/ralphoksiprojects/issue/PPDO-65/p3-b-close-the-programdivision-program-half-fk) was raised to convert `ProgramDivision`'s program half to an FK on `aip_programs.id`. **It is cancelled** — that FK would break a documented invariant: the assignment is **permanent across fiscal years** (`ProgramDivision.cs`, [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks)), and `GetProgramAssignmentsAsync` implements it by scoping on office FK with **no fiscal year** and matching `ProgramRefCode`. Since each fiscal year now has its own AIP record, an FK pins the assignment to one year and the next year silently resolves to none.
>
> **⚠️ This ticket is what makes the defect live.** `AipProgram.RefCode` is set at creation and edited by no path today; `DeleteProgramAsync` does not renumber. **“Removing a middle program renumbers without leaving a gap” — in this ticket's own Done-when list — is what starts ref codes changing**, and that is exactly when assignments detach. The failure looks like missing data, not an error: a PPDO encoder simply stops seeing programs that are theirs.
>
> **The rule:** whenever this ticket changes a program's `RefCode`, update the matching `ProgramDivision.ProgramRefCode` rows **in the same transaction**. Not a follow-up write — a renumber that commits without its re-link leaves assignments pointing at codes no program carries, which is the original bug with extra steps.
>
> No migration, no new column. Test it both ways: a renumber preserves the assignment, **and a rolled-back renumber leaves neither half applied**.

> 🔒 **Blocker added 2026-09-07:** [PPDO-64](https://linear.app/ralphoksiprojects/issue/PPDO-64/extract-aipdetailpagetsx-into-components-and-lib-pure-move-no) — extract `aip/detail/page.tsx` first, the extraction ticket §6.4 asked for and that did not exist. ✅ **Shipped as PR #304**, so this may be unblocked already.

**In one line:** the encoder's own tab — build the office's AIP, then submit the whole office's work for department review in one action.

**Plan:** `docs/v1.8/Phase_Plan.md` §5, §12.5, §12.6a · **Spec:** `docs/v1.8/AIP_Entry_Spec.md` §5.1, §6.1 · **Size:** L — **the largest item in Phase 3** · **Blocked by:** [PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates), [PPDO-64](https://linear.app/ralphoksiprojects/issue/PPDO-64/extract-aipdetailpagetsx-into-components-and-lib-pure-move-no), V18-44

## Three stages, not two

The plan originally said two. ✅ **2026-08-26 corrected it to three:** encoders must also create the **sub-office group**. It is entered **with** the program, not as a separate step.

1. **Sub-office group + program** — one mini-form
2. **Project and activity**
3. **Expenditures** against the activity

## ⚠️ Lift `LdipForm.tsx`, do not redesign it

`LdipForm.tsx` ([RAL-61](https://linear.app/ralphoksiprojects/issue/RAL-61/ui-ldip-entry-form-createedit)) **already implements the exact interaction** Ralph described:

> Pick a Sector → see the office-level ref-code preview → set the office/sub-office group name, choosing an existing name from the suggestions to keep adding under that group **or typing a new one to start another** → name the program → enter its budget → Add.

Names are uppercased to match the source files and normalised server-side; **program numbering runs continuously across groups that share a ref code**, and removals renumber without gaps.

Phase_Plan §12.6a says lift it. **Redesigning it is how the two forms end up subtly different**, and the AIP one is the one that prints.

## ⚠️ The sub-office group is NOT the division

They both attach at program level and they are orthogonal. Getting this wrong produces a document that is wrong in a way no test catches:

|  | Sub-office group | Division |
| -- | -- | -- |
| **Stored on** | `AipOffice` — `(Sector, Name)` | `ProgramDivision` |
| **Printed?** | **Yes** — an office row on the form | **No** — never appears on the AIP |
| **Applies to** | every office | **the host office only** |
| **Cardinality** | one group per program | a program may span several divisions |
| **Purpose** | how the document is *structured* | how the work is *divided* |

Real example: three `3000-000-1-01-001` office rows on the province's FY2027 SOCIAL sheet — `OFFICE OF THE GOVERNOR - WARDEN`, `- AKAP-HUB`, `- HOUSING` — each with its own programs and its own shaded subtotal.

## Shape and gating

Shaped like the WFP entry page. **This is the encoder's tab**, and V18-83 splits the sidebar into **AIP Entry** and **AIP Review** as two siblings under Budget Planning — gated separately. Build against that split rather than assuming one AIP page.

## ⚠️ Extract before you build — now its own ticket

`aip/detail/page.tsx` was **1,908 lines — the largest file in the repo** (the 2,057 figure predates Phase 2's trimming), and `docs/v1.8/RETROSPECTIVE.md` says to extract before redesigning. **Extraction and new entry UI in one commit is not reviewable** — the same warning V18-35 carried, for the same reason.

✅ **That extraction is now** [PPDO-64](https://linear.app/ralphoksiprojects/issue/PPDO-64/extract-aipdetailpagetsx-into-components-and-lib-pure-move-no) **and blocks this ticket.** Do not do it here; start from the extracted `components/aip/` and `lib/aip-tree.ts`.

## Done when

* An encoder can build a full FY2028 AIP: sub-office group, program (from the LDIP), project, activity, expenditures
* Sub-office group is entered inside the add-a-program form, with existing names as suggestions
* ~~Program numbering runs continuously across groups sharing a ref code, and renumbers on removal without gaps~~ ↩️ **WITHDRAWN 2026-09-07** — the AIP does not renumber; see the banner above. Replaced by: an AIP program's ref code is byte-for-byte its LDIP program's, and deleting a program leaves the survivors' codes unchanged
* Submit hands the **whole office's** work to department review in one action
* Loading, empty, error, read-only and validation states all render — `docs/DESIGN_SYSTEM.md`, and note that UI states are this project's largest fix category to date

## Out of scope

The *rules* for when submit is allowed — the completeness checklist and ceiling gate (V18-49 / [PPDO-59](https://linear.app/ralphoksiprojects/issue/PPDO-59/v18-49-completeness-checklist-before-submit-this-is-the-ceiling-gate)). **The column and the** `Draft → DepartmentReview` **transition are in scope; the gate on them is not.** Also out: the extraction ([PPDO-64](https://linear.app/ralphoksiprojects/issue/PPDO-64/extract-aipdetailpagetsx-into-components-and-lib-pure-move-no)), which lands first · **any** `ProgramDivision` **schema change** — the re-link above is a write inside the renumber, not a new column · the fund toggle (V18-43) · procurement lines (V18-80) · review and return (Phase 4) · the printable form (Phase 5).
