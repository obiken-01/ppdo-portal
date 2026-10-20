---
id: PPDO-138
title: 'Division allocation shows ₱0 used for FY2028 — per-fund "used" read the WFP ledger'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-24
completed: 2026-09-24
parent: PPDO-122
blocked_by: (none)
related: PPDO-130, PPDO-140
url: https://linear.app/ralphoksiprojects/issue/PPDO-138
uuid: 2a6f0029-dd4b-4a30-9301-ae7d1dd16898
---

# PPDO-138 — Division allocation shows ₱0 used for FY2028 — per-fund "used" read the WFP ledger

Found 2026-09-24 on a guest office (SPO) dashboard: ADMIN's General Fund row read **used ₱0** with ₱200K MOOE encoded.

## Cause

`BudgetPlanningDashboardService.BuildByDivisionAsync` filled each fund row's Used from the **WFP allocation ledger**. FY2028+ has no WFP in the portal (`AipFiscalYears`), so it is always empty there. The division row meanwhile showed an all-funds, PS-included rollup (₱700K = ₱500K PS + ₱200K MOOE) compared against a shared-fund allocation.

## Decided (Ralph, 2026-09-24)

* **Used = the ceiling's rule**: MOOE + CO, PS exempt, each activity rounded up to the thousand (DECISION 9) — agrees with the submit card's Encoded figure.
* **Row aligned**: Costed in AIP = sum of the fund rows; Remaining = allocated − that.
* **Shared programs**: unchanged — counted in full against each assigned division. The split rule belongs to [PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that)'s spec.
* FY2027 and earlier keep the WFP ledger (they have a real WFP).

## Fix

* New `IAipExpenditureRepository.SumMooeCoByConfigOfficeAsync` — per (activity, fund) with program ref code, one grouped query.
* Dashboard reads it for entered years; a fund used without allocation is now shown (overspend visible).
* Fund row label: "AIP costed" for FY2028+, "WFP used" before.

## Acceptance

- [ ] SPO ADMIN GF row shows ₱200,000 used; row Costed in AIP ₱200,000; Remaining ₱6,800,000
- [ ] FY2027 dashboard unchanged
