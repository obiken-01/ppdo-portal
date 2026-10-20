---
id: PPDO-76
title: 'V18-75 — Query-first review page'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-09
parent: PPDO-67
blocked_by: PPDO-68
related: PPDO-71, PPDO-74, PPDO-78, PPDO-94
url: https://linear.app/ralphoksiprojects/issue/PPDO-76
uuid: 9ec50c17-8525-4908-9ca9-8f7a2602a3e5
---

# PPDO-76 — V18-75 — Query-first review page

**In one line:** the review page lists nothing by default — work is found by querying for it, because no useful default list exists across 19 offices and thousands of rows.

**Plan:** `Phase_Plan.md` §12.5, §12.7 · **Size:** L · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec)

## How the filters combine — settled 2026-08-26

|  | Rule |
| -- | -- |
| **Within one field** | **OR** — each field is multi-value (pick two sectors, three programs) |
| **Across fields** | **AND** — office AND sector AND title. Not a feature to build; it is what a filter panel does |
| **Boolean expressions** | ❌ **Do not build.** No `A OR (B AND C)`, no precedence, no nesting, no parser |

**There is a working precedent — copy it rather than invent one.** The PR List status filter (`inventory/pr-register`) is multi-select toggle chips with per-value counts, and its own helper text already reads *"Select multiple to combine"*. Reuse that interaction, counts included.

## ⚠️ The ref-code box and the segment filters are NOT interchangeable

The instinct is to query by a ref-code prefix, since the code carries both sector and office. **The segment order defeats that for the most common query:** sector is segment 1, office is segment 5. Pinning the office while letting the sector vary means wildcarding the front of the string and matching the middle — not a prefix, and as a string search it degrades to `LIKE '%…%'`.

**Verified against real data:** 11 offices span more than one sector; office `001` and `017` span three, PPDO's own `010` spans two.

| Input | Good for | Not for |
| -- | -- | -- |
| **Ref-code prefix** | "All offices in a sector" (`3000-`), and subtree drill-down (`…-010-001-`) | "One office, all sectors" |
| **Segment filters** | **"One office, all sectors"** — set office, leave sector blank | — |

Both stay. The ref-code field also accepts a typed `OR` **list** (`1000-…-010 OR 3000-…-010`) — a flat separator, `OR` **only**, parsed to a set never a tree, and **code-shaped fields only**: do not OR-split the free-text title field, where a title may legitimately contain the word "or". A comma is an equivalent separator.

⚠️ **The difference is durability, not capability.** A typed prefix list captures today's sectors — if PPDO later gains a program under `8000`, that query silently returns a short list with no error. `office = 010, sector = blank` picks it up automatically.

## Scope

* Query fields: ref code (whole, prefix, or OR-list), title free-text, office code, sector, and a **"show everything applicable to me"** tag scoped by the caller's own permissions.
* Results link into [PPDO-74](https://linear.app/ralphoksiprojects/issue/PPDO-74/v18-56-ppdo-cross-office-review-screen)'s review screen.
* ⚠️ **Empty by default is the design, not a bug** — but an empty page is a poor landing, which is why PPDO-78's kanban exists. Ship the empty state deliberately (explain how to search), not as a blank panel.

## Watch for

⚠️ Segment queries must hit **indexed columns**, not `LIKE '%…%'` over the formatted string. Confirm what Phase 2 actually shipped for V18-76 before designing the query — the plan reshaped it (segments 1–5 fixed columns; 6+ a `seq` per node) and the ticket must match reality, not the plan's first draft.
