---
id: PPDO-39
title: 'V18-40 — New AIP record shape: office-owned, LDIP-like'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: PPDO-33
related: PPDO-1, PPDO-31
url: https://linear.app/ralphoksiprojects/issue/PPDO-39
uuid: 7f06e00f-54f5-44db-b76d-fbf1c93f24a5
---

# PPDO-39 — V18-40 — New AIP record shape: office-owned, LDIP-like

**In one line:** today one AIP record spans every office; from FY2028 each office owns its own record — and PPDO is just another office, with division of work carried on the program, not the record.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §2 decision 4 · **Size:** L · **Blocked by:** V18-32 · **Blocks:** V18-37

## Why

An `AipRecord` currently has no owner — it holds `AipOffice` children for every office in the province. That is why there is no ownership FK to scope on, why one office cannot submit independently of another, and why review (Phase 4) has nothing to attach to. The new shape is office-owned, like `LdipRecord` already is.

## The part that was nearly built wrong

✅ **Settled 2026-08-26 (tracker B12-b): PPDO gets an *ordinary* office record.**

* **No** per-division AIP records
* **No** division column on `AipOffice`
* Divisions **never print** on the AIP form
* Division of work is carried on the **program**, via the existing `ProgramDivision` map — **exactly as WFP does**

The tempting alternative — give PPDO one record per division — would make PPDO structurally different from all 18 other offices, and every downstream feature would then need two code paths forever.

A sub-unit that genuinely *does* print is an `AipOffice` row **sharing the office ref code**, distinguished by `(Sector, Name)`. That is already built and is how the province actually encodes (Phase_Plan §12.6).

## Scope

The FY≥2028 record shape: office-owned, one record per office per fiscal year, with `AipOffice` rows beneath it as today.

## Watch out

* **Do not add a division column to** `AipOffice`**.** It is the first thing this shape looks like it wants and it is explicitly ruled out.
* FY≤2027 records keep the old multi-office shape untouched — no migration between them (V18-37 gates it).
* `ProgramDivision` keeps its ref-code keying. Deliberate and documented ([RAL-249](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks)) — do not "finish the job" with an FK to `aip_programs.Id`.
* PPDO's visibility now rests **solely** on `ProgramDivision` (tracker B12-b). Worth knowing that [PPDO-31](https://linear.app/ralphoksiprojects/issue/PPDO-31/enforce-1-program-1-division-on-ppa-assignment) (1 program : 1 division) is deferred, so a shared program is still possible.

## Out of scope

The FY gate mechanism (V18-37). Entry UI (Phase 3). Submission and review (Phase 4).

## Done when

* An FY2028 record belongs to exactly one office
* PPDO's record is structurally identical to a guest office's — no special case anywhere
* No division column exists on `AipOffice`
* FY2027 records open unchanged
