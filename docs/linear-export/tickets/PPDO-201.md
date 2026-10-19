---
id: PPDO-201
title: 'AIP expenditure line: server accepts an amount in a column the account''s class does not allow (PS on a MOOE account)'
status: Done
project: PPDO Portal
labels: backend, Bug
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-11
completed: 2026-10-11
parent: (none)
blocked_by: (none)
related: PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-201
uuid: faca896f-7e8a-4b80-8f0a-1da8e7c0780e
---

# PPDO-201 — AIP expenditure line: server accepts an amount in a column the account's class does not allow (PS on a MOOE account)

Found in the [PPDO-195](https://linear.app/ralphoksiprojects/issue/PPDO-195/our-own-uat-test-pass-on-v182-using-pto-as-the-test-office-weekend-10) UAT run (2026-10-11, v1.8.2).

## Steps

1. AIP Entry, FY2028, as `pto.encoder2.test`: activity "Test — collection drive", fund GF.
2. Add Account → `5-02-99-990 · Other Maintenance and Operating Expenses — MOOE`.
3. The UI disables PS and CO and opens only MOOE. A value was written into the (disabled) **PS** input by browser automation and saved.

## Actual

Save succeeded: the line stored **PS 250.50** on a MOOE account. Encoded (MOOE + CO) stayed at 350, because PS is ceiling-exempt, so the amount silently escaped the ceiling check.

## Expected

The server rejects (400) an amount in a column that does not match the account's class: MOOE account → MOOE only, CO account → CO only, PS → PS only. The UI lock is not a guard.

## Why it matters

PS is ceiling-exempt, so a crafted request (or a future UI bug) could park MOOE/CO spending in PS and bypass the ceiling.

## Fix

Validator on add/update expenditure line: the account's class determines the only non-zero column. Add tests for each class, red-tested.

Corrected in UAT data during the run (line now MOOE 250.50).
