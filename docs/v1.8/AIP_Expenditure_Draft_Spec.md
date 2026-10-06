---
status: draft — defaults proposed, awaiting Ralph's review of §2 and the open follow-ups
version: v1.8.2 (or later — milestone not yet chosen)
tickets: PPDO-192 (expenditure-line draft mirror)
supersedes: nothing — extends PPDO-112 (V18-64), whose non-goals listed expenditure lines
---

# AIP Entry — Draft Mirror for Expenditure Lines

> Follows PPDO-112, which mirrors an activity's **descriptive fields** to IndexedDB. That ticket
> scoped expenditure lines out on purpose ("descriptive fields only"). This spec covers them. Found
> in Ralph's PPDO-112 live check on 2026-10-06: he ended the browser process while adding an
> expenditure line, and nothing was offered back.

## 1. Goal

An encoder costing an activity can spend minutes on one expenditure line: the account, the amounts,
and often a dozen procurement items picked from the ~6,400-row price index. Today a crashed or
closed tab loses all of it. After this ticket, the open line editor (new or existing line, procurement
items included) is mirrored to the same IndexedDB database PPDO-112 created. The next time the
activity is opened, the encoder is offered it back. This is the same crash and accidental-close
safety net as PPDO-112, applied to the part of AIP Entry where the most typing happens.

## 2. Decisions (settled — defaults proposed 2026-10-06, Ralph to confirm)

1. **One draft slot per user per activity, not one per line.** `AipExpenditureTable` allows exactly
   one open editor at a time (`adding` or `editingId`, never both), so there is only ever one line's
   worth of unsaved typing per activity. The slot records which line it belongs to. Starting to type
   in a different line's editor replaces the slot, just as typing over an offered activity draft
   replaces it in PPDO-112.
2. **Key `<userId>:<activityId>`, in a new store `expenditure-drafts`.** The user axis is there for
   the same reason as PPDO-112 decision C: office PCs are shared. A separate store, rather than a key
   suffix in `activity-drafts`, keeps the two value shapes from ever being read as each other.
   ⚠️ This means **`DB_VERSION` 2 → 3** in `lib/aip-cache-db.ts`. The upgrade adds only the missing
   store, under the same guarded pattern PPDO-112 tested. It cannot be folded into version 2:
   UAT and every dev machine that ran PPDO-112 are already at version 2 and would never upgrade.
3. **The value is the editor's own `Draft` shape** (`accountId`, `fundingSourceId`, `ps`, `mooe`,
   `co`, `procurementItems`), exactly what `AipExpenditureTable` holds. It is stored with the target
   (`{ kind: "new" }` or `{ kind: "line", lineId }`), `baseRowVersion` (the line's version when the
   edit began, null for a new line) and `savedAt`. Comparing form shape to form shape keeps "the
   draft says what the server says" exact, the same reasoning as PPDO-112.
4. **"Is the draft worth offering" is decided by rowVersion and content, never by clocks.** This is
   PPDO-112 decision A, with two cases that only lines have:
   - **New line whose content matches a saved line** (same account, fund, amounts and items) → it
     was saved and the delete never ran (tab killed between the response and the delete) → **delete
     silently**. Offering it would create a duplicate line.
   - **Existing line that no longer exists** (deleted since) → **offer it as a new line**, worded
     so. The typed amounts and items are the encoder's work; silently dropping them is the failure
     this feature exists to prevent.
5. **Restoring onto a line that moved on saves against the draft's base version**, so the overlap
   reaches the existing line conflict panel (`lineConflict.kind = "save"`) instead of silently
   overwriting. This is the same as PPDO-112 decision A, and it costs the same: an activity-wide
   fund change (`runFundChange`) bumps every line's version, so a draft from before it shows one
   extra conflict panel.
6. **Delete on every successful save of that line (Overwrite included), on Cancel, on the conflict
   panel's Discard, on the PPDO-166 guard's in-app Discard, and on a successful delete of that
   line. Keep on a 409 and on a failed save.** These are the same rules as PPDO-112, plus the delete
   case.
7. **Reuse PPDO-112's machinery, generalised rather than copied.** `createDraftSession`, the safe
   read/write/delete wrappers and the debounce are made generic over the stored value (or moved to a
   shared `lib/local-drafts.ts` with the activity API kept as it is). The key builder and the
   decision function are per feature. The "a broken store never breaks the page" guarantee applies
   unchanged.
8. **AIP Entry only.** Same as PPDO-112: only `AipActivityPanel` turns the mirror on (it already has
   the user id). The review modal renders `AipExpenditureTable` too, and stays without drafts.

### Open follow-ups (not blocking)

- **Restoring a deleted line as a new line (decision 4b)** — the proposed default. The alternative
  is to show the text read-only with a "copy" affordance. Ralph to confirm.
