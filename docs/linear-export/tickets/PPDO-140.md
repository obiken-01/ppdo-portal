---
id: PPDO-140
title: 'Office tile and readiness board disagree with the division rows and the submit gate (FY2028)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-24
completed: 2026-09-24
parent: PPDO-122
blocked_by: (none)
related: PPDO-130, PPDO-138
url: https://linear.app/ralphoksiprojects/issue/PPDO-140
uuid: ed5be0cd-a7d9-44d3-8f81-70072e6f2abc
---

# PPDO-140 — Office tile and readiness board disagree with the division rows and the submit gate (FY2028)

Found 2026-09-24 on the SPO dashboard, right after [PPDO-138](https://linear.app/ralphoksiprojects/issue/PPDO-138/division-allocation-shows-peso0-used-for-fy2028-per-fund-used-read-the) moved the division rows to the ceiling rule.

## Symptoms

* Office tile **Costed in AIP ₱830,000** above division rows at ₱300,000 — same two activities. The tile summed activity totals: every fund, PS included.
* Readiness board: costed = the same all-funds, PS-included total, compared with the **sum of every fund's ceiling** — so a PS-heavy office could show **Over ceiling** while its own submit card said it had room. The office-ceilings page already worked around this (its header comment says so); the board never got the fix.
* Guest-office tiles computed Remaining = GF ceiling − an all-funds costed.

## Fix (FY2028+ only; FY2027 unchanged)

| Figure | Compared with | Rule |
| -- | -- | -- |
| Division rows | Division allocations | Ceiling rule, shared funds ([PPDO-138](https://linear.app/ralphoksiprojects/issue/PPDO-138/division-allocation-shows-peso0-used-for-fy2028-per-fund-used-read-the)) |
| PPDO office tile | Allocated to divisions | Ceiling rule, shared funds, **each activity once** |
| Guest office tiles | GF ceiling | Ceiling rule, **GF only** — new `CostedAgainstCeiling` |
| Readiness board costed + over-ceiling | GF ceiling (was: all funds' ceilings) | Ceiling rule, **GF only** — the submit gate's figure |

New `SumMooeCoByRecordAndFundAsync` — every office's GF lines in one query (no per-office loop).

Real data, FY2028 board costed: PPDO ₱1,426,314 → ₱427,000 · PTO ₱1,138,351 → ₱699,000 · SPO ₱830,000 → ₱300,000 · OPA ₱550 → ₱1,000. Ceilings unchanged (all GF today).

## Not in scope

A program shared by several divisions still counts in full on each division row — the activity→division model is being specified in [PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that).
