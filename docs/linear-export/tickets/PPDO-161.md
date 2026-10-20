---
id: PPDO-161
title: 'Demo 2.15 T8 — Signatory defaults card on Investment Planning Settings'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: PPDO-144, PPDO-155
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-161
uuid: 1abb02f4-c4c0-432f-a360-040d382f463d
---

# PPDO-161 — Demo 2.15 T8 — Signatory defaults card on Investment Planning Settings

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` decisions 19, 25; §4 (signatory-defaults); §6.3 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

Blocked by [PPDO-144](https://linear.app/ralphoksiprojects/issue/PPDO-144/demo-214-t2-config-page-investment-planning-settings-default-fiscal) (the Investment Planning Settings config page this card sits on).

## Implementation prompt

```
Read CLAUDE.md.
Read docs/v1.8/Investment_Proposal_Spec.md decisions 19 and 25, §4 (signatory-defaults) and §6.3.

Read these files before writing code:
- the Investment Planning Settings page from PPDO-144 (/config/investment-planning) and its
  default-fiscal-year card — this card sits beside it and copies its states
- the signatory-defaults endpoints from PPDO-155

Before changing anything, state in one line each:
- Current behaviour: PPDC/LCE names can only be typed per proposal.
- Target behaviour: a PPDO config manager sets them once; new proposals default slots 2–3 from them.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-161-signatory-defaults off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

1. Card with four inputs (PPDC name/position, LCE name/position), Save, the same
   loading/error/success states as the fiscal-year card; hidden without
   CanManageInvestmentPlanningSettings.
   Verify: as a PPDO config manager, save names → create a new proposal → slots 2–3 are filled;
   as a guest-office Admin, the card is not shown.

Risks and rollback: frontend only; revert is safe.

Do NOT: back-fill existing proposals' signatories (defaults apply at creation only).

When done, commit with:
feat(investment-proposal): signatory defaults on Investment Planning Settings (PPDO-161)
```
