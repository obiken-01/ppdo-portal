---
id: PPDO-75
title: 'V18-58 — In-app notifications: pending count + review queue'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-14
parent: PPDO-67
blocked_by: PPDO-68, PPDO-69
related: PPDO-74, PPDO-120, PPDO-168, PPDO-176
url: https://linear.app/ralphoksiprojects/issue/PPDO-75
uuid: 6b5af9e5-195c-4004-bb92-cb231561dbb4
---

# PPDO-75 — V18-58 — In-app notifications: pending count + review queue

**In one line:** a reviewer needs to know something is waiting for them without being told out of band — a sidebar count and a queue page.

**Plan:** `Phase_Plan.md` §6 · **Size:** M · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec), [PPDO-69](https://linear.app/ralphoksiprojects/issue/PPDO-69/v18-51-the-second-submit-department-head-ppdo)

## Why in-app rather than email

**No email infrastructure exists in this project**, and standing one up is not v1.8.0 scope. Push notification is PWA Phase 3. The in-app queue is a prerequisite for either of those later, so it is the right thing to build regardless of which arrives.

## Scope — settled with Ralph 2026-09-14 (spec §6.5)

* **Sidebar count beside AIP Review = offices waiting on this person.** PPDO reviewer: offices at `SubmittedToPpdo`. Department head: own office at `DepartmentReview`. Both flags: the sum. Offices — not groups, not activities. Only open FY2028+ records. Nothing shown at zero.
* ↩️ **No separate queue page.** Clicking the count opens the AIP Review search with "Only what's waiting on me" applied (department head only: AIP Entry on that year). A third list would drift from the search and the readiness board.
* **Encoder "returned" notice, state-based:** a Returned pill beside AIP Entry and a banner on AIP Entry while the office sits at `ReturnedByPpdo` (encoders + department head), or at `Draft` with `RETURN_DH` as its latest hand-off (encoders only). Clears itself on re-submit — no read tracking, no migration.
* **Freshness:** once per portal load + after the reader's own review actions. No polling.
* **API:** `GET /api/budget-planning/aip/review/notifications` (replaces the planned `pending-count`) — one call carries both counts, their years and the returned notices.

## Watch for

⚠️ **Do not fetch this per component.** The sidebar count is exactly the "fetched once, read everywhere" shape that CLAUDE.md calls out — the WFP page once fired `/auth/me` four times a load. Read it from shared context mounted in the portal layout.

⚠️ **A count query per sidebar render must be a** `CountAsync` **at the database**, not a list fetched and counted in memory (`PERFORMANCE_GUIDELINES.md`). This runs on every portal page for every reviewer.

⚠️ **This overlaps PPDO-78's kanban** — the board answers "who is done" for the whole province, the queue answers "what is waiting on me". Keep them distinct or the kanban becomes a second, drifting queue.
