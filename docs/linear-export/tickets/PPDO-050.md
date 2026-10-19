---
id: PPDO-50
title: 'V18-44 — Make AIP ref-code allocation safe under concurrent creates'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-05
parent: PPDO-48
blocked_by: (none)
related: PPDO-68
url: https://linear.app/ralphoksiprojects/issue/PPDO-50
uuid: 5ebe63fa-90c0-494f-9e19-1797a33034db
---

# PPDO-50 — V18-44 — Make AIP ref-code allocation safe under concurrent creates

**In one line:** two encoders adding a node under the same parent at the same moment both compute the same ref code, and the second gets an unhandled 500 — close that race.

> ⚠️ **Retitled and rescoped 2026-09-04, after reading the code.** This ticket was written from `Phase_Plan.md` §5, and its original framing was wrong in three places: generation **already exists** (`AipService.NextRefCode`, line 1465, called by all three create paths), the sibling queries are **already parent-scoped in SQL**, and **unique indexes already make a duplicate impossible**. See the comment below for the full correction and the implementation prompt. What remains is the read-then-write race between the generator and the index.

**Plan:** `docs/v1.8/Phase_Plan.md` §5, §12.5 · **Size:** M · **Blocks:** V18-42

## The format is pinned to a primary source

DBM **Budget Operations Manual for LGUs, 2023 Ed.**, Figure 4 + Annexes C/D. Confirmed example: `8000-000-1-03-009-001-001-001`.

**Generation is narrower than it looks.** Segments 1–5 are **office identity** — they are not generated at all, they are read from the office. What this ticket allocates is a **sibling-unique** `seq` **per node**. That is the whole job.

## ⚠️ What is actually broken

All three create paths do **load siblings → compute → insert**, with nothing between the read and the write. Two concurrent creates under one parent compute the same code; the first commits, the second violates `UX_aip_activities_project_id_ref_code` and surfaces as an **unhandled** `DbUpdateException` **→ HTTP 500**. Tracker **D5** confirms two-or-more encoders per office is the normal case, so this is a live path.

The index is doing its job. **It is the handling that is missing** — a bounded retry that re-reads siblings and returns a 409 on exhaustion.

## ⚠️ NOT this ticket's bug

`GeneratePRNoAsync`'s full-table-scan-per-create was cited here originally. **AIP does not have it** — `GetProgramsByOfficeIdsAsync` and its siblings already filter on the parent id in SQL. Do not "fix" queries that are already correct.

## Offline clients cannot mint ref codes safely

**Still true, and still worth recording.** This is a **hard constraint on Phase 6**, and it is worth recording in the code where the generator lives rather than only in the plan: a code allocated on a disconnected client cannot be sibling-unique. Phase 6 will need a provisional identity that is replaced on sync, or entry that is online-only.

## ⚠️ Open question, and it blocks a different ticket

Tracker **B9-b**: the *format* is confirmed, but **segment meanings and reset points are still missing**, and the segment count varies with node depth. That does not block this ticket — allocating a sibling-unique `seq` needs neither — but **V18-76 cannot create indexed ref-code columns without a defined maximum depth**. Flagging it here because this is where someone will first want the answer.

## Watch out

⚠️ **Code depth is not hierarchy level.** `RAL-238` established that the AIP importer must read level from the **description column**, not from segment count, precisely because they disagree in the province's real file. A generator that infers a node's level from how many segments its parent has will reproduce the bug [RAL-238](https://linear.app/ralphoksiprojects/issue/RAL-238/aip-import-detect-hierarchy-level-from-the-description-column-not-the) fixed. See the AIP sample-file findings.

## Done when

* Concurrent creates under the same parent **both succeed**, with distinct codes — tested by simulating the conflict, not assumed
* Retry exhaustion returns a **409 Conflict** with the spec §4 wording, never an unhandled exception
* **Only** unique-constraint violations are retried — an FK or NOT NULL failure still surfaces as the bug it is
* The retry **re-reads siblings** on each attempt (a retry over a stale set is a no-op that looks like a fix)
* The unique indexes are untouched — they are the backstop the whole approach depends on
* Spec §2 decision 11 updated in the same PR (`SPEC_STANDARD.md` §3 deviation protocol)
* The offline constraint is recorded where the generator lives

## Out of scope

Indexed ref-code columns (V18-76, blocked on B9-b) · re-coding existing FY≤2027 records · the printed form's use of the code (Phase 5).
