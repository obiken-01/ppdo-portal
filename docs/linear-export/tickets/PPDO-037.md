---
id: PPDO-37
title: 'V18-34 — Derived, recomputed AIP activity totals'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: PPDO-36
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-37
uuid: 6b02d8bd-30f8-4925-9711-b79488fbd0f9
---

# PPDO-37 — V18-34 — Derived, recomputed AIP activity totals

**In one line:** once expenditure lines exist, an activity's `Ps/Mooe/Co/Total` stop being typed in and become the sum of its lines — recomputed on every write, still stored.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §5.4 · **Size:** M · **Blocked by:** V18-33

## Why

Whiteboard W2's "cost auto generated". With `aip_expenditures` in place, two sources of truth for the same number is a desync waiting to happen — and the activity total is what the ceiling check, the dashboard, the reports and the external API all read.

## Scope

Recompute `Ps`, `Mooe`, `Co` and `Total` on `aip_activities` from its `aip_expenditures` rows, on every insert / update / delete of a child row.

**They stay stored.** Recomputing on read would put a `GROUP BY` under the printable form and the external API — `PERFORMANCE_GUIDELINES.md` is explicit about not doing that to a report path.

## Watch out

⚠️ **FY≤2027 activities have no expenditure rows.** They keep their imported values and **must not be recomputed to zero.** The recompute runs only where a child row exists — otherwise a whole fiscal year is silently wiped, and the symptom is a historical AIP reading ₱0.

* Deleting the **last** expenditure of an activity leaves `Total == 0`, not null. Null meant "never computed" and stops being a state that exists.
* Recompute in the repository, not the service — `AppDbContext` stays out of Application per `CLAUDE.md`.
* Sequential awaits. `DbContext` is not thread-safe; `Task.WhenAll` over two repo calls sharing it is what produced the `GetStatsAsync` production 500.

## Out of scope

The UI that creates expenditure lines (Phase 3). Changing what the ceiling check reads — it already reads the stored total.

## Done when

* Add / edit / delete of a line each recompute the parent, pinned by test
* Three lines sum correctly
* Deleting the last line gives `Total == 0`
* **An FY2027 activity with no lines is left untouched** — the test that matters most
