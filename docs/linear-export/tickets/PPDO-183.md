---
id: PPDO-183
title: 'Perf: stop downloading the whole price index (713 KB) for every picker'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-183
uuid: f0e74ccb-9098-40a7-b5ee-e6ac20a8d02d
---

# PPDO-183 — Perf: stop downloading the whole price index (713 KB) for every picker

From the performance audit, 2026-10-04 (finding **O2**). Optimization ticket 2 of 8. Frontend (+ backend for ETag), size M.

## Current behaviour

* **Whole catalogue every time:** `listPriceIndexForPicker({ active: "true" })` downloads all \~6,400 items (713 KB):
  * on every visit to AIP Entry (`aip/entry/page.tsx:376`) and WFP Entry (`wfp/entry/page.tsx:984`);
  * on every review-modal open for an editor (`AipActivityReviewModal.tsx:108`);
  * on Procurement Presets (`procurement-presets/page.tsx:190`).
* **Server-side search already exists:** `/config/price-index/picker?search=paper` returns 14 KB in 0.11 s.

## Options (decide before starting)

* **A. Cache with ETag (default).** Build on V18-65's shared reference-data cache, which already lists the picker. Add `ETag` / `If-None-Match` to `/picker` so a revisit with no change costs a 304. The picker UI is unchanged.
* **B. Type-ahead.** Call `/picker?search=` as the user types (debounced, from 2 characters). There is no big download at all, but the picker feels different to encoders.

If V18-65 lands first, option A shrinks to "add ETag/304 to the cached endpoints".

## Acceptance

- [ ] Second visit to AIP Entry: the picker request is a 304 (A) or absent until typing (B).
- [ ] Picking an item still fills name, unit, unit price and the days flag.
- [ ] A price edited on the Price Index page shows in the picker on the next visit.
- [ ] Inactive items are still excluded.

## Related

V18-65 (shared reference cache, same milestone).
