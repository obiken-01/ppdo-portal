---
id: PPDO-156
title: 'Demo 2.15 T3 — HGDG score → attributed GAD budget calculator (hand-code candidate)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: PPDO-155
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-156
uuid: a1db9376-9637-4364-9c23-2c3907b0bc90
---

# PPDO-156 — Demo 2.15 T3 — HGDG score → attributed GAD budget calculator (hand-code candidate)

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` decision 9 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

> **Manual-implementation candidate for Ralph** (CLAUDE.md "Flag manual-implementation candidates"): pure function, tiny blast radius, feedback entirely via `dotnet test`, and `WfpExpenditureCalculator` is the sibling to copy the shape from. **Guidance ladder level 1:** this spec + that sibling. Escalate only on request. Handing it back is a normal outcome.
>
> ⚠️ **Merge gate:** confirm the score bands and percentages (spec decision 9) against the PCW HGDG manual first. Starting before that is fine.
>
> Follows T2 on purpose: T2 ships `attributedGadBudget = null`, so this ticket is never on the critical path.

## Implementation prompt

```
Read CLAUDE.md.
Read docs/v1.8/Investment_Proposal_Spec.md decision 9 and §11 (HgdgAttributionTests) FULLY.

Read these files before writing code:
- backend/PPDO.Application/Common/WfpExpenditureCalculator.cs (the sibling: a static, pure
  calculator in Application/Common)
- backend/PPDO.Tests/Application/WfpExpenditureCalculatorTests.cs (its test shape)
- the InvestmentProposalService from PPDO-155 (where attributedGadBudget is set to null today)

Before changing anything, state in one line each:
- Current behaviour: attributedGadBudget is always null.
- Target behaviour: score present → Project Cost × band %; score null → null.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-156-hgdg-attribution off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

TDD: write HgdgAttributionTests first — every boundary in decision 9 — then implement.

1. HgdgAttribution (static, Application/Common): Percent(decimal? score) → int? and
   AttributedBudget(decimal? score, decimal projectCost) → decimal? (2 decimals,
   MidpointRounding.AwayFromZero).
   Verify: dotnet test --filter HgdgAttributionTests
2. Wire it into InvestmentProposalService where the header is built; the HGDG checklist code list
   (HgdgChecklists constant) lives next to it.
   Verify: dotnet test --filter InvestmentProposalServiceTests still passes, plus one new test:
   score 8, cost 200000 → 100000.00

Risks and rollback: wrong bands → fix the constants; no migration, revert is safe.

Do NOT: compute the score from checklist answers (spec §7 non-goal).

When done, commit with:
feat(investment-proposal): HGDG attributed GAD budget calculator (PPDO-156)
```
