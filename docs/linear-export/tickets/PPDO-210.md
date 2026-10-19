---
id: PPDO-210
title: 'AIP printed figures: apply the +30% uplift to PS as well as MOOE and CO'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.4 — Minor fixes
created: 2026-10-14
completed: 2026-10-14
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-210
uuid: 609f9499-cc03-4179-a6a9-4fdbadc564ad
---

# PPDO-210 — AIP printed figures: apply the +30% uplift to PS as well as MOOE and CO

From Demo 3 (2026-10-13): "Add PS to have 30% markup".

## Current

DECISION G applies a fixed **+30% uplift to MOOE and CO only**, FY2028+, presentation-only (`AipPrintedFigures.cs`, `UpliftRate = 1.3m`). PS prints at base, rounded up to the thousand.

## Target

PS carries the same +30% uplift as MOOE and CO, under the same rules:

* FY2028+ only. FY2027 and earlier are untouched.
* **Presentation-only**: stored amounts, ceiling checks, and the external API (`ExternalAipMapper`, base pesos, never uplifted) are unchanged.
* Order of operations matches MOOE/CO: `UpToThousand(ps × 1.3)`.

## Same figure, same rule sweep

Every place that prints or compares an uplifted figure must agree: printed AIP form / Annex B Excel export, consolidated grid, any totals that sum printed PS+MOOE+CO, and any screen that shows "printed" vs "base" figures side by side. Doc comments that say "MOOE and CO" (`AipPrintedFigures`, `AipRounding`, AIP DTOs, `AIP_Form_Spec` §6.2) are updated to match.

## Acceptance

- [ ] FY2028 activity with PS ₱100,000 prints PS ₱130,000 (rounded up to the thousand).
- [ ] FY2027 activity unchanged.
- [ ] Ceiling check and external API still use base PS.
- [ ] Printed row total = printed PS + printed MOOE + printed CO.
- [ ] Tests updated: `AipPrintedFigures` cases for PS, red-tested.

Branch: `fix/v1.8.4-…` → PR into `release/1.8.4`.
