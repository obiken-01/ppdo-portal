---
id: PPDO-114
title: 'V18-67 — eSRE codes from config, not the hardcoded AIP_ESRE_OPTIONS array'
status: Duplicate
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-09-21
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-114
uuid: 2a626883-d647-481b-ad8b-406bf6d4f6d3
---

# PPDO-114 — V18-67 — eSRE codes from config, not the hardcoded AIP_ESRE_OPTIONS array

Spec: `docs/v1.8/AIP_Offline_Caching_Spec.md` — decision 4.

**Blocked by** [PPDO-111](https://linear.app/ralphoksiprojects/issue/PPDO-111/v18-65-shared-reference-data-cache-idb-wired-into-aip-entry) **(V18-65)** — folds eSRE into the same reference-data cache rather than adding a second fetch path.

## Context to read first

* `docs/v1.8/AIP_Offline_Caching_Spec.md` decision 4

## Files to read before coding

* `frontend/src/lib/aipConstants.ts` — `AIP_ESRE_OPTIONS`, the array being deleted
* `backend/PPDO.Domain/.../AipEsreCode.cs` — `AllowedValues`, the list the backend actually validates against
* The existing eSRE config page and `GET /api/config/esre-codes` ([RAL-248](https://linear.app/ralphoksiprojects/issue/PPDO-25/config-esre-code-table-management-page), already shipped)

## Current behaviour

The eSRE picker's options come from `AIP_ESRE_OPTIONS`, a hardcoded array in `lib/aipConstants.ts`. Meanwhile eSRE has had a real config table, management page and API since [RAL-248](https://linear.app/ralphoksiprojects/issue/PPDO-25/config-esre-code-table-management-page).

⚠️ **The two can drift silently.** The frontend array is what an encoder picks from; `AipEsreCode.AllowedValues` is what the server validates against. Add a code in Configuration and it does not appear in the picker. Change one side and the mismatch surfaces as a rejected save with a confusing message, not as a build error.

## Target behaviour

The picker reads `GET /api/config/esre-codes` through the V18-65 cache, so it is both correct and instant on a repeat visit. `AIP_ESRE_OPTIONS` is deleted.

## Steps

1. Add `esre-codes` to the cached reference kinds (key `esre-codes` — not office-scoped; the endpoint reads as any authenticated user and returns the same list for everyone).
2. Point the eSRE picker at it.
3. Delete `AIP_ESRE_OPTIONS` and fix every reference.
4. Confirm the picker's values still satisfy `AipEsreCode.AllowedValues` — if Configuration holds a code the backend rejects, that is a **data** problem to surface now, not to paper over in the frontend.

## Deliberately not in scope — two near-neighbours in the same file

Both were investigated for the spec and both stay as they are:

* `AIP_SECTOR_PREFIX` — already commented as an intentional preview-only mirror; the server computes the real ref code. Not a bug.
* `AIP_EXCEL_CHARS_PER_LINE` **/** `MAX_PRINTABLE_LINES` ([PPDO-85](https://linear.app/ralphoksiprojects/issue/PPDO-85/long-aip-activity-names-keep-line-breaks-on-screen-grow-the-entry-box)) — a soft, approximate warning heuristic. Turning it into a server round-trip would be more machinery than the feature justifies.

Do not "finish the job" by moving these too.

## Acceptance

- [ ] `AIP_ESRE_OPTIONS` no longer exists in `lib/aipConstants.ts`
- [ ] The eSRE picker's options match exactly what Configuration → eSRE Codes shows
- [ ] Adding a code in Configuration makes it selectable in AIP Entry within one page load
- [ ] Saving an activity with any offered code succeeds — no server-side rejection from a code the picker offered
