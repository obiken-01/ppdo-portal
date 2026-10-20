---
id: PPDO-109
title: 'Per-office fund sources: shared province-wide list plus an office''s own (⚠️ MIGRATION)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-20
completed: 2026-09-21
parent: (none)
blocked_by: PPDO-107
related: PPDO-111, PPDO-128
url: https://linear.app/ralphoksiprojects/issue/PPDO-109
uuid: 6dabdd7f-9eaf-4201-97fc-7b5e4c064100
---

# PPDO-109 — Per-office fund sources: shared province-wide list plus an office's own (⚠️ MIGRATION)

Spec: `docs/v1.8/Office_Setup_Spec.md` (C4). From the 2026-09-15 demo item 7; model chosen by Ralph 2026-09-17 after a pros/cons comparison.

**Blocked by** [PPDO-107](https://linear.app/ralphoksiprojects/issue/PPDO-107/new-grant-canmanageofficesetup-a-department-head-sets-up-their-own) (the `CanManageOfficeSetup` grant).

## Goal

A department head adds fund sources for their own office, while the province-wide funds (GF, 20% DF, GAD, LDRRMF, SEF) stay PPDO's and read-only to offices.

## Decisions

* **D5 (Option 2):** `funding_sources.office_id` nullable. Null = province-wide, PPDO-owned. Not per-office copies of the standard funds — that would duplicate the General Fund and split province-wide totals.
* **D6:** fund codes stay **globally unique**; `IX_funding_sources_code` is unchanged. An office cannot reuse `GF`, and two offices cannot give one code different meanings.
* **D7:** `GetGeneralFundIdAsync` resolves **only** shared rows, so the ceiling arithmetic keeps one canonical General Fund.
* **D11:** office funds carry no ceiling check.

## Scope

* ⚠️ **MIGRATION** `AddFundingSourceOfficeId`: nullable `office_id` FK → `offices.id`, `NoAction`, **no backfill** — every existing row stays shared, i.e. today's behaviour. CI does not run migrations; apply by hand to Azure SQL at release.
* `GET|POST|PUT|DELETE /api/config/funding-sources[/{id}]` accept `CanManageConfig` **or** `CanManageOfficeSetup`.
  * Reads return shared rows **plus** the caller's office's rows; each row carries `isShared` so the UI renders shared ones read-only.
  * A `CanManageOfficeSetup` caller writing a shared row → 403.
  * Creates from such a caller are stamped with their office id; a body office id is ignored.
  * Duplicate code → 409, as today.
* The WFP and AIP expenditure fund pickers show shared + that **record's office's** funds — resolve from the record, not from `me.officeCode`.
* Delete blocked when WFP/AIP lines use the fund, with the count.
* \~11 services read `FundingSource` (`AipService`, `WfpService`, `AllocationService`, `ExternalAipReadService`, …) — audit each for a read that must stay shared-only.

## Tests

* `GetGeneralFundIdAsync` ignores an office row coded `GF` if one is ever forced in.
* Picker returns shared + own office only; another office's funds are absent.
* Partner API totals by funding source unchanged for existing data.

## Out of scope

Per-office General Fund. Any change to `generalFundId`. Ceilings for office funds.
