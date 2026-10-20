---
id: PPDO-97
title: 'AIP Entry expenditure form: Add Account, PS/MOOE/CO by account class, required fields, explicit fund-mode question'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-104
url: https://linear.app/ralphoksiprojects/issue/PPDO-97
uuid: 3505c9a8-5e06-4f5e-92a9-e5d6a4679ca2
---

# PPDO-97 — AIP Entry expenditure form: Add Account, PS/MOOE/CO by account class, required fields, explicit fund-mode question

**In one line:** four changes to `AipExpenditureTable`, all asked for at the 2026-09-15 demo — the link is renamed, the money columns follow the account's class, a line cannot be saved half-filled, and the single/multi fund question is asked before the link appears.

**Source:** 2026-09-15 PDC/finance demo notes · **Size:** M · **Demo:** required on or before 2026-09-23

## Scope

1. **"+ Add line" → "+ Add Account".** One string.
2. **PS/MOOE/CO enabled by the selected account's** `ExpenseClass`**.** The class is already stored (`Account.ExpenseClass`, [RAL-117](https://linear.app/ralphoksiprojects/issue/RAL-117/config-chart-of-accounts-rework-expense-class-default-nature-default)) and already routes itemised totals (`routedPreview`). Pick a PS account → the MOOE and CO inputs disable; an unrecognised or blank class leaves all three enabled, as today. Switching class after typing must not silently keep a figure in a now-disabled column.
3. **Required-field validation while encoding.** A line needs an account and at least one non-zero of PS/MOOE/CO; in single-fund mode the activity needs a fund source. Message under the field, Save disabled until satisfied — not a toast after the round trip.
4. **The fund-mode question is asked, not defaulted.** Today a checkbox ("This activity draws on several funds") silently defaults to single-fund and "+ Add line" is always visible. Instead: a Yes/No radio pair with **no default** on an activity that has no lines yet; **+ Add Account stays hidden until it is answered**. No → activity-level fund lookup **plus** the link. Yes → the link, and each line carries its own fund as today. An activity that already has lines keeps today's inferred answer (`funds.length > 1`) and never shows the unanswered state; `lockedToMulti` behaviour is unchanged.

## Out of scope

The procurement item panel, the itemised routing rules, and the server-side `AipProcurementRouting` — all unchanged. Validation here is a client-side encoding aid; the submit gate stays where it is.

## Acceptance

- [ ] A brand-new activity shows the fund question and no Add Account link until it is answered
- [ ] Answering No reveals the fund lookup and the link; answering Yes reveals the link and the per-line Fund column
- [ ] Choosing a PS account disables MOOE and CO; choosing an account with no class leaves all three enabled
- [ ] Save is refused, with the reason under the field, on a line with no account or with all three amounts zero
- [ ] An existing multi-fund activity still opens straight into multi-fund mode with its lines intact
