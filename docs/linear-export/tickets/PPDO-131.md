---
id: PPDO-131
title: 'Demo 2.9 — Draft feature'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-112, PPDO-130, PPDO-166
url: https://linear.app/ralphoksiprojects/issue/PPDO-131
uuid: 2e21f7f7-458a-4011-bef3-f94dd67c30d4
---

# PPDO-131 — Demo 2.9 — Draft feature

*"Draft feature. Note: we can set a low priority for this specially when the effort will be big."*

Filed at low priority per that note, and deliberately **not scoped** — the requirement is one word, and guessing at it would produce a spec nobody asked for.

## What needs answering first

"Draft" already has at least three plausible meanings here, and they are different features:

1. **A saved-but-not-submitted AIP** — largely exists: `workflow_status` already has a Draft state and returning an AIP puts it back there.
2. **Per-activity draft rows** that don't count toward totals or the ceiling check until finalised — new, and it complicates every sum in the system.
3. **A local unsaved-work buffer** so an encoder doesn't lose typing — that's the offline/caching groundwork already written up in `docs/v1.8/AIP_Offline_Caching_Spec.md` (V18-64…67, deferred to v1.8.1).

If it's (3), much of the thinking is done and [PPDO-112](https://linear.app/ralphoksiprojects/issue/PPDO-112/v18-64-indexeddb-draft-mirror-for-aip-entry-with-restore-prompt) is the existing ticket. If it's (2), it needs a full spec and it is genuinely big — that is probably the effort Ralph was flagging.

## Next step

Ask the office what they meant, in their words, before estimating anything. Do not start from this ticket.
