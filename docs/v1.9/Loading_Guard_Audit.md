---
type: audit
version: v1.9.1
status: reference
tickets: [PPDO-244]
updated: 2026-10-21
status_note: "findings; small fixes made in the same PR"
date: "2026-10-21"
---

# Loading guard audit — heavy portal pages

Up: [v1.9 hub](README.md)

**Question (Ralph, 2026-10-21):** can a user click a button, or edit a field, before the data it depends on has loaded? And can a fast double-click send a save twice?

**Scope:** the ten heavy pages named in PPDO-244. For each one: the actions, the data each needs, and whether the action waits for it. Small fixes were made in the PR that added this doc. Nothing found was big enough to need its own ticket.

**How it was checked:** reading each page's loading states, initial values and `disabled` rules, then live runs on a local copy for the pages marked *live*. The page-wide top loading bar and the double-click fixes are in PR A ([#532](https://github.com/obiken-01/ppdo-portal/pull/532)).

## Summary

| Page | Verdict | Fixed here |
|---|---|---|
| AIP Entry | Guarded (full skeleton) | Account and Fund pickers in the line editor: disabled with "Loading…" until their lists arrive |
| AIP Review | Guarded (full skeleton) | — |
| Investment Proposals editor | Guarded (section-shaped skeleton) | — (the Ctrl+S double-save is fixed in PR A) |
| WFP Entry | **Gap: wrong-activity add** | Activity switch clears the old lines and ref; skeleton row; "+ Add expenditure" waits |
| Allocation | Small gap | Office picker disabled with "Loading offices…" until the list arrives |
| Office Ceilings | Small gap | Carry-forward waits for the year's cards; year select says "Loading…" |
| Report | Guarded | — |
| Receive Delivery | **Gap: stale rows on PR switch** | Rows cleared on switch, stale answer dropped, Submit waits |
| Distribution | Small gap (race) | A slower answer for an earlier item is dropped |
| Create PR | Small gap | Division select says "Loading divisions…" and waits |

## Per page

"Guarded?" means the action is blocked, hidden or harmless until its data is in.

### AIP Entry (`budget-planning/aip/entry/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Fiscal year select | Default year | Yes: `disabled={!yearSettled}` | — |
| Program / project / activity pickers | The AIP record | Yes: whole page is `EntrySkeleton` while `loading` | — |
| Submit / Send to PPDO / division submit | Readiness, divisions | Yes: the checklist renders only when `readiness && divisionsKnown`. The button waits on `checkingProposals` and `submitting` | Double-click lock in PR A |
| Return to encoders / division return | Readiness, divisions | Yes: same panel, `busy={submitting}` | — |
| Line editor: Account picker | Accounts list (reference cache) | **No** on a cold cache: an empty list was searchable | ✅ Disabled, placeholder "Loading…" |
| Line editor: Fund picker | Funding sources | **No** on a cold cache | ✅ Same |
| Line editor: Save | The open line | Yes: `busy` set before the request (double-click: **live**, 1 PUT) | — |
| Price-index item picker | Price index | Yes: `priceIndexLoading` passed down | — |

### AIP Review (`budget-planning/aip/review/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Drill-down pickers | The office review | Yes: `ReviewSkeleton` while `loading` | — |
| Send back / Accept / Re-open | The office's status | Yes: rendered after load, `disabled={acting}`, confirm dialog closes on the first click | — |

### Investment Proposals editor (`budget-planning/proposals/edit/page.tsx`, `components/proposals/editor/ProposalEditor.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Section fields | The proposal | Yes: `ProposalEditorSkeleton` (header, rail and section-shaped skeletons) | — |
| Save section / Save all | The proposal's rowVersion | Button: yes (`saving`). **Ctrl+S: no**. Two presses sent 2 PUTs and the 2nd got a false 409 (**live**) | Fixed in PR A (`useInFlightLock`) |
| Finalize / Reopen | Status | Yes: `busy`, confirm dialog | — |
| Create proposal (picker) | Project options | Yes: `disabled={creating}` | — |

