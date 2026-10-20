---
id: PPDO-33
title: 'V18-32 — AIP ownership FK: AipOffice → offices.id'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-33
uuid: 23d3d94c-f80e-4fdd-a3bb-3ca8db387538
---

# PPDO-33 — V18-32 — AIP ownership FK: AipOffice → offices.id

**In one line:** an AIP office currently knows which office it belongs to only by *string-matching its ref code* — give it a real foreign key, because a review workflow cannot be built on `EndsWith()`.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §5.1 · **Size:** M · **Blocks:** V18-39, V18-40 · ⚠️ **MIGRATION**

## Why

Every ownership question in the codebase today is answered like this:

```csharp
aipOffices.Where(o => o.RefCode.EndsWith(office.OfficeRefCode, OrdinalIgnoreCase))
```

That is the mechanism behind AIP scoping, the dashboard's office rollups, and the office readiness panel. It works by coincidence of code formatting. Phase 4 adds submission and review on top of ownership; string matching is not a foundation for that.

## Scope

1. Add `aip_offices.office_id` (INT **NULL**) → FK `offices(id)`, plus an index.
2. Backfill by the existing suffix rule — this is the last time that rule is used.
3. Switch every scoped read off `RefCode.EndsWith(...)` and onto `OfficeId`.
4. `RefCode` **stays** — it remains the AIP-side re-link key and the record of what the backfill matched on. Same division of labour `ProgramDivision` already uses.

## Watch out

* **Nullable is deliberate.** An office never configured has no match. `NOT NULL` forces the migration to invent an owner or fail — both worse than a reported null. `ProgramDivision.OfficeId` made the same call for the same reason.
* **The backfill must report unmatched rows, never drop them.** Record the count.
* **Test the backfill against a restored copy of production first.** It is only meaningfully testable against real ref codes.
* The FK must win over the suffix rule where they disagree — otherwise the migration achieved nothing. Pin that with a test.

## Out of scope

Removing `RefCode`. The new record shape (V18-40). The scope resolver itself (V18-39) — this ticket gives it the column to resolve on.

## Done when

* No `RefCode.EndsWith` remains on a scoping path (grep it)
* The backfill's unmatched count is recorded
* A ref code that would suffix-match office A but whose `OfficeId` is office B resolves to **B**
