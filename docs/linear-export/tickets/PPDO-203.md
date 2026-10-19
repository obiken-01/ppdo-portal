---
id: PPDO-203
title: 'Office dashboard: division rows still say "In progress" after PPDO accepts the AIP'
status: Done
project: PPDO Portal
labels: frontend, Bug
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-11
completed: 2026-10-11
parent: (none)
blocked_by: (none)
related: PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-203
uuid: 37e1ba30-b0f0-43af-a3e1-9fc5a802814f
---

# PPDO-203 — Office dashboard: division rows still say "In progress" after PPDO accepts the AIP

Found in the [PPDO-195](https://linear.app/ralphoksiprojects/issue/PPDO-195/our-own-uat-test-pass-on-v182-using-pto-as-the-test-office-weekend-10) UAT run, T11 (2026-10-11, v1.8.2).

## Steps

PTO FY2028 AIP accepted by `ppdo.reviewer.test` (status Consolidated). Sign in as `pto_head_test` → Investment Planning dashboard.

## Actual

Header: "FY 2028 AIP · accepted", every stage done. But the **Divisions — FY 2028** table shows CASH and REV with AIP status **"In progress"**.

## Expected

Division rows reflect the office outcome: "Accepted" / "Done" (or whatever label the spec uses for a consolidated office), not "In progress".

## Notes

Check which status the row derives from: probably the division submission status, which the accept does not move, or a mapping that lacks a Consolidated case.
