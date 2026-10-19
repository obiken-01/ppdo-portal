---
id: PPDO-110
title: 'History feeds: show the AIP ref code instead of the raw database id'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-09-21
completed: 2026-10-07
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-110
uuid: 7b869718-7f19-4f0f-b973-e68fbc55b975
---

# PPDO-110 — History feeds: show the AIP ref code instead of the raw database id

Ralph, 2026-09-21, from live-testing the v1.8.0 build: the dashboard's Recent activity reads
`update on division_allocations #36`. A database id means nothing to a reader. Where the row has a
**ref code** — an AIP program, project or activity — show that instead.

**Scope is deliberately narrow.** Only the three ref-coded AIP tables change. Every other table
(`budget_ceilings`, `division_allocations`, `wfp_*`, `users`, config) keeps `#id` for now, by
Ralph's explicit call — revisit later as its own ticket.

## Current behaviour

Two surfaces render the same `audit_log` rows, and each has its own near-identical helper:

* `frontend/src/app/(portal)/budget-planning/page.tsx:92` — `recordLabel()`, the dashboard feed
* `frontend/src/app/(portal)/config/audit-log/page.tsx:59` — the same three lines again

Both do `#${entry.recordId}`. `AuditLog.RecordId` is not a real FK — it points into whichever table
`TableName` names — so nothing today turns it into something a person recognises.

## Target behaviour

| Given | When | Then |
| -- | -- | -- |
| An audit row on `aip_programs` / `aip_projects` / `aip_activities`, row still exists | The feed renders it | The **ref code** is shown (e.g. `1000-000-1-01-010-001-001`), with the record's name as the title/tooltip |
| The same, row since deleted | The feed renders it | Fall back to the ref code recorded in the audit JSON; if absent, `#id` — **never** blank or an error |
| Any other table | The feed renders it | `#id`, exactly as today |
| An audit row with `RecordGuid` (users) | The feed renders it | Unchanged — first GUID segment, as today |

## How (investigated 2026-09-21)

**Resolve at read time, batched.** The feed returns 10 rows; the Audit Log page is paginated. Group
the page's rows by table, then one `WHERE id IN (…)` per ref-coded table projecting
`(Id, RefCode, Name)` — at most three small queries per page, never a `GetAllAsync` + in-memory
filter (`docs/PERFORMANCE_GUIDELINES.md`).

Read-time resolution is what makes this work **retroactively** for history already written, which a
write-time-only approach would not.

⚠️ **The deleted-row problem is the interesting part.** A DELETE audit row points at an id that no
longer exists, so the lookup misses. Good news, already verified: the **CREATE** payloads already
carry `RefCode` in their JSON —

```
AipService.cs:1075  new { entity.OfficeId, entity.RefCode, entity.Name, entity.FunctionBand }
AipService.cs:1115  new { entity.ProgramId, entity.RefCode, entity.Name }
AipService.cs:1199  new { entity.ProjectId, entity.RefCode, entity.Name, entity.Total }
```

— but the **UPDATE** payloads do not (`AipService.cs:1276, 1315, 1396, 1458, 2091, 2120`). So add
`RefCode` to those update snapshots as part of this ticket. It is a one-field addition per call
site, needs no migration, and makes the fallback reliable for everything written from here on.
Older rows degrade to `#id`, which is today's behaviour and therefore not a regression.

**Do not add a** `record_label` **column to** `audit_log`**.** It was considered: one place instead of many,
survives deletes perfectly. Rejected because it needs a migration, only helps future rows anyway
(so it does not remove the read-time lookup), and duplicates state the ref code already owns.

## Steps

1. **Backend — a shared resolver.** A helper that takes `IReadOnlyList<AuditLog>` and returns
   `id → (RefCode, Name)` for the ref-coded AIP tables. One batched repo query per table. Tests:
   mixed tables in one page, a missing id, an empty page.
2. **Repository methods.** `IAipRepository` gains by-ids projections for programs / projects /
   activities if they do not already exist. Ids and two columns only — no entity graphs.
3. **DTOs.** `RecentActivityDto` and the Audit Log page's entry DTO each gain
   `string? RecordCode` and `string? RecordName`. Nullable — absent means "no ref code, use the id",
   which keeps every existing consumer working.
4. **Wire both readers** — `BudgetPlanningDashboardService.GetRecentActivityAsync` and the audit-log
   list service.
5. ⚠️ **Add** `aip_projects` **to** `BudgetPlanningTableNames` (`BudgetPlanningDashboardService.cs:27`).
   Project writes are audited but are currently filtered out of the dashboard feed entirely, so
   "show the project ref code" is unreachable there without this. Confirm with Ralph that project
   activity *should* appear on the dashboard — it is a small behaviour change beyond relabelling.
6. **Frontend.** Replace both copies of `recordLabel()` with one shared helper; prefer
   `recordCode`, fall back to `#id`. `recordName` becomes the `title`.
7. **Add** `RefCode` **to the six AIP update audit payloads** listed above.

## Risks

Low. Every change is additive and falls back to current behaviour. The one thing to watch is the
per-page query count on the Audit Log page — verify that a page render fires at most three extra
queries, not one per row.

## Out of scope

Every non-AIP table. LDIP (`ldip_records`, `ldip_programs` also carry ref codes — a natural
follow-up, deliberately not bundled). Any change to what is audited, or to `audit_log`'s schema.

## Test focus

* A page mixing ref-coded and plain tables resolves only the former.
* A deleted activity falls back to the JSON ref code, then to `#id` — never blank.
* No N+1: one query per table per page, asserted by mock call count.
