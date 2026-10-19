---
id: PPDO-93
title: 'Warn in AIP Entry when adding an activity under an unassigned host-office program'
status: Canceled
project: PPDO Portal
labels: 
milestone: techdebt
created: 2026-09-15
completed: 2026-10-07
parent: (none)
blocked_by: (none)
related: PPDO-150
url: https://linear.app/ralphoksiprojects/issue/PPDO-93
uuid: 658b699a-72b9-47da-a02c-0b6b6a5db91d
---

# PPDO-93 — Warn in AIP Entry when adding an activity under an unassigned host-office program

## Problem

When a PPDO (host-office) program has no division assigned on the Allocation page, amounts entered under its activities **count against no division's allocation**. The only sign today is a server-side `LogWarning` in `AipCeilingService` (`AIP activity reserves against no division…`), which nobody in PPDO will ever see. Each division's remaining ceiling then looks higher than it really is.

Nothing in the UI enforces the order "assign a division first, then encode activities."

## How it came up (2026-09-15)

On the Allocation page, FY2028 shows six PPDO programs as **Unassigned**: `1000-000-1-01-010-019` → `-024` (the gender/GAD program set). They come from the LDIP and are in the FY2028 draft AIP, but not in the uploaded FY2027 AIP, so they've never had a division. Ralph is keeping them: they're unfunded for now, but they may get funding in FY2028. If someone encodes activities under them before assigning a division, those amounts silently skip every division's ceiling.

Local data check: FY2027 AIP = 18 PPDO programs, all assigned. FY2028 AIP = 24, of which 6 are unassigned.

## What already exists

* `AipCeilingService.ResolveAttributionAsync` already detects this exact state and returns `LedgerAttributionKind.HostProgramUnassigned`. It checks the host office from `Office.IsHostOffice` (never from "has divisions" and never from the code "PPDO"), and a program is unassigned when no `ProgramDivision` row matches its ref code for that office.
* `ProgramDivision` assignments are keyed by office + program ref code and are **not per fiscal year**. That's deliberate and must stay that way (see the ref-code key rationale in `AipCeilingService.ResolveDivisionIdAsync`).
* Frontend add-activity entry points: `components/aip/AipAddActivityRow.tsx`, `components/aip/AipProjectRow.tsx`, `app/(portal)/budget-planning/aip/entry/page.tsx`, `app/(portal)/budget-planning/aip/detail/page.tsx`.

## Proposed change (thin slice)

1. Expose a per-program `isUnassignedHostProgram` flag (or equivalent) on the payload AIP Entry already loads for the program tree, computed through the same rule as `ResolveAttributionAsync`. Don't add a second copy of that rule.
2. When the flag is set, show a non-blocking inline notice at the program and on the add-activity row, e.g. *"This program isn't assigned to a division. Its amounts won't count against any division ceiling."*, with a link to Budget Planning → Allocation.
3. Guest offices never show the notice. Division isn't a scoping axis for them (Permission_Matrix §3.1).

## Out of scope

* **Recounting existing activities when a division is later assigned** ("option 2"). Filing separately only if it actually happens. Today, amounts already encoded under a program that gets assigned later aren't recounted until each activity is saved again.
* Blocking activity entry on unassigned programs. The notice is a warning, not a gate.
* Any change to the `ProgramDivision` key or making assignments per fiscal year.

## Acceptance

- [ ] FY2028, PPDO: adding an activity under `…-01-010-019` shows the notice, with a working link to Allocation.
- [ ] FY2028, PPDO: adding an activity under `…-01-010-018` (assigned) shows no notice.
- [ ] A guest office's programs never show the notice.
- [ ] After assigning `-019` on Allocation and reloading AIP Entry, the notice is gone.
- [ ] Backend test: the flag matches `HostProgramUnassigned` for host/assigned, host/unassigned, and guest cases.

## Scheduling

Techdebt. Not to be started while v1.8.0 is in flight. When picked up, it moves into the active version milestone.