- **Price-index prices in a restored draft.** Items carry the `unitPrice` they were picked at. If
  the price index changed between the draft and the restore, the restored line shows the old price,
  exactly as an editor left open overnight would today. Does the server re-read prices from
  `priceIndexItemId` on save? Confirm while reading `AipExpenditureService` at ticket time. If it
  does not, say so in the PR; it is not this ticket's to change.
- **Procurement presets** (Load/Save preset dialogs) are not mirrored. They are their own short
  flows and save immediately.

## 3. Behaviour

| Case | Given | When | Then |
|---|---|---|---|
| Happy path, new line | Encoder clicked + Add Account, picked an account, typed amounts or added items | Tab crashes / process ended / tab closed with **Leave**, then the activity is reopened | Banner above the expenditure table: "A local draft of a new expenditure line from `<time>` was found. Restore it? [Restore] [Discard]". Restore opens the add row filled with the draft, dirty |
| Happy path, edited line | Encoder was editing an existing line | Same | Banner names the line ("…for the line `<account name>`…"). Restore opens that line's editor with the draft values, dirty |
| Draft cleared on save | A draft exists for the open line | Save succeeds (add or update, including Overwrite) | Draft deleted. Reopening shows no banner |
| Draft cleared on delete | A draft exists for an existing line | That line is deleted successfully | Draft deleted |
| Edge: draft equals server | Edited-line draft whose values equal the saved line | Activity opens | No banner; draft deleted silently |
| Edge: new line already saved | New-line draft matching a saved line of this activity | Activity opens | No banner; draft deleted silently (decision 4a) |
| Edge: line saved since | Edited-line draft, line's rowVersion ≠ `baseRowVersion` | Activity opens | Banner plus: "This line has been saved since the draft was made. If you restore it, saving will show you what changed first." Save checks against the base version → conflict panel |
| Edge: line deleted since | Edited-line draft, line no longer in the activity | Activity opens | Banner: "…for a line that has since been deleted. Restore it as a new line?" Restore opens the **add** row with the draft |
| Edge: add not available | New-line (or deleted-line) draft, but + Add Account is withheld (single mode with no activity fund, or a legacy multi-fund activity) | Activity opens | Banner shown. Restore disabled, with the reason the + Add Account link would give ("Pick the activity's fund first"). Discard works |
| Edge: another editor already open | The encoder opened a line editor before the stored draft finished loading | Draft read resolves | Banner not shown; the open editor's typing owns the slot (same race guard as PPDO-112) |
| Edge: typing elsewhere | Banner offered for line A | Encoder opens line B's editor and types | Slot replaced by line B's draft; banner goes |
| Edge: items only | Line itemised with procurement items, amounts derived | Crash, reopen, Restore | Items restored with quarter, qty, days, unit price; amounts re-derive as when typed |
| Edge: two tabs | Same activity open in two tabs, both typing in lines | Either crashes | Each tab wrote the same slot; the later write wins locally. No cross-tab merge (PPDO-112 non-decision, unchanged) |
| Failure: conflict | Restored draft saved, someone saved the line first | Save → 409 | Line conflict panel as today; draft **kept** |
| Failure: save rejected | Server validation fails (e.g. account deactivated since) | Save → 400 | Existing error text; draft kept |
| Failure: IndexedDB unavailable | Private window, storage blocked, blocked upgrade | Anything | No banner, no writes, no errors; AIP Entry behaves exactly as before |
| Role: Staff encoder, own office | Can edit the activity | Any of the above | As above |
| Role: Admin / SuperAdmin | Editing on AIP Entry | Any of the above | As above, under their own user id |
| Role: read-only (submitted record, division lock, comment-only reviewer) | `canEdit` false | Activity opens | No banner. Draft kept, not deleted, so it is offered again if the record is returned for revision |
| Role: user B on user A's machine | A left a draft and signed out | B opens the same activity | No banner: B's key differs |
| Scope: office reassigned | A's draft exists; A can no longer open that office's AIP | — | Never shown: the activity is unreachable, so no read happens. Nothing leaks |

## 4. API contract

No new or changed endpoints. Saves still go through the existing `POST
/api/budget-planning/aip/activities/{activityId}/expenditures` and `PUT
/api/budget-planning/aip/expenditures/{id}` with their rowVersion check (V18-71). Errors and the
409 shape are unchanged.

## 5. Data model changes

None server-side. No migration.

**IndexedDB** (`ppdo-aip-cache`, client-side):

- **Version 3.** Upgrade adds store `expenditure-drafts` if missing. `reference-data` and
  `activity-drafts` are untouched.
- **Store `expenditure-drafts`:** key = `<userId>:<activityId>`, value =
  `{ target: { kind: "new" } | { kind: "line", lineId: number }, fields: { accountId, fundingSourceId,
  ps, mooe, co, procurementItems: SaveAipProcurementItemRequest[] }, baseRowVersion: string | null,
  savedAt: string (ISO) }`.