### WFP Entry (`budget-planning/wfp/entry/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Office / division / AIP selects | Resources | Yes: `LoadingState` while `resourcesLoading` | — |
| Program / project / activity lookups | The AIP detail | Yes: skeleton while `loading` | — |
| **+ Add expenditure** | The **selected** activity's WFP ref | **No.** Switching activity A → B kept A's ref until B loaded, so a fast click opened the wizard for A while B was shown | ✅ Ref and lines cleared when the switch starts; button also waits on `loadingActivity` |
| Expenditure list | The selected activity's lines | **No.** A's lines showed under B's header until B loaded | ✅ Cleared on switch; one skeleton row (same height as the empty state) while loading |
| Edit / Delete a line | That line | Yes once the list is right (above) | — |
| Wizard Save | Form | Yes: `Modal.PrimaryButton loading={saving}` | — |

### Allocation (`budget-planning/allocation/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Fiscal year | Default year | Yes: `disabled={!yearSettled}` | — |
| Office picker (SuperAdmin / finance) | Office list | **No**: enabled and searchable while empty | ✅ Disabled, placeholder "Loading offices…" |
| Ceiling, division allocation, PPA edits | Office setup | Yes: skeleton while `loading || !yearSettled`; saves disabled on `saving…` | — |

### Office Ceilings (`budget-planning/office-ceilings/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Fiscal year select | Year list | **No**: an empty select | ✅ Disabled with a "Loading…" option until the years arrive |
| Carry forward from FY−1 | This year's cards (which offices have no ceiling) | **No**: while a new year loads, `cards` still holds the old year's, so the count was wrong | ✅ Also disabled while `loading` |
| Card Save | The card | Yes: skeleton while loading; `disabled={dirtyCount === 0 || card.saving}` | — |

### Report (`budget-planning/report/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Office select (WFP) | Offices | Yes: `disabled={officesLoading || offices.length === 0 …}` | — |
| Division select | Divisions | Yes: `disabled={officeId == null || !divisionsLoaded …}` | — |
| Generate Preview (WFP) | An office | Yes: `disabled={officeId == null || reportLoading}` | — |
| Generate Preview (AIP) | Year, optional office | Harmless: a blank office is the consolidated scope, a real choice | — |
| Excel export / bulk export | The preview / job | Yes: `excelExporting`, `bulkExport.loading` | — |

### Receive Delivery (`inventory/receive-delivery/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| PR picker | PR list | Yes: `disabled={loading}`, "Loading PRs…" | — |
| Item quantities | The selected PR's lines | Yes: `TableSkeleton` while `prLoading` | — |
| **Submit** (two buttons) | The selected PR's lines | **No.** On a PR switch, the previous PR's rows and typed quantities stayed, and Submit stayed enabled. A slower answer for the old PR could also land after the new one | ✅ Rows cleared on switch, stale answer dropped, Submit also waits on `prLoading` |

### Distribution (`inventory/distribution/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Distribute (open form) | The item's summary | Yes: the card renders only when `summary && !summaryLoading` | — |
| Item search → summary | — | **Race:** picking item A then B quickly could show A's summary if A answered last | ✅ Request counter: only the latest pick is shown |
| Submit distribution | Rows | Yes: `disabled={!canSubmit || submitting}` | — |

### Create PR (`inventory/create-pr/page.tsx`)

| Action | Needs | Guarded? | Fix |
|---|---|---|---|
| Division select | Inventory divisions | **No**: an empty list showing "— Select Division —" | ✅ Disabled, "Loading divisions…" |
| Stock No. / Description lookup | Typed term | Yes: it is a search, nothing to preload | — |
| Upload Excel / GSO import | File | Yes: `uploading`, `gsoImporting` | — |
| Submit | Header + items | Yes: client validation, then `submitting` before the request | — |

## Double-click on the main saves

The full button-by-button table, and the before/after Network evidence, is in PR A ([#532](https://github.com/obiken-01/ppdo-portal/pull/532)). In short:

- **One real double-send:** Ctrl+S in the proposal editor (2 PUTs, the 2nd → 409). Fixed with a synchronous lock.
- **One near-miss:** the AIP submit button re-enabled during the allocation re-read, so a second click built the confirmation twice (reads only, no double submit). Locked too.
- **Everything else** sets its `saving` / `submitting` / `busy` state before any `await` and disables the button, or sends from a ConfirmDialog that closes on the first click. React 18 re-renders before the second click lands. Checked live on the AIP expenditure Save (double-click → 1 PUT).

## Not checked

- **Slow-network runs.** A per-page pass with DevTools "Slow 3G" was not done (the in-app browser has no throttling). The fixes above came from reading the code, then live runs at normal speed. Worth one throttled pass on UAT before training.
- Pages outside the ten heavy ones (config pages, LDIP, dashboard). Most are a single table behind `TableSkeleton`.
