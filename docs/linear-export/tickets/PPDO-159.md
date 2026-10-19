---
id: PPDO-159
title: 'Demo 2.15 T6 — Investment Proposals list page + sidebar entry'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: PPDO-145, PPDO-155
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-159
uuid: 1ffaf38c-a410-4d73-95fb-0b6db5293f12
---

# PPDO-159 — Demo 2.15 T6 — Investment Proposals list page + sidebar entry

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` §6.1 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

Blocked by [PPDO-145](https://linear.app/ralphoksiprojects/issue/PPDO-145/demo-214-t3-investment-planning-pages-open-on-the-default-fiscal-year) for the `useDefaultFiscalYear()` hook (Demo 2.14 T3).

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md §6.1 and §10 "Roles and scope" / "UI states" FULLY.
Read docs/DESIGN_SYSTEM.md and docs/PERFORMANCE_GUIDELINES.md (frontend rules).

Read these files before writing code:
- frontend/src/lib/budget-planning-access.ts and frontend/src/components/layout/Sidebar.tsx
  (the nav rule and the route guard share one function — add the entry in both through it)
- frontend/src/components/ui/DataTable.tsx, TableSkeleton.tsx, RowActions.tsx, StatusPill.tsx,
  OfficeSelect.tsx
- the useDefaultFiscalYear hook from PPDO-145 (fiscal-year default)
- frontend/src/app/(portal)/budget-planning/aip/entry/page.tsx (query-string routing under
  output: "export", and how a budget-planning page reads the current user from context)
- frontend/src/lib/api.ts (all calls go through it)

Before changing anything, state in one line each:
- Current behaviour: no Investment Proposals page or sidebar entry.
- Target behaviour: /budget-planning/proposals lists the year's AIP projects in scope with their
  proposal status, and Create/Open/Export actions per spec §6.1.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-159-investment-proposals-list off release/1.8.0 and open the PR against
release/1.8.0 (NOT main).

1. Types + API functions (lib/investment-proposals.ts): getProposalList, createProposal,
   exportProposal (blob download).
   Verify: npm run lint && npx tsc --noEmit
2. Page /budget-planning/proposals/page.tsx: filters (fiscal year from useDefaultFiscalYear,
   OfficeSelect for host-office users, debounced search), server-side pagination, every §6.1
   state (8-row skeleton, both empty states, FY2027 message, error + Retry, read-only for
   cross-office reviewers — Create hidden). Create → navigate to the editor; 409 → navigate to
   the existing proposal.
   Verify: npm run dev; walk each §6.1 state (throttle the network for the skeleton).
3. Sidebar entry "Investment Proposals" after AIP (emoji icon, per DESIGN_SYSTEM.md).
   Verify: a Staff user without Budget Planning access doesn't see it.

Risks and rollback: frontend only; revert is safe.

Do NOT: fetch /auth/me in the page (read the shared user context); use text-slate-700 or
rounded-lg; build the editor (T7).

When done, commit with:
feat(investment-proposal): Investment Proposals list page (PPDO-159)
```
