---
id: PPDO-32
title: 'Epic: v1.8.0 Phase 2 — AIP Foundation'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: (none)
blocked_by: (none)
related: PPDO-16, PPDO-28, PPDO-31, PPDO-48
url: https://linear.app/ralphoksiprojects/issue/PPDO-32
uuid: 400476e9-cbf7-4bae-8b0f-1ba184c8ee3b
---

# PPDO-32 — Epic: v1.8.0 Phase 2 — AIP Foundation

**In one line:** give the AIP a real owner, a place to record what money is *for*, and one unit — so Phase 3 (Entry) and Phase 4 (Review) have something to build on.

Spec: `docs/v1.8/AIP_Foundation_Spec.md` · Plan: `docs/v1.8/Phase_Plan.md` §4

⚠️ Not to be confused with [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-for-gso-phase-2-implementation), "External AIP API for GSO — Phase 2". Different thing.

## Why this phase exists

Three structural facts block the redesign:

1. An AIP office has no owner. `AipOffice` identifies its office by `RefCode` *string suffix matching* — there is no column to scope on. A review workflow cannot be built on a string comparison.
2. Money sits on the leaf with no breakdown. `AipActivity.Ps/Mooe/Co/Total` are four loose decimals with nowhere to say what an amount is *for* — which is what Phase 3 entry and Phase 5's printable form both need.
3. **AIP stores thousands while everything it touches stores pesos.** Six sites multiply by 1000 to cross that seam, and nothing guards it.

**This phase ships no user-facing feature.** That is deliberate.

## Children — 9 tickets

**Order:** V18-32 + V18-35 in parallel → V18-36, V18-33, V18-39 → V18-34, V18-40 → V18-37 → V18-38.

| # | Ticket | Size |
| -- | -- | -- |
| V18-32 | Ownership FK: `AipOffice` → `offices.id` | M |
| V18-35 | Units: thousands → pesos, all fiscal years | L |
| V18-36 | Pin the AIP↔WFP numeric boundary with tests | S |
| V18-33 | `aip_expenditures` child table | M |
| V18-34 | Derived, recomputed activity totals | M |
| V18-39 | AIP scope resolver — `OfficeScope` × `DivisionScope` | M |
| V18-40 | New AIP record shape — office-owned, LDIP-like | L |
| V18-37 | FY partition — FY≤2027 old shape, FY≥2028 new | M |
| V18-38 | Freeze `.xlsm` upload to historical years | S |

## Three things to know before starting

**1. ~~Five~~ 15 manual migrations reach production with v1.8.0.** ✅ **Corrected 2026-09-03, at the end of the phase — both numbers here were wrong.** "Five" counted Phase 1's two AIP-adjacent migrations (`AddClimateChangeTypologies`, `AddEsreCodes`) and missed Phase 1's identity, password-reset and permission ones entirely. "Three more" became **four**, because V18-40 was scoped after this was written. **CI runs none of them. **`docs/v1.8/Pre_Deployment_Checklist.md` §1 is the authority on the count and the order — it is rechecked against `git diff` at release time; believe it over any planning doc, this one included. ⚠️ The units migration is **#13 of 15, not last**; that is safe, and the checklist says why.

**2. V18-35 is the dangerous one and is bigger than Phase_Plan §4 says.** The plan names two display sites. There are **19**, all in `aip/detail/page.tsx` (the largest file in the repo): 2 thousands headers, 6 raw cells, and **11 **`MoneyInput` **fields**. Entry is in thousands too — so a value typed as `250` must still mean ₱250,000 afterwards, or every edit silently divides the record by a thousand.

**3. One open question needs someone outside the dev team. P2-a:** after the migration, does the AIP detail page drop the `(in ₱000)` headers and show full pesos, or divide by 1000 at render to keep the province's convention? Ask the finance officers. The other two open questions (P2-b gate mechanism, P2-c expense vocabulary) are engineering calls.

## Out of scope

AIP entry (Phase 3) · review and submission (Phase 4) · the printable form (Phase 5) · moving LDIP to pesos · any new endpoint · [PPDO-31](https://linear.app/ralphoksiprojects/issue/PPDO-31/enforce-1-program-1-division-on-ppa-assignment) (1 program : 1 division, deferred to the WFP rework).

Follows [PPDO-28](https://linear.app/ralphoksiprojects/issue/PPDO-28/epic-v180-phase-1-identity-configuration-and-landing) (Phase 1 — Identity, Configuration & Landing), which is complete and merged to `release/1.8.0`.
