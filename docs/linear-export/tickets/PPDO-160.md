---
id: PPDO-160
title: 'Demo 2.15 T7 — Investment proposal editor page (sections A–M, work plan, H-1 preview)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-01
parent: PPDO-153
blocked_by: PPDO-155, PPDO-157
related: PPDO-166
url: https://linear.app/ralphoksiprojects/issue/PPDO-160
uuid: fd3503c2-8f92-4392-a1fe-582881e108cd
---

# PPDO-160 — Demo 2.15 T7 — Investment proposal editor page (sections A–M, work plan, H-1 preview)

**Spec:** `docs/v1.8/Investment_Proposal_Spec.md` §6.2 · **Parent:** [PPDO-153](https://linear.app/ralphoksiprojects/issue/PPDO-153/demo-215-pgom-investment-proposal-generate-the-word-proposal-from-the) · **Prompts file:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md`

> **Not a manual-implementation candidate:** a large page component.

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/Investment_Proposal_Spec.md FULLY — §6.2 is the screen, §2 the rules it displays,
§10 the acceptance list you will be tested against.
Read docs/DESIGN_SYSTEM.md and docs/PERFORMANCE_GUIDELINES.md.

Read these files before writing code:
- frontend/src/components/announcements/RichTextToolbar.tsx and AnnouncementEditorModal.tsx
  (the TipTap setup to extract)
- frontend/src/components/ui/Modal.tsx, ConfirmDialog.tsx, MessageDialog.tsx, Toast.tsx,
  Lookup.tsx, RowActions.tsx, StatusPill.tsx, MoneyInput.tsx
- the ProposalDto / UpdateProposalDto from PPDO-155 and the document model from PPDO-157
  (the H-1 preview must match the export's grouping and lines)
- frontend/src/app/(portal)/budget-planning/aip/entry/ (an Investment Planning page with
  read-only states and the shared user context)

Before changing anything, state in one line each:
- Current behaviour: no proposal editor.
- Target behaviour: /budget-planning/proposals/edit?id= edits every section A–M per §6.2, saves
  with the row version, and shows Final/read-only/conflict states.

Working branch: release/1.8.0.
Create feature/v1.8.0-ppdo-160-investment-proposal-editor off release/1.8.0 and open the PR
against release/1.8.0 (NOT main).

1. Extract components/ui/RichTextEditor.tsx from the Announcements toolbar, restricted to bold,
   italic, bullet and ordered lists; switch Announcements to it without changing its toolbar.
   Verify: the Announcements editor still works exactly as before (create + edit an announcement).
2. Editor shell: sticky header (Save, Export Word, Finalize/Reopen, Delete), A–M rail with
   unsaved/error dots, section-shaped skeletons, beforeunload + in-app leave confirm, Ctrl+S.
   Verify: load with network throttled — no layout shift between skeleton and content.
3. Sections A–F, I–M, signatories (spec §6.2 "Section specifics"): computed totals, live GAD
   budget, "Same as Section A" toggle with confirm, D's cost column label.
   Verify: spec §10 "Content rules" lines, one by one.
4. G work-plan editor (ProposalWorkPlanEditor): groups, ↑/↓ ordering, proposal-only steps with
   "Proposal only" tag, AIP rows tagged "From AIP" with read-only name/timeline/OPR.
   H-1 preview (AnnexH1Preview) rendered from the same grouping.
   Verify: spec §10 "Work plan and cost annex" lines.
5. States: 409 stale → MessageDialog with Reload / Keep editing (input kept); Final and
   cross-office-reviewer read-only rendering (text, not disabled inputs; actions hidden);
   activities-without-lines banner; "AIP has changed since finalized" notice.
   Verify: two browser tabs, save in both — the second shows the conflict dialog and keeps text.

Risks and rollback:
- The RichTextEditor extraction touches components/ui → blast radius includes Announcements;
  step 1's check covers it. Frontend only; revert is safe.

Stop and get my sign-off before: merging the components/ui/RichTextEditor extraction (shared
component).

Do NOT: autosave; allow tables/links/colours in rich text; edit AIP-sourced fields; call APIs
from Server Components.

When done, commit with:
feat(investment-proposal): investment proposal editor (PPDO-160)
```
