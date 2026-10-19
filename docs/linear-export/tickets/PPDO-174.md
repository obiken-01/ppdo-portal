---
id: PPDO-174
title: 'Demo 2.15 — Refuse creating an investment proposal for a project in an archived AIP record'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-10-01
completed: 2026-10-01
parent: PPDO-153
blocked_by: (none)
related: PPDO-155
url: https://linear.app/ralphoksiprojects/issue/PPDO-174
uuid: 6d190f6e-4d8d-419e-99c1-d0727f372748
---

# PPDO-174 — Demo 2.15 — Refuse creating an investment proposal for a project in an archived AIP record

Decided by Ralph 2026-10-01 (open question from [PPDO-155](https://linear.app/ralphoksiprojects/issue/PPDO-155/demo-215-t2-investment-proposal-service-api-lifecycle-scope)): **yes, refuse it.**

`POST /api/budget-planning/proposals` currently accepts any FY2028+ project in scope, including one whose AIP record is **Archived** (superseded). The list and the project picker already read only the non-archived record (`GetLatestByFiscalYearAsync`), so the only way in is a direct project id — but a proposal on a superseded record would print figures nobody will approve.

**Change:** `InvestmentProposalService.CreateAsync` answers **409** "This project is in an archived AIP. Create the proposal from the current FY {year} AIP." when `AipRecord.Status == Archived`. Checked after scope (so an out-of-scope id still answers 404 and existence is never confirmed) and after the FY2027 rule.

**Not changed:** reading, editing, exporting or deleting a proposal that already exists on a record later archived. Ralph asked about create only.

TDD: a service test archiving the fixture record → 409 with the message; red-test by removing the guard.
