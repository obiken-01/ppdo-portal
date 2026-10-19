---
id: PPDO-181
title: 'Dashboard: readable recent activity (sentences, not table names)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-04
completed: 2026-10-05
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-181
uuid: 1676f2c2-d719-4147-9d71-932bd0d7b97c
---

# PPDO-181 — Dashboard: readable recent activity (sentences, not table names)

From the dashboard design pass, 2026-10-04 (`docs/v1.8/Dashboard_Design_Pass.md`, finding **F5**, backend field **B4**). Ticket 7 of 7. Backend + frontend, size M. Wireframe board 5 (today vs proposed).

## Current behaviour

* Recent activity prints database names: "retag_div on aip_activities #18501", "update on budget_ceilings #13".
* Times show to the second.

## Target behaviour

* **Backend (B4):** each `/budget-planning/activity` entry carries a readable description, or the fields to build one: office code, record label (activity or program name), amount where relevant.
  * Resolve labels set-based (one query per table touched in the page of entries), never per row.
  * Fall back to a generic sentence ("Updated a budget ceiling") when the record is gone.
* **Frontend:** sentences grouped by day (Today, Yesterday, date), times without seconds, a **Full history** link to the audit log.
* **Slim payload:** stays under \~2 KB; no new request.

## Acceptance (local)

- [ ] No table name or `#id` appears in the band for any role.
- [ ] A retag, a ceiling change, a submit and a return each read as a sentence naming the office and the record.
- [ ] A deleted record still shows a sensible sentence.
- [ ] Entries group by day; times are `h:mm a`.
- [ ] An office user sees only their office's entries (unchanged scope).

## Out of scope

Changing what is logged; the audit log page itself.
