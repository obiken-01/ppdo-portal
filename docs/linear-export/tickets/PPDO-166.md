---
id: PPDO-166
title: 'AIP Entry — warn before leaving unsaved edits (expenditure lines, procurement items, activity/project/program fields)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-30
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-112, PPDO-130, PPDO-131, PPDO-160
url: https://linear.app/ralphoksiprojects/issue/PPDO-166
uuid: 4c206333-6782-4ae2-8cb8-510e75391764
---

# PPDO-166 — AIP Entry — warn before leaving unsaved edits (expenditure lines, procurement items, activity/project/program fields)

Found by Ralph on 2026-09-30 during the Demo 2.8 run, as `pto.head`. He changed a price-index expenditure line on AIP Entry (the procurement item quantities, and with them the total), clicked **Done**, and the change was gone. Nothing warned him.

## Why

An expenditure line being added or edited, its procurement items, "Edit details" on an activity, and project/program field edits all hold their changes in component state until that editor's own **Save**. Every way off the editor discards that state silently. AIP Entry has no unsaved-changes check at all; WFP Entry already has one ("Discard unsaved changes?", comparing against a snapshot taken on open).

## Decisions (Ralph, 2026-09-30)

1. **A warning guard now.** Keeping unsaved edits in memory and restoring them is to be **studied for later**: it is [PPDO-112](https://linear.app/ralphoksiprojects/issue/PPDO-112/v18-64-indexeddb-draft-mirror-for-aip-entry-with-restore-prompt) (IndexedDB draft mirror, v1.8.1) and the likeliest meaning of [PPDO-131](https://linear.app/ralphoksiprojects/issue/PPDO-131/demo-29-draft-feature) "Draft feature". It is not in this ticket.
2. **Dialog: Keep editing / Discard**, the same as WFP Entry. No "Save and continue".
3. **Exits caught:** Done, Change activity, Change project, the program/project/activity pickers, tree/child rows, checklist and comment jumps, sidebar and other in-app links, and reload/close (the browser's own prompt). **The browser Back button is not caught**: Next.js has no clean hook for it.

## Behaviour

| Given | When | Then |
| -- | -- | -- |
| An editor has a change that differs from what was loaded | The user takes any exit above | Dialog "Discard unsaved changes?" names what is unsaved. **Keep editing** leaves everything as it was; **Discard** drops the edit and completes the exit |
| An editor is open but nothing was changed (or it was changed back) | Exit | Proceeds silently, with no dialog |
| Unsaved edit | Reload / close the tab | Browser's native "Leave site?" prompt |
| Unsaved edit | Clicks the editor's own **Cancel** | Unchanged (Cancel is an explicit discard) |
| Unsaved edit | Clicks the editor's own **Save** | Unchanged; once saved, nothing is dirty |
| Save fails validation | Exit | Still dirty, so it still warns |

## Non-goals

* Restoring unsaved edits after leaving or reloading ([PPDO-112](https://linear.app/ralphoksiprojects/issue/PPDO-112/v18-64-indexeddb-draft-mirror-for-aip-entry-with-restore-prompt) / [PPDO-131](https://linear.app/ralphoksiprojects/issue/PPDO-131/demo-29-draft-feature), to study)
* Catching the browser Back button
* Other pages (WFP Entry already has its own guard)

## Acceptance

- [ ] Edit a price-index line's quantities, click **Done**: the dialog appears. Keep editing keeps the values; Discard closes the editor and the old total stands
- [ ] Same for Change activity, Change project, picking another activity, and a checklist/comment jump
- [ ] Unsaved edit + a sidebar link (e.g. Dashboard): the dialog appears; Discard navigates
- [ ] Unsaved edit + reload: the browser's Leave-site prompt appears
- [ ] Open an editor, change nothing, click Done: no dialog
- [ ] The same guard works for a new (not yet saved) expenditure line, activity "Edit details", and project/program field edits
