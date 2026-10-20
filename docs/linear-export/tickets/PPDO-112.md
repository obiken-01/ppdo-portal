---
id: PPDO-112
title: 'V18-64 — IndexedDB draft mirror for AIP Entry, with restore prompt'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-21
completed: 2026-10-07
parent: (none)
blocked_by: (none)
related: PPDO-131, PPDO-166
url: https://linear.app/ralphoksiprojects/issue/PPDO-112
uuid: e68d8cdb-6932-4266-a7a2-081f0f5cedd9
---

# PPDO-112 — V18-64 — IndexedDB draft mirror for AIP Entry, with restore prompt

Spec: `docs/v1.8/AIP_Offline_Caching_Spec.md` — decision 7; §3 rows "Draft recovery", "Draft cleared", "Edge: two tabs"; §5; §6.

**Independent of the other three tickets** — it does not need the reference-data cache module, and can land before or after it. It does create the same IndexedDB database, so whichever lands second reuses the existing `openDb` helper rather than writing a second one.

## Context to read first

* `docs/v1.8/AIP_Offline_Caching_Spec.md`
* `frontend/src/app/(portal)/budget-planning/wfp/page.tsx` — **this is the pattern being ported**, not invented. It already mirrors in-progress edits to `localStorage`, compares a saved timestamp against the server record's `updatedAt`, and offers "Restore?" when the local copy is newer.

## Files to read before coding

* `frontend/src/app/(portal)/budget-planning/aip/entry/page.tsx` and the activity edit panel under `frontend/src/components/aip/entry/`
* The WFP draft block above — read it before writing anything

## Current behaviour

An encoder typing an activity's name, description or expected outputs loses everything if the tab crashes or is closed before saving. There is no recovery. AIP Entry has no local mirror at all; WFP has one, per-page and ad hoc.

## Target behaviour

Debounced writes of the activity's descriptive fields (name, eSRE code, implementing office, dates, expected outputs) to IndexedDB, keyed by activity id. On reopening that activity, if the draft's timestamp is newer than the field's last-saved value, show a dismissible inline banner:

> "A local draft from `<time>` was found for this activity. Restore it? \[Restore\] \[Discard\]"

Same wording and placement as the WFP pattern.

⚠️ **This is a crash / accidental-close safety net, not an offline-editing feature.** It works whether or not the user is online, and it does not let anyone create or modify an activity without a connection — every write still needs a live `PUT`/`POST`. Do not let the ticket drift toward offline editing; that is V18-68, deferred and undesigned.

## Steps

1. IndexedDB store `activity-drafts`: key `<activityId>`, value `{ name, esreCode, implementingOffice, startDate, endDate, expectedOutputs, savedAt }`.
2. Debounced write on field change — off the main thread's synchronous-block risk, which is the reason for IndexedDB over `localStorage` here (that, no \~5 MB ceiling, and structured data instead of a string blob).
3. On activity open, compare `savedAt` against the server value's last-saved timestamp; show the banner only when the draft is newer.
4. ⚠️ **Delete the draft on a successful save.** A stale draft resurfacing after a real save is the failure that makes the feature worse than not having it.
5. Discard deletes immediately and shows the server value.
6. ⚠️ Fall back silently to no-draft-mirror if IndexedDB is unavailable — never a broken page.

## Deliberate non-decisions

**Two tabs, same activity:** each tab keeps its own draft keyed by activity id. The last save wins for the server record, exactly as today. No cross-tab merging — that is V18-71's concurrency problem, and it is a real one (two or more encoders per office is confirmed normal), but it is not this ticket's.

## Out of scope

Cross-tab or cross-device draft sync. Expenditure-line drafts — descriptive fields only. Retrofitting WFP's `localStorage` mirror onto this module (a later call; a shared module is the natural shape either way).

## Acceptance

- [ ] Type into an activity's name field, close the tab without saving, reopen the activity — a "Restore draft from `<time>`?" prompt appears with the typed text intact
- [ ] Restore fills the field with the draft text; Discard clears the draft and leaves the server value showing
- [ ] Saving an activity clears its draft — reopening shows no prompt
- [ ] A draft older than the last save shows no banner
- [ ] With IndexedDB unavailable, AIP Entry still loads and saves normally, just without the safety net

## Test focus

No frontend test runner exists today. If one is added, the draft-vs-server "which is newer" comparison is worth covering — the WFP original is untested and this is a port, not a blind copy.
