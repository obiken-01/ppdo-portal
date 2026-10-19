---
id: PPDO-196
title: 'Ceiling copy still says PBO sets the ceiling; PPDO finance does since PPDO-87'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.3 — Concurrent-edit guard: turn it on
created: 2026-10-08
completed: 2026-10-08
parent: (none)
blocked_by: (none)
related: PPDO-87, PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-196
uuid: 237100fc-d0b4-43b5-a710-ca69ae738a09
---

# PPDO-196 — Ceiling copy still says PBO sets the ceiling; PPDO finance does since PPDO-87

## Problem

Ceiling authority moved from PBO to PPDO finance users on 2026-09-15 ([PPDO-87](https://linear.app/ralphoksiprojects/issue/PPDO-87/office-ceiling-authority-moves-to-ppdo-finance-exempt-ceiling-save); the flag was renamed `CanManageOfficeCeilings`). The screens still tell a guest office that **PBO** sets their ceiling. Ralph found it while setting up [PPDO-195](https://linear.app/ralphoksiprojects/issue/PPDO-195/our-own-uat-test-pass-on-v182-using-pto-as-the-test-office-weekend-10), on PTO's Investment Planning page: "Waiting on PBO for the FY 2028 ceiling".

## Changes (display strings only; no logic)

| \# | File | Now | Change to |
| -- | -- | -- | -- |
| 1 | `frontend/src/lib/dashboard-status-band.ts:216` | `"Published by PBO"` | `"Published by PPDO"` |
| 2 | same line | `"Waiting on PBO"` | `"Waiting for ceiling"` |
| 3 | `dashboard-status-band.ts:395` | `Waiting on PBO for the ${fyLabel} ceiling. You can start encoding now.` | `Waiting for the ${fyLabel} ceiling. You can start encoding now.` |
| 4 | `dashboard-status-band.ts:396` | `Sending the AIP to PPDO opens once the Provincial Budget Office publishes it.` | `Sending the AIP to PPDO opens once the ceiling is published.` |
| 5 | `frontend/src/app/(portal)/budget-planning/page.tsx:390` | `"Set by PBO — read only"` | `"Set by PPDO — read only"` (Ralph's wording) |
| 6 | `frontend/src/components/aip/entry/AipSubmitChecklist.tsx:382` | `"PBO has not set your ceiling — treated as ₱0"` | `"PPDO has not set your ceiling — treated as ₱0"` |
| 7 | `backend/PPDO.Application/Services/AipCeilingService.cs:152` | `Ask the Provincial Budget Office to set the ceiling.` | `Ask PPDO to set the ceiling.` |
| 8 | `AipCeilingService.cs:157` | `…or ask the Provincial Budget Office to raise the ceiling.` | `…or ask PPDO to raise the ceiling.` |

Wording rule (Ralph, 2026-10-08): **waiting** states are generic ("Waiting for ceiling"); **who set it** says PPDO.

## Deliberately unchanged

* `config/api-access/page.tsx:298`, "(GSO, PBO, and other provincial systems)". PBO really is an API partner, so this is still correct.
* Code comments and doc comments that mention PBO are historical record (CLAUDE.md: don't bulk-rewrite). Optional: fix the two comments next to changed lines if they would now mislead.

## Acceptance

- [ ] No user-visible "PBO" / "Provincial Budget Office" remains except the API Access description (`grep` over `frontend/src` string literals and backend message strings)
- [ ] Guest-office dashboard with no ceiling: band shows "Waiting for the FY 2028 ceiling…" and the step note "Waiting for ceiling"
- [ ] With a ceiling set: step note shows "Published by PPDO"; Office Ceiling tile hint reads "Set by PPDO — read only"
- [ ] AIP Entry submit checklist with no ceiling: "PPDO has not set your ceiling — treated as ₱0"
- [ ] Submit over/without a ceiling: the error message says "Ask PPDO…"
- [ ] `dotnet test` and the frontend tests pass (none assert the old strings, checked 2026-10-08)

No migration. Small and low-risk.
