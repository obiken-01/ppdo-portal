---
id: PPDO-63
title: 'Remove Carry Forward Office'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-05
completed: 2026-09-07
parent: PPDO-48
blocked_by: PPDO-61
related: PPDO-51, PPDO-52, PPDO-62
url: https://linear.app/ralphoksiprojects/issue/PPDO-63
uuid: fc15036b-3cdf-40e0-b173-d2569e1c26eb
---

# PPDO-63 — Remove Carry Forward Office

**In one line:** delete carry-forward — the PDC wants offices building their AIP from scratch.

**Spec:** `docs/v1.8/AIP_Entry_Spec.md` §2 decision 13 · **Size:** S · **Blocked by:** [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal)

## Why it goes rather than lying dormant

Ralph, 2026-09-05: *"we won't need this. I suggested this but the PDC wants the offices to create their AIP from scratch."*

Two further reasons beyond the decision itself:

1. **It bypasses the closed list.** It copies programs from a prior *AIP*, not from the LDIP, so a carried-forward program need not exist in the office's current LDIP — contradicting the rule V18-41 enforces.
2. **It copies a prior year's activity amounts** (`Ps`/`Mooe`/`Co`/`Total`, CC values, funding source). Once V18-45/46 land, that puts money in the ceiling and reservation ledger that nobody entered this year.

⚠️ **It is already unreachable in practice**, which makes removal low-risk: it requires a Manual + Draft target record, and FY2027's record is `Upload`-sourced, so it refuses. FY2028 was refused by V18-37's guard.

⚠️ **Dormancy is the higher risk.** Dead find-or-create paths are the specific hazard in this service — `CopyOfficeFromPriorYearAsync` was **one of the two silent leaks V18-37 found**, writing a legacy-shape record into a year that must not have one, with nothing downstream positioned to notice.

## Scope

Remove the whole path:

* `AipService.CopyOfficeFromPriorYearAsync` + `IAipService`
* `CopyAipOfficeDto`
* `POST /api/budget-planning/aip/copy-office` (`AipCopyOffice` in `AipFunctions`)
* `copyAipOffice` in `frontend/src/lib/aip.ts`
* `CarryForwardOfficePanel` and its button on the AIP detail page
* Its tests, including the V18-37 partition test that guarded it

## Watch out

⚠️ **Read each test before deleting it.** Some of the carry-forward tests pin behaviour that is really about something else — the record conflict guard, ref-code derivation, the office find-or-create. Two tests already needed *replacing* rather than deleting in [PPDO-51](https://linear.app/ralphoksiprojects/issue/PPDO-51/v18-41-aip-programs-sourced-from-a-valid-ldip-closed-list) for exactly this reason. If a test still says something true about code that stays, move it rather than dropping it.

⚠️ Check whether `GetLatestByFiscalYearAsync` is left with no callers once this and [PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates) are done. If so it goes too — but confirm, do not assume.

## Done when

* No route, service method, DTO, client function or UI control for carry-forward remains
* `dotnet test` and `tsc` are clean with no skipped or commented-out tests left behind
* Any test that pinned something still true has been moved, not deleted
* The AIP detail page's action row reads correctly without it

## Out of scope

LDIP seeding ([PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates) turns it into a re-sync) · the year-opening action ([PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates)).
