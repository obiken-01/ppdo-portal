---
id: PPDO-157
title: 'Demo 2.15 T4 — Proposal document model builder (G/H-1 grouping, computation lines)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: PPDO-155
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-157
uuid: b6900ea8-fdb2-4ff7-a3f2-63fd3edac72c
---

# PPDO-157 — Demo 2.15 T4 — Proposal document model builder (G/H-1 grouping, computation lines)

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` decisions 6, 13–18, 21 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

Pure, unit-tested builder; no Open XML. T5 renders its output; T7's H-1 preview must match it.

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md FULLY — authoritative. This ticket implements
decisions 6, 13–18 and 21 as a pure, testable model (no Open XML).
Read docs/v1.8/Investment_Proposal_Findings.md §8.2 (how real proposals lay out H-1).

Read these files before writing code:
- backend/PPDO.Application/Common/AipFormRowBuilder.cs (the Annex B equivalent: a pure builder the
  Excel service renders — copy its separation, not its rounding)
- backend/PPDO.Application/Services/AipConsolidatedService.cs (how the builder is fed)
- backend/PPDO.Domain/Entities/AipExpenditure.cs and AipProcurementItem.cs (snapshot columns only)
- the ProposalDto from PPDO-155 (the builder's input)

Before changing anything, state in one line each:
- Current behaviour: no document model exists.
- Target behaviour: InvestmentProposalDocumentBuilder.Build(ProposalDto) → an ordered,
  render-ready model of every section, with G and H-1 sharing one grouping.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-157-proposal-document-builder off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

TDD: InvestmentProposalDocumentBuilderTests first — every builder bullet in spec §11.

1. Model types (Application/DTOs/InvestmentProposal/Document/): ProposalDocument, SectionA rows,
   WorkPlanRow (group header | row), AnnexH1Row (group header | activity | expenditure heading |
   computation line | total), RichTextBlock (paragraph/list items with bold/italic runs parsed
   from the allow-listed HTML).
2. Builder: grouping + order shared by G and H-1; ungrouped after grouped; empty groups skipped;
   proposal-only rows blank in H-1; computation line
   "{Name}  {UnitPrice:N2} x {Qty} {Unit}[ x {NumberOfDays} days] = {LineTotal:N2}" (days part only
   when NumberOfDays ≠ 1); expenditure with no items → heading + amount; activity with no lines →
   one totals row; amounts in the PS/MOOE/CO column; fund = the fund NAMES (decision 17, revised by PPDO-173); grand
   total; Section M "N/A" when empty; F-Direct = A's rows while directSameAsSummary.
   Verify: dotnet test --filter InvestmentProposalDocumentBuilderTests
3. Invariant test: for a seeded proposal, H-1 grand total == header.projectCost (to the centavo).
   Verify: same filter.

Risks and rollback: pure code, no migration — revert is safe.

Do NOT: reference DocumentFormat.OpenXml here (Application stays renderer-agnostic); round
amounts or apply +30% (decision 7).

When done, commit with:
feat(investment-proposal): document model builder for G and Annex H-1 (PPDO-157)
```