## 6. UI states

### AIP Entry — expenditure table (`AipExpenditureTable`)

| State | Content |
|---|---|
| Loading | Unchanged. The draft read never delays the table; the banner appears when it resolves |
| No draft / draft not worth offering | Unchanged — no banner |
| Draft offered | Inline amber banner at the top of the table, above the Expenditures header. Same styling and component shape as PPDO-112's `DraftBanner` (flat, `border-amber-200 bg-amber-50 text-amber-800`, Manila time) |
| Restored | The add row or the line's editor opens filled with the draft, dirty: PPDO-166's warning and Save behave as for typed edits |
| Discarded | Banner goes; draft deleted; table as saved |
| Error | Unchanged save-error text; draft kept |
| Read-only / forbidden | No banner (hidden, not disabled) |
| Validation | Unchanged |

Components: the PPDO-112 banner, lifted from `AipActivityFields.tsx` into a small shared
`components/aip/entry/AipDraftBanner.tsx` now that it has two callers. `AipConflictPanel` unchanged.

## 7. Non-goals

- **Offline editing** (V18-68). Every save is still a live request.
- **Several drafts per activity.** One open editor, one slot (decision 1).
- **Procurement preset dialogs**, the activity-fund picker and the multi-fund question. Each
  saves immediately or is a single click.
- **Cross-tab or cross-device sync.**
- **The review modal's expenditure table** (decision 8).
- **WFP's localStorage draft** — still a separate, later decision.

## 8. Deployment notes

- No migration, no backend change, no new dependency (`idb` and `fake-indexeddb` arrived with
  PPDO-111/112).
- ⚠️ **IndexedDB version bump 2 → 3.** A tab still open on the previous build blocks the upgrade;
  PPDO-112's open timeout makes that a 3-second fallback to live fetches with no drafts, not a hang.
- ⚠️ **Rollback is not free.** Reverting leaves upgraded browsers at version 3, and the version-2
  code's open then fails (`VersionError`). It is caught as "no database": pickers fetch live and
  drafts stop until site data is cleared. Degraded, not broken, but it lasts past the revert.
- Ships after PPDO-112 (it reuses that ticket's modules).

## 9. Ticket split

One ticket. **Blocked by PPDO-112.** Not split further: the generalisation (decision 7) is only
worth doing with its second caller in hand.

## 10. Acceptance checklist

- [ ] Click + Add Account, pick an account and type an MOOE amount, wait a second, end the browser
      process (Shift+Esc → End process). Reopen the activity: the new-line banner appears; Restore
      opens the add row with the account and amount.
- [ ] Same with procurement items in two quarters: Restore brings back every item with its quarter,
      quantity and price, and the derived amount matches what was shown before the crash.
- [ ] Edit an existing line's amount, end the process, reopen: the banner names that line; Restore
      opens its editor with the typed amount.
- [ ] Save the restored line: reopening shows no banner.
- [ ] Discard on the banner: the banner goes, a reload shows no banner, the table shows the saved
      values.
- [ ] Draft an edit to a line, then (in another browser) delete that line. Reopen: the banner offers
      the draft as a new line; Restore opens the add row.
- [ ] Draft an edit to a line, then (in another browser) save that line. Reopen: the banner has the
      "saved since" line; Restore then Save shows the conflict panel; reload still offers the draft;
      Overwrite then clears it.
- [ ] Add a line and save it, then end the process before anything else. Reopen: no banner, and no
      duplicate line (decision 4a).
- [ ] On a single-fund activity with no fund picked, a new-line draft shows the banner with Restore
      disabled and the fund reason.
- [ ] Two users on one browser profile: B never sees A's expenditure draft.
- [ ] With IndexedDB blocked, AIP Entry loads, adds, edits and deletes lines normally.
- [ ] DevTools → IndexedDB → `ppdo-aip-cache` is version 3 with three stores, and an existing
      activity draft from PPDO-112 is still offered after the upgrade.

## 11. Test focus

Vitest (`npm test`), pure logic, TDD:

- The expenditure decision function: no draft; equal to the saved line → delete; new line matching
  a saved line → delete; base matches → restore; base differs → restore-changed; line gone →
  restore-as-new; empty new-line draft → delete.
- Key building with and without a user.
- The generalised session (debounce, flush, saved, conflicted, discarded) still passes PPDO-112's
  existing tests unchanged. That is the guard that the generalisation broke nothing.
- `aip-cache-db.test.ts`: fresh database has three stores; **v2 → v3 keeps both existing stores'
  entries**; **v1 → v3 in one step** works (a user who skipped v1.8.2's first deploy).
- Red-test: remove the user axis, the delete-on-save, and the 4a duplicate guard; each must fail a
  test.

Not unit-testable here (Node-only runner, no DOM): the wiring in `AipExpenditureTable`. Covered by
§10.
