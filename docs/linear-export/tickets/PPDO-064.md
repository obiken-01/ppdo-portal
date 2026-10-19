---
id: PPDO-64
title: 'Extract aip/detail/page.tsx into components and lib — pure move, no behaviour change'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-07
completed: 2026-09-07
parent: PPDO-48
blocked_by: (none)
related: PPDO-34, PPDO-61, PPDO-74, PPDO-120
url: https://linear.app/ralphoksiprojects/issue/PPDO-64
uuid: 257662b4-beeb-4d7f-a450-6b7a73a4b15b
---

# PPDO-64 — Extract aip/detail/page.tsx into components and lib — pure move, no behaviour change

**In one line:** split the repo's largest file into reusable pieces **before** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) builds the entry UI — a pure move with zero behaviour change, so the entry-UI diff is reviewable.

**Spec:** `docs/v1.8/AIP_Entry_Spec.md` §6.4 · **Size:** M · **Blocks:** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) · **Blocked by:** —

## Why this ticket exists

`docs/v1.8/AIP_Entry_Spec.md` §6.4 and [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)'s own body both say *"extraction and new entry UI in one commit is not reviewable"* — the same warning V18-35 carried. **Neither created a ticket to extract into.** This is it.

The file is **1,908 lines** today (the spec's 2,057 predates Phase 2's trimming) and is still the largest in the repo.

**§6.4 offers two paths — this ticket takes the first, deliberately.** The alternative ("build Entry as a genuinely separate page and leave detail alone") duplicates three things that must not be duplicated:

