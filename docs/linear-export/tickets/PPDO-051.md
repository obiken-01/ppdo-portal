---
id: PPDO-51
title: 'V18-41 — AIP programs sourced from a valid LDIP (closed list)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-05
parent: PPDO-48
blocked_by: PPDO-61
related: PPDO-60, PPDO-62, PPDO-63
url: https://linear.app/ralphoksiprojects/issue/PPDO-51
uuid: acb58f45-0523-4a55-988c-2bc9da76a9b7
---

# PPDO-51 — V18-41 — AIP programs sourced from a valid LDIP (closed list)

> ↩️ **SUPERSEDED IN PART — 2026-09-05. Do not merge PR #298 as it stands.**
>
> The closed-list rule this ticket enforces **stands**, and so does its server guard (`AddProgramAsync` refusing a free-typed name). What is superseded is the *office-owned seeding* it built: Ralph settled the record model as **one base record per fiscal year holding every office**, so there is no office-owned record to seed into. See `AIP_Foundation_Spec.md` §2 decision 4.
>
> [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal) reverses the shape; [PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates) takes over program population, which now happens when an Admin opens the fiscal year rather than through a Seed button. PR #298 should be **amended** on top of [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal), keeping the closed-list guard and its tests and dropping the office-owned find-or-create.
>
> The `AipOffice.OfficeId` fix in that PR is still correct and still needed — it matters *more* now, since after [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal) that column is the only carrier of office identity ([PPDO-60](https://linear.app/ralphoksiprojects/issue/PPDO-60/aipofficeofficeid-is-never-set-outside-the-importer-manually-added)).

**In one line:** an office's AIP programs come from its LDIP and nowhere else — the LDIP is a **closed list**.

**Plan:** `docs/v1.8/Phase_Plan.md` §5 · **Size:** S · **Blocks:** V18-42

## The decision that makes this small

✅ **Open question #5, answered 2026-08-25: the LDIP is a closed list.** An office **cannot** add a program outside it. That removes an entire branch the plan originally anticipated — there is **no "propose a new program" path and no approval flow for one**. Anyone reading this ticket expecting to build that has the wrong version.

## Scope

Reuse `seedAipProgramsFromLdip` ([RAL-181](https://linear.app/ralphoksiprojects/issue/RAL-181/aip-create-an-offices-programs-from-its-ldip-ldip-aip-program-list)) rather than writing a second path. It already seeds an office's AIP programs as bare shells — name and ref code only — from that office's LDIP for a given sector.

What changes is that this becomes the **only** way programs enter an FY2028 AIP, and the entry UI has to present it that way: pick from the LDIP, not type a name.

## ⚠️ Note the shape change underneath

`SeedProgramsFromLdipAsync` is one of the four paths V18-37 gated, and it **find-or-creates** its target record. Under the office-owned shape it now refuses FY2028 unless the record is office-owned — which is correct, and means this ticket works with the new shape rather than around it. Do not loosen that guard to make seeding easier.

## Watch out

⚠️ **Seeding happens without the office touching anything**, which is why V18-82's kanban decides Not Started vs In Progress on **activity count, not seeded programs**. If this ticket changes when or how seeding fires, that denominator moves with it.

⚠️ The LDIP→AIP match is a **ref-code suffix match**, already implemented in `AllocationService.GetProgramAssignmentsAsync`. Do not re-derive it.

## Done when

* An FY2028 office can add programs only from its own LDIP
* There is no path to create a program with a free-typed name
* An office whose LDIP is empty gets a clear empty state naming the LDIP as the prerequisite — not a blank picker
* `seedAipProgramsFromLdip` is reused, not duplicated

## Out of scope

Editing the LDIP itself · a program-proposal or approval flow (does not exist by decision) · the sub-office group, which is V18-42 and is entered *with* the program.
