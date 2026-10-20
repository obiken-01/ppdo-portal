---
id: PPDO-95
title: 'AIP add-programs picker offers programs the group already has'
status: Done
project: PPDO Portal
labels: 
milestone: techdebt
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-89, PPDO-101
url: https://linear.app/ralphoksiprojects/issue/PPDO-95
uuid: e28b2b82-fba1-4dbc-b6f9-9d67f6b63707
---

# PPDO-95 — AIP add-programs picker offers programs the group already has

**In one line:** the AIP Entry add-programs picker listed every program in the office's LDIP group, including the ones already in the AIP — which the add endpoint then refuses.

**Found:** 2026-09-16, while hiding the "+ Add programs" control during [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout) · **Size:** S

## The defect

`AipService.GetAddableProgramsAsync` returned every program in the resolved LDIP group. `AddProgramsWithGroupAsync` *does* reject the ones already present ("These programs are already in this group: …"), so **no duplicate** `aip_programs` **row could ever be created** — but the panel rendered them as tickable checkboxes, so an encoder could select one and only learn it was impossible after pressing Add.

## Fix

`GetAddableProgramsAsync` now takes the `aipRecordId` (required — an optional one could only be answered by returning the misleading list) and flags each program with `AlreadyAdded`.

⚠️ **The flag is scoped to the ONE group, resolved by** `(RefCode, Name)`**, exactly as the write path's collision check is** — never across the office. Two sub-office blocks under one office are separate rows on the AIP form and may each legitimately carry the same LDIP program. Stricter than the write path and the picker hides a valid choice; looser and it offers one that gets refused, which is this bug.

**Flagged, not filtered.** An already-added program stays in the list, rendered unselectable with "· already in this group". Dropping it would make a program vanish from its own LDIP group, which reads as missing data and sends the encoder to check the LDIP — the failure mode the entry page's division-filter notice exists to prevent.

## Changed

* `AipService.GetAddableProgramsAsync` — record lookup (404 on unknown), group-scoped `AlreadyAdded`, one `GetProgramsByOfficeIdsAsync` for all matched groups rather than one per group
* `AipAddableProgramDto` — gains `AlreadyAdded`
* `GET /api/budget-planning/aip/addable-programs` — `aipRecordId` now required, 400 without it
* `lib/aip.ts`, `types/budget-planning.ts`, `AipAddProgramsPanel.tsx`
* `AipProgramGroupTests` — 5 new cases: flags a program the group carries; keeps it in the list; does **not** flag one another group carries; flags nothing when the AIP has no block yet; unknown record → NotFound

## Follow-up

Unblocks re-enabling `SHOW_ADD_PROGRAMS_WHEN_POPULATED` in `frontend/src/app/(portal)/budget-planning/aip/entry/page.tsx`, which [PPDO-89](https://linear.app/ralphoksiprojects/issue/PPDO-89/aip-entry-wfp-style-program-project-activity-drill-down-layout) set to `false` partly because of this. **Flipping it back is a separate decision** — the other reason it is off is that the control competed with the drill-down picker.

> Move out of `techdebt` into the active version milestone when this goes In Progress (CLAUDE.md).
