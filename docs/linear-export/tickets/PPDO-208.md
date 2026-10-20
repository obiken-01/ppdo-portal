---
id: PPDO-208
title: 'AIP Entry: rename "Activity Description" to "Activity Title" and make it a multi-line field'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.4 — Minor fixes
created: 2026-10-14
completed: 2026-10-14
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-208
uuid: d62ef47d-23d4-4fdf-be72-3a09cdded6ce
---

# PPDO-208 — AIP Entry: rename "Activity Description" to "Activity Title" and make it a multi-line field

From Demo 3 (2026-10-13).

## Current

The activity's name is labelled **"Activity Description"** (label and placeholder) and is a short single-line input, both when first creating an activity and in **Edit Details**.

## Target

1. Label and placeholder read **"Activity Title"** everywhere the activity name is entered or edited in AIP Entry (create + Edit Details). Read-only displays that label the same field follow the same wording.
2. The field is a **multi-line textarea**, wider than today, in both the create flow and Edit Details. Follow the existing long-text rule (min-height 44px, max-height 88px, resize vertical) unless the layout calls for more.
3. Enter must not submit the form from inside the textarea.

## Notes

* No migration: `AipActivity.Description` is already max 1000 characters. The column/property name stays `Description`; this is UI wording only.
* Excel/printed exports keep their own column headers (province form), not in scope.

## Acceptance

- [ ] Create an activity: the field says "Activity Title", wraps onto multiple lines, saves a long (500+ char) title.
- [ ] Edit Details on an existing activity: same label and multi-line field, value round-trips.
- [ ] No "Activity Description" string remains in AIP Entry UI.

Branch: `fix/v1.8.4-…` → PR into `release/1.8.4`.
