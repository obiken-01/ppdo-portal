---
id: PPDO-192
title: 'AIP Entry — IndexedDB draft mirror for expenditure lines (follow-up to PPDO-112)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-10-06
completed: 2026-10-07
parent: (none)
blocked_by: PPDO-112
related: PPDO-191
url: https://linear.app/ralphoksiprojects/issue/PPDO-192
uuid: 845b38f0-79fa-4188-8a5e-78a68936f0a2
---

# PPDO-192 — AIP Entry — IndexedDB draft mirror for expenditure lines (follow-up to PPDO-112)

Spec: `docs/v1.8/AIP_Expenditure_Draft_Spec.md` (authoritative; committed on the [PPDO-112](https://linear.app/ralphoksiprojects/issue/PPDO-112/v18-64-indexeddb-draft-mirror-for-aip-entry-with-restore-prompt) branch, lands with it).

**Why:** [PPDO-112](https://linear.app/ralphoksiprojects/issue/PPDO-112/v18-64-indexeddb-draft-mirror-for-aip-entry-with-restore-prompt) mirrors an activity's descriptive fields only. Ralph's live check on 2026-10-06: he ended the browser process while adding an expenditure line and nothing was offered back. The expenditure table is where most typing happens: account, amounts, and often a dozen procurement items.

**Blocked by** [PPDO-112](https://linear.app/ralphoksiprojects/issue/PPDO-112/v18-64-indexeddb-draft-mirror-for-aip-entry-with-restore-prompt) (reuses its `lib/activity-drafts.ts` and `lib/aip-cache-db.ts`). Milestone not chosen yet.

✅ **Spec status: ready** (Ralph, 2026-10-06). Both open questions settled: a draft for a since-deleted line is offered back as a new line (decision 4), and restored procurement items keep their drafted prices, because the server stores the `unitPrice` it is sent and never re-reads the price index (decision 8, checked against `AipExpenditureService.BuildItems`).

**Manual-implementation candidate? No.** The wiring lives in `AipExpenditureTable.tsx` (\~1,070 lines, with the procurement editor and the [PPDO-191](https://linear.app/ralphoksiprojects/issue/PPDO-191/v18-714b-wire-rowversion-into-aip-entry-and-the-wfp-is-creation-toggle) conflict flows), and the IndexedDB version bump has a bad feedback loop. The decision function alone (spec §2.4 and §11) would be a good hand-coded slice: pure, sibling `decideDraft` to copy, tested by `npm test`.

---

## Implementation prompt

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.8/AIP_Expenditure_Draft_Spec.md FULLY — it is the authoritative spec for this ticket.
Also read docs/v1.8/AIP_Offline_Caching_Spec.md §5 and decision 8, and
docs/v1.8/AIP_Concurrent_Edit_Spec.md §4 (line rowVersion) and §6 (conflict panel).

Read these files before writing code:
- frontend/src/lib/activity-drafts.ts + activity-drafts.test.ts (PPDO-112 — the session, safe
  wrappers, key and decision function you are generalising; its tests must keep passing unchanged)
- frontend/src/lib/aip-cache-db.ts + aip-cache-db.test.ts (the one place the DB is opened; the
  guarded upgrade and the open timeout you extend to version 3)
- frontend/src/components/aip/entry/AipActivityFields.tsx (PPDO-112 wiring: ownsDraft, restoredBase,
  the mirror effect, the read-race guard, DraftBanner — the pattern to follow)
- frontend/src/components/aip/entry/AipExpenditureTable.tsx (the `Draft` shape, beginAdd/beginEdit,
  save(existingId, rowVersion), remove, closeEditor, lineConflict, the PPDO-166 openedDraft guard,
  MULTI_FUND_ENTRY_ENABLED / legacyMulti / activityFund gating of + Add Account)
- frontend/src/components/aip/entry/AipProcurementItemTable.tsx (items flow straight up through
  onItemsChange; confirm no item state lives only inside it)
- frontend/src/components/aip/entry/AipActivityPanel.tsx (where draftUserId comes from; the table
  is rendered here)
- frontend/src/components/aip/review/AipActivityReviewModal.tsx (also renders the table — must stay
  without drafts)

Before changing anything, state in one line each:
- Current behaviour: an unsaved expenditure line (new or edited, items included) is lost if the tab
  crashes or closes.
- Target behaviour: the open line editor is mirrored to IndexedDB per user+activity and offered back
  on reopen, per the spec.

Working branch: release/1.8.2 (after PPDO-112 has merged into it).
Create feature/v1.8.2-ppdo-192-expenditure-draft-mirror off origin/release/1.8.2 and open the PR
against release/1.8.2 (NOT main). Never branch off uat.

TDD: write failing Vitest tests first (spec §11), then implement.

1. Generalise the PPDO-112 session (spec decision 7): make createDraftSession and the safe
   read/write/delete generic over the stored value, keeping the activity API and every existing
   test as they are.
   Verify: cd frontend && npm test — all PPDO-112 tests pass with no edits to them.
2. aip-cache-db.ts: DB_VERSION 2 → 3, guarded creation of store `expenditure-drafts`.
   Verify: new tests for fresh DB (three stores), v2 → v3 keeps both existing stores' entries,
   v1 → v3 in one step.
3. lib/expenditure-drafts.ts: key `<userId>:<activityId>` (null without a user), value per spec §5,
   and decideExpenditureDraft(draft, lines) → none | restore | restore-changed | restore-as-new,
   covering every row of spec §11's first bullet (incl. 4a: new-line draft equal to a saved line
   → none).
   Verify: npm test; red-test the user axis, delete-on-save and the 4a guard (each must fail a test).
4. Lift DraftBanner out of AipActivityFields into components/aip/entry/AipDraftBanner.tsx (message
   and optional note as props, optional disabled-Restore reason). AipActivityFields uses it with
   no visible change.
   Verify: tsc; the activity banner still reads exactly as before.
5. Wire AipExpenditureTable behind an optional `draftUserId` prop, passed only from
   AipActivityPanel: read on mount (skip if canEdit is false or an editor is already open),
   mirror the open editor (debounced, flushed on pagehide / hidden / unmount), banner at the top of
   the table, Restore → beginAdd/beginEdit then load the draft (dirty), restore-changed saves
   against baseRowVersion, restore-as-new opens the add row, Restore disabled with the + Add
   Account reason when adding is withheld.
   ⚠️ Delete on: successful add/update (Overwrite included), Cancel, conflict Discard, PPDO-166
   in-app Discard, successful delete of that line. KEEP on 409 and on a failed save.
   Verify: npx tsc --noEmit, npm run lint, npm test, npm run build all clean; then the spec §10
   checklist on the running app.

Risks and rollback:
- The DB version bump touches PPDO-111's cache and PPDO-112's drafts. If the upgrade were wrong,
  every picker falls back to live fetches and drafts stop (degraded, not broken). Rollback: revert
  the PR — but a browser already upgraded to version 3 then makes the version-2 code's open fail
  (VersionError: opening at a lower version than stored). getAipCacheDb catches that as "no
  database", so pickers go live and drafts stop until the site data is cleared. Say this in the PR;
  it is the real rollback cost.
- Generalising activity-drafts.ts could regress PPDO-112. Its tests are the guard: they must pass
  unedited.

Stop and get my sign-off before: changing AipConflictPanel or anything in components/ui/
(shared); any backend change.

Do NOT: build offline editing (V18-68); mirror procurement preset dialogs, the activity-fund picker
or the multi-fund question; add drafts to the review modal; retrofit WFP's localStorage draft;
keep more than one draft slot per activity.

Do NOT re-read prices from the price index on restore, or warn about price changes (spec
decision 8).

PR body: the spec §10 checklist as "Manual checks for Ralph", the DB 2 → 3 rollback note, and a confidence % with what it rests on and what was not run live.

When done, commit with:
feat(aip): IndexedDB draft mirror for AIP Entry expenditure lines (PPDO-192)
```
