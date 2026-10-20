---
id: PPDO-56
title: 'V18-46 — AIP ceiling service: validate at submit, upsert the ledger, expose remaining'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-07
parent: PPDO-48
blocked_by: PPDO-55
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-56
uuid: 1b180991-af76-4154-abfb-91d2bda02db6
---

# PPDO-56 — V18-46 — AIP ceiling service: validate at submit, upsert the ledger, expose remaining

**In one line:** the check that decides whether an office's AIP may be submitted — narrower than it reads, and wrong in four specific ways if written from intuition.

**Plan: **`docs/v1.8/Phase_Plan.md` §5, §12.1, §12.3 · **Size:** M · **Blocked by:** V18-45 · **Blocks:** V18-47, V18-49

## When it runs

✅ **DECISION C: validate at SUBMIT, not during entry.** Over-ceiling encoding is allowed and expected; the gate is V18-49's submit checklist. This service is what that gate calls.

## ⚠️ Four ways to get the check wrong

**1. It sums** `mooe + co` **— General Fund ONLY.** ↩️ DECISION H (all-fund ceilings) was **withdrawn 2026-08-26**, one day after being adopted. **This has now flipped twice — read §12.3 before re-deriving it from the meeting notes.**

**2. PS is exempt**, as an expense *class*, on top of the fund restriction.

**3. Non-GF funds must be EXPLICITLY EXCLUDED** — not merely left without ceiling rows. `GetDivisionAllocationAsync` resolves a missing allocation to `0m`, so **a blank row means *zero*, not *unlimited***. Leaving non-GF funds unconfigured silently forbids them instead of ignoring them.

**4. It compares the ROUNDED figures the document prints** (tracker A2-4), and the **BASE** figures. The +30% uplift of DECISION G is **presentation-only** and is **not** part of the comparison (tracker G3). Printed total is `PS + 1.3 × (MOOE + CO)`, but the ceiling never sees the 1.3.

⚠️ **A consequence that is intended and will look like a bug:** an office at exactly its ceiling passes every check and **prints a document 30% over it**. That is the deliberate outcome of DECISION G. Phase 5's form spec must say so, or someone will "fix" it back.

## ⚠️ This does not replace `WfpCeilingService`

**Zero diff there.** Its allocation check **stays live** for FY2028+ (§12.1), and a WFP expenditure is bound by **the lesser** of its AIP activity amount and the fund's currently remaining allocation. Two bounds, both enforced — a raised allocation gives no extra room without amending the AIP (V18-73); a cut one blocks the difference.

## ⚠️ Do not clamp `remaining` at zero

Standing warning across V18-45/46/48/49. A negative remaining after a ceiling cut is the signal that must block submit.

## Done when

* Submit-time validation sums `mooe + co`, General Fund only, PS exempt, on rounded base figures
* Non-GF funds are excluded by an explicit rule, not by absent configuration
* `remaining` is exposed and may be negative
* The ledger is upserted on submit
* `WfpCeilingService` has a zero diff
* Tests pin all four traps above, each named

## Out of scope

The submit checklist UI (V18-49) · office-level ceilings for division-less offices (V18-47) · the PBO ceiling UI (V18-48) · the uplift's rendering (Phase 5).
