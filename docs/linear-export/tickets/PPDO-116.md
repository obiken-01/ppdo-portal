---
id: PPDO-116
title: 'V18-71 — Concurrent-edit guard for AIP entry (epic)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.4 — Minor fixes
created: 2026-09-22
completed: 2026-10-14
parent: (none)
blocked_by: (none)
related: PPDO-92, PPDO-115, PPDO-202
url: https://linear.app/ralphoksiprojects/issue/PPDO-116
uuid: 9fbaf811-11d3-4c5e-b1ae-a41ba19d0fb3
---

# PPDO-116 — V18-71 — Concurrent-edit guard for AIP entry (epic)

Epic for **V18-71** ([Phase_Plan.md](<../../docs/v1.8/Phase_Plan.md>) §8). Five children, below.

## Why this is not hardening

Two or more encoders per office is confirmed normal (tracker D5). Today, when two of them have the same AIP activity open and both save, **the second save silently overwrites the first.** Nobody is told, and the only trace is an audit row nobody reads.

The Phase Plan promoted this out of the hardening bucket for exactly that reason: it is the one Phase 7 item that costs real work rather than polish.

## Spec

`docs/v1.8/AIP_Concurrent_Edit_Spec.md` — written 2026-09-22, PR [obiken-01/ppdo-portal#366](https://linear.app/ralphoksiprojects/review/docsaip-spec-the-concurrent-edit-guard-v18-71-7be9a9a37b06). Read it FULLY before any child ticket. It satisfies [SPEC_STANDARD.md](<../../docs/SPEC_STANDARD.md>).

## ✅ Decisions settled 2026-09-22 — unblocked

Ralph: *"go with soft warning, optimistic concurrency"*.

|  | Decision | Status |
| -- | -- | -- |
| 🅐 | Optimistic concurrency, **not** a lock | ✅ **confirmed explicitly** |
| 🅑 | The **activity row** is the unit of conflict, not the record | ✅ adopted with 🅐 |
| 🅒 | The loser keeps their input; explicit Overwrite / Discard | ✅ adopted with 🅐 |
| 🅓 | AIP Entry only; WFP excluded | ✅ adopted with 🅐 |

🅑, 🅒 and 🅓 were presented alongside 🅐 as defaults and adopted with it rather than each being answered separately. All three stay cheap to change until [PPDO-118](<https://linear.app/ralphoksiprojects/issue/PPDO-118>) starts, and the spec still records what each would cost to reverse.

> ⚠️ **"Soft warning" does not mean the save goes through** — worth pinning, because the phrase has a second reading that would leave the bug in place. **Soft** describes the *locking model*: nothing is reserved, no lease is held, an abandoned tab blocks nobody, and the user may choose to overwrite. **The conflicting save is still rejected** — 409, nothing written. A save that completed with a warning attached would still destroy the first encoder's work, which is the whole defect.

## Approach in one paragraph

Optimistic concurrency on a SQL Server `rowversion`. Every save carries the version the editor loaded; a mismatch is rejected with **409** carrying who changed it, when, and the current values. The encoder keeps their input on screen and chooses **Overwrite** or **Discard mine and reload**.

`rowversion` was chosen over comparing `UpdatedAt` because it **cannot be forgotten** — the database maintains it and EF's `.IsRowVersion()` puts it in the `WHERE` clause automatically. Comparing `UpdatedAt` would put the burden on every write path to remember to set it, across \~240 endpoints, in a codebase that has twice shipped a rule one call site missed ([PPDO-92](<https://linear.app/ralphoksiprojects/issue/PPDO-92>)'s three-place route gate, [PPDO-115](<https://linear.app/ralphoksiprojects/issue/PPDO-115>)'s reset path). `UpdatedAt`/`UpdatedById` are still added — for the *message*, not the detection.

## Three traps, each of which ships looking finished

1. **The version check runs AFTER authorization.** Put it first and the 409 becomes an oracle for whether a record exists and recently changed.
2. **A missing** `rowVersion` **is a 400, not a pass.** "Treat absent as no conflict so old clients keep working" makes the feature opt-out by omission. The staged rollout handles old clients instead — and **stopping after child 4 leaves the schema and the UI in place with none of the protection.** Child 5 is a real ticket.
3. **Row grain is the activity.** A warning people learn to click through is worse than no warning.

## Children, in order

| \# | Ticket | Deploy |
| -- | -- | -- |
| 1 | Migration + entity config | — |
| 2 | Service layer: conflict detection and payload | — |
| 3 | Endpoints: accept `rowVersion` (optional), map 409 | **Deploy 1** |
| 4 | Frontend: hold version, send it, conflict panel | **Deploy 2** |
| 5 | Flip to required | **Deploy 3** |

Child 2 carries the real logic and is where the tests live.

## Milestone

> **Moved 2026-10-07 (Ralph):** this epic and [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) are now in **v1.8.3 — Concurrent-edit guard: turn it on**, because [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) can only ship after v1.8.2 is deployed and the warning has stayed at zero. The shipped children ([PPDO-117](https://linear.app/ralphoksiprojects/issue/PPDO-117/v18-711-concurrency-columns-on-aip-activities-and-aip-expenditures)…120, [PPDO-191](https://linear.app/ralphoksiprojects/issue/PPDO-191/v18-714b-wire-rowversion-into-aip-entry-and-the-wfp-is-creation-toggle), [PPDO-193](https://linear.app/ralphoksiprojects/issue/PPDO-193/v18-714c-activity-delete-checks-rowversion-409-on-a-stale-delete)) are in **v1.8.2**, where their code ships. The notes below are the earlier state, kept for history.

**Scheduled 2026-10-04 (Ralph): moved to v1.8.2.** [PPDO-116](https://linear.app/ralphoksiprojects/issue/PPDO-116/v18-71-concurrent-edit-guard-for-aip-entry-epic) and [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) are in the v1.8.2 milestone. [PPDO-117](https://linear.app/ralphoksiprojects/issue/PPDO-117/v18-711-concurrency-columns-on-aip-activities-and-aip-expenditures)…120 are Done and stay in `Backlog — Unscheduled`, so their finished work keeps its current milestone.

Branch off `release/1.8.2` per the standing convention, never off `uat`.

⚠️ v1.8.2 will carry the `AddAipConcurrencyTokens` migration. CI does not run migrations, so it needs a manual `dotnet ef database update` at deploy.

⚠️ [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) is blocked: three files still send no `rowVersion` (`AipActivityFields.tsx`, `AipExpenditureTable.tsx`, `wfp/entry/page.tsx`) and must be wired first. See [PPDO-121](https://linear.app/ralphoksiprojects/issue/PPDO-121/v18-715-make-rowversion-required-deploy-3-the-step-that-turns-the) §4a. The App Insights evidence window (§4b) can only start after that wiring ships.
