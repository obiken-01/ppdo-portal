---
id: PPDO-168
title: 'Sidebar Returned pill / pending count don''t pick up someone else''s hand-off until a full reload'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-75, PPDO-130
url: https://linear.app/ralphoksiprojects/issue/PPDO-168
uuid: feb7add5-5045-4c1d-a741-59105d01e862
---

# PPDO-168 — Sidebar Returned pill / pending count don't pick up someone else's hand-off until a full reload

Found by Ralph on 2026-09-30 during the Demo 2.8 run. `pto.head` returned Cash Division in one window, and `pto.user`'s sidebar in another window only showed the **Returned** pill after a manual refresh.

## Cause

`lib/aip-notifications.ts` is a module-level store: fetched once per portal load, and refreshed only after the reader's **own** submit, return, accept or re-open (AIP Review spec §6.5, "no polling"). It survives client-side navigation, so a hand-off by someone else stayed invisible until a full reload. That covers a division return, an office return, and a division or office submit.

## Decision (Ralph, 2026-09-30)

Refresh on **tab focus / becoming visible** and on **every in-app page change**, throttled to once per 30 s. No background polling. The reader's own actions keep refreshing at once, as before.

## Pre-existing bug found while testing

`load()` stored `run.finally(...)` in `_inflight` but cleared it only when `_inflight === run`, which is never true. So the in-flight marker stuck after the first fetch. It was harmless until something asked "is a fetch in flight?" after the first load; the new refresh asks exactly that, and never fetched.

## Acceptance

- [ ] Another user returns or submits; switching back to the tab, or moving to another page after 30 s, updates the pill and count without a reload
- [ ] Page changes within 30 s of the last fetch send no extra request
- [ ] Your own submit/return still updates the pill at once
