---
id: PPDO-36
title: 'V18-33 — aip_expenditures child table'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-02
parent: PPDO-32
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-36
uuid: fef957d6-b9b8-47cc-bf77-6e81405829f1
---

# PPDO-36 — V18-33 — aip_expenditures child table

**In one line:** an AIP activity's money is four loose decimals with nowhere to say what it is *for* — add the expenditure-line table that Phase 3 entry and Phase 5's printable form both need.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §5.2 · **Size:** M · **Blocks:** V18-34 · ⚠️ **MIGRATION**

## Why

`AipActivity.Ps/Mooe/Co/Total` record *how much* and nothing about *what*. Phase 3 asks the user to build an activity's cost from line items; Phase 5 prints those lines. Neither is possible against four decimals.

WFP already solved this shape with `WfpExpenditure`. **AIP gets its own table rather than sharing it** (DECISION A, 2026-08-25) — the two documents answer different questions, and one table serving both would serve both badly.

## Scope

New `aip_expenditures`, snake_case per `NAMING_CONVENTIONS.md`, mapped from PascalCase via `IEntityTypeConfiguration`:

| Column | Notes |
| -- | -- |
| `id` | INT IDENTITY |
| `activity_id` | FK → `aip_activities(id)`, cascade delete, **indexed** |
| `account_id` + `account_number_snapshot` + `account_title_snapshot` | See P2-c |
| `funding_source_id` + `funding_source_snapshot` |  |
| `ps` / `mooe` / `co` | **Pesos.** Default 0 |
| `total` | `ps + mooe + co`, computed on write — never read from an input field |
| `created_at` / `updated_at` |  |

Entity, configuration, repository. No endpoint.

## Watch out

* **Keep the snapshot pattern.** It is why a historical AIP survives someone editing the chart of accounts. Copying `WfpExpenditure`'s shape without its snapshots would quietly lose that.
* `total` **is computed on write, not accepted from the client.** A stale or absent input field desyncing from its components is the exact bug [RAL-144](https://linear.app/ralphoksiprojects/issue/RAL-144/bug-aip-activity-total-blankzero-when-source-excel-cell-is-blank) fixed on `AipActivity.Total`.
* Index `activity_id` — every read is "this activity's lines", and V18-34's recompute runs on every write.

## ⚠️ Open: P2-c

Reuse the existing `accounts` config table, or does AIP need its own expense vocabulary? Spec default: **reuse** `accounts` — same chart of accounts, and WFP already snapshots from it. Engineering call, settle it when the ticket starts.

## Out of scope

Any endpoint or UI to write into the table — that is Phase 3. This ticket creates the place for the data to go. Also out: the recompute of parent totals (V18-34).

## Done when

* Table, entity, configuration and repository exist with tests
* `total` cannot be set from outside
* An activity delete cascades its expenditure rows
