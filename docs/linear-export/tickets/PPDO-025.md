---
id: PPDO-25
title: 'Config: eSRE code table + management page'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-02
parent: PPDO-28
blocked_by: (none)
related: PPDO-19, PPDO-124
url: https://linear.app/ralphoksiprojects/issue/PPDO-25
uuid: f46165fb-a93e-41f3-ae6b-34cfaf4705ce
---

# PPDO-25 — Config: eSRE code table + management page

**V18-10** — plan: `docs/v1.8/Phase_Plan.md` §3.2; raised in `AIP_Requirements_Review.md` §5.5.

The eSRE classification (`SS` / `ES` / `ID` / `EN`) is a **free string** on `AipActivity`. Same problem and same fix as V18-09: an `esre_codes` config table plus a management page, following the existing config-page pattern.

Smaller than V18-09 — a short, near-static list — so it may be folded into V18-09's PR if that keeps the diff readable. Kept as its own ticket because it is a separate table and a separate page tile.

Same constraints: snake_case table/columns mapped from PascalCase properties, soft delete only, `CanManageConfig` gate, design-system-compliant page. Backfilling existing AIP rows onto FK references is Phase 2 work.