* the **₱000 ↔ pesos conversion** (V18-35 / [PPDO-34](https://linear.app/ralphoksiprojects/issue/PPDO-34/v18-35-aip-units-thousands-pesos-every-fiscal-year)), whose own comment says *"both directions or neither"* — a second copy is exactly how one direction drifts
* the **twelve immutable tree mutators** that keep the nested record consistent after an inline edit
* the table cells and input classes, whose drift shows up on **the document that prints**

## 1. Context docs to read

Read `CLAUDE.md` and `PROJECT_DOCUMENTATION_NET_AZURE.md`.
Read `docs/v1.8/AIP_Entry_Spec.md` §6.4 FULLY — it is the authority for this ticket.
Read `docs/DESIGN_SYSTEM.md` — **this ticket restyles nothing**; it is the reference for confirming nothing changed.
Read `docs/v1.8/RETROSPECTIVE.md` — the extract-before-redesign finding this ticket acts on.

## 2. Files to read before writing code

* `frontend/src/app/(portal)/budget-planning/aip/detail/page.tsx` — the whole file, all 1,908 lines. There is no shortcut; the extraction boundaries are only visible end to end.
* `frontend/src/components/wfp/WfpProcurementItemTable.tsx` (558 lines) and `WfpFrequencyGrid.tsx` (217) — **the precedent.** `components/wfp/` is how this project already extracts a heavy budget-planning page; match its shape and its props style rather than inventing one.
* `frontend/src/lib/aip.ts` and `frontend/src/lib/aipConstants.ts` — where AIP client functions and constants already live, and therefore where the extracted pure helpers belong.
* `frontend/src/components/ui/MoneyInput.tsx` and `ConfirmDialog.tsx` — already-shared primitives the rows consume. Extracted components must keep consuming these, not grow local copies.

## 3. Current behaviour → target behaviour

**Current:** one 1,908-line client component holds the units conversion, twelve tree mutators, four presentational cells, nine row/panel components, the sector grouping helpers and the page itself.

**Target:** identical rendered page and identical network calls, with those pieces in `lib/` and `components/aip/`, and `page.tsx` reduced to roughly the last section — data fetching, sector tabs, expand/collapse state and composition.

> State today's behaviour of `toDisplayUnits`/`toStorageUnits` in one sentence as output before touching anything. An implementer who cannot is about to break the amounts.

## 4. Working branch + PR target

Working branch: `release/1.8.0`. Create `feature/v1.8.0-ppdo-64-extract-aip-detail-page` off `release/1.8.0` and open the PR against `release/1.8.0` **(NOT** `main`**)**.

## 5. TDD

**Not applicable — and that is the risk.** The frontend has no test runner (`frontend/package.json` has no `test` script, no jest, no vitest). There is no net under this change. Verification is therefore `tsc` + `eslint` + **live browser comparison**, and step 8 is not optional.

## 6. Implementation steps

Each step compiles and the page keeps working. Commit per step — a reviewer must be able to see each move in isolation.

1. `frontend/src/lib/aip-units.ts` — move `PESOS_PER_DISPLAY_UNIT`, `toDisplayUnits`, `toStorageUnits`, `fmt` (lines 62–87). ⚠️ **Move the whole V18-35 comment block with them.** It is the only place the "both directions or neither" rule is written down.
   *Verify:* `npx tsc --noEmit`; open an activity for edit, confirm the figure shown matches before and that saving it unchanged does not multiply or divide the record by 1,000.
2. `frontend/src/lib/aip-tree.ts` — move `sumActivities`, the twelve `replace`/`remove`/`add` mutators for office/program/project/activity (lines 88–228), plus `SECTOR_ORDER`, `groupBySector`, `toggleSet`, `allCollapsed` (1533–1563). Pure functions, no React import.
   *Verify:* `npx tsc --noEmit`; add, inline-edit and delete one activity, one project, one program — the tree updates in place without a refetch, exactly as now.
3. `frontend/src/components/aip/AipTreeCells.tsx` — `Chevron`, `StatusBadge`, `TH`, `AmtTD`, and the `selectCls` / `inputCls` strings (49–61, 229–272). Export the two class strings; **do not** convert them to a variant helper — that is a redesign.
   *Verify:* `npx tsc --noEmit`; screenshot the collapsed detail page and diff it against the same view on `release/1.8.0`.
4. `frontend/src/components/aip/AipTreeActions.ts` — the `TreeActions` interface (489–514) with its V18-41 doc comment intact. It is the contract every row consumes and the one [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) will read first.
5. **Row components, one file each, one commit each** — `AipActivityRow.tsx` (274–488), `AipAddActivityRow.tsx` (515–658), `AipProjectRow.tsx` (659–783), `AipAddProjectRow.tsx` (784–836), `AipProgramRow.tsx` (837–959), `AipAddProgramRow.tsx` (960–1039), `AipOfficeRow.tsx` (1040–1175).
   *Verify after each:* `npx tsc --noEmit` and exercise that level's add / edit / delete in the browser.
6. **Panels** — `AipAddOfficePanel.tsx` (1176–1302) and `AipSeedFromLdipPanel.tsx` (1303–1532).
   ⚠️ **Check reachability first, and report it — do not act on it.** [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal)/62 made LDIP seeding a *re-sync* into an already-opened year and removed the encoder's create path. If either panel is now dead or partly dead for FY2028+, **say so in the PR body and leave the code alone.** Deleting it belongs in its own ticket; a pure move that also removes a feature is not a pure move.
7. `page.tsx` keeps only `AipDetailPage` (1564–1908) plus imports. Expect roughly 350 lines.
   *Verify:* `npx tsc --noEmit && npx eslint .` clean.
8. **Live comparison against** `release/1.8.0` — the real acceptance step, since no test covers this page. Same record, same tab, side by side: collapsed view, one office expanded, one program, one project, an activity in edit mode, the confirm dialog, and the footer count line.

## 7. Risks, rollback, sign-off

| Risk | Detail | Rollback |
| -- | -- | -- |
| **Amounts silently off by 1,000** | The single highest-consequence failure here. Moving `toDisplayUnits` without `toStorageUnits`, or letting an extracted row import one and inline the other, corrupts stored pesos on the next save — and it looks plausible on screen | Revert the commit. **No migration runs**, so a revert is a complete rollback — but data already saved wrong is not undone by it. Check a saved figure in SQL before merging |
| **Silent restyle** | An extracted row that reformats `className` strings changes the printed document | Revert that step's commit |
| **Prop-drilling rewrite creeping in** | Converting `TreeActions` to context, or props to a reducer, during the move | Out of scope — see below |

**No sign-off gate applies:** this touches no auth, no `PermissionService`, no office/division scope, no migration, and nothing in `components/ui/`. That is precisely why it is a good standalone ticket.

**⚠️ Manual-implementation candidate** (`CLAUDE.md`): **yes, unusually so.** Reversible, no migration, no permission logic, `tsc` gives immediate feedback, and `components/wfp/` is a sibling to pattern-match. Its weakness as a candidate is the missing test net — the units rule is the one place a wrong move compiles cleanly.

## 8. Out of scope — do NOT

* **Any behaviour, styling, copy, or network-call change.** If something looks like a bug during the move, file it; do not fix it here.
* **Deleting** `AddOfficePanel` **or** `SeedFromLdipPanel` even if step 6 finds them unreachable — report only.
* **Rewriting state management** — no context, no reducer, no react-query. `TreeActions` stays a prop.
* **Building anything for** [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity) — no new entry page, no three-stage form, no `workflow_status`.
* **Touching** `aip/new`**,** `aip/page.tsx` **or** `aip/import-preview` — they import from `lib/aip.ts`, which does not move.

## 9. Commit message

Per step, e.g.:

```
refactor(aip): extract AIP units conversion into lib/aip-units (PPDO-64)
refactor(aip): extract AIP tree mutators into lib/aip-tree (PPDO-64)
refactor(aip): extract AIP row components into components/aip (PPDO-64)
```

## Done when

* `aip/detail/page.tsx` is ~350 lines and holds only the page
* `npx tsc --noEmit` and `npx eslint .` are clean
* The detail page renders and behaves **identically** to `release/1.8.0` across the step-8 checklist
* No file under `components/ui/`, `lib/aip.ts` or the backend changed
* The PR body records what step 6 found about the two panels
