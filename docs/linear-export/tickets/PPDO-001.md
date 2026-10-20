---
id: PPDO-1
title: 'ProgramDivision: replace ref-code string keys with real FKs'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: (none)
related: PPDO-31, PPDO-39, PPDO-48, PPDO-52, PPDO-65, RAL-181
url: https://linear.app/ralphoksiprojects/issue/PPDO-1
uuid: c0e1413d-ffa5-4ba4-b576-1212e3901513
---

# PPDO-1 — ProgramDivision: replace ref-code string keys with real FKs

**V18-11** — plan: `docs/v1.8/Phase_Plan.md` §3.2; raised in `AIP_Requirements_Review.md` §5.2.

`ProgramDivision` (the program → division assignment behind the Allocation page's PPA→Division tab) keys on `OfficeRefCode` **+ **`ProgramRefCode` **strings**, not FKs. Same fragile mechanism as [RAL-181](https://linear.app/ralphoksiprojects/issue/RAL-181/aip-create-an-offices-programs-from-its-ldip-ldip-aip-program-list)/PR #188's Tier 2 ref-code matching.

Today that is untidiness. In Phase 3 it becomes a **correctness risk**: the new AIP entry flow makes program-to-division assignment load-bearing for what a PPDO user can see ("only programs related to their division will be visible"). A ref code that is edited, re-imported with different spacing, or typed inconsistently silently detaches a program from its division — and the failure looks like missing data, not an error.

Scope: add real FK columns, backfill from the current string matches, report any row that fails to match rather than dropping it, then make the FKs the read path. Keep the ref-code columns until the backfill is verified.

⚠️ Do this **before** Phase 3 reads the assignment, not after.

**Done when:** assignment reads and writes go through FKs, the backfill reconciles 100% or reports the exceptions, and a test covers a program whose ref code changes after assignment.

---

## ⚠️ Delivered as half the fix, deliberately — PR #274

The **office side is now a real FK**; the **program side deliberately stays ref-code keyed**, so this ticket's acceptance line — *"a test covers a program whose ref code changes after assignment"* — is **not** met for the program half, and cannot be under the design confirmed 2026-09-01.

**Why.** Assignment is permanent, not per fiscal year (one row serves every FY with the same ref code). `aip_programs` rows have no identity that survives a fiscal year or a re-upload — `AipService.ReplaceImportAsync` deletes the record's top-level `AipOffice` rows and lets the cascade wipe the subtree, so every re-upload issues fresh surrogate IDs. An FK to `aip_programs.id` would therefore either pin each assignment to one fiscal year, contradicting that rule, or detach it on the next FY≤2027 re-upload (FY2027 stays on the old format and stays re-uploadable).

So: an **office** ref-code change no longer detaches an assignment (covered by test); a **program** ref-code change still does. `ProgramDivision`'s class remarks carry the reasoning with an explicit "do not finish the job by adding one".

**Follow-up worth filing:** closing the program half needs a durable program key carried across re-uploads — a larger design than this ticket.
