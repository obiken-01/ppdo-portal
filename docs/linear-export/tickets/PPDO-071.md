---
id: PPDO-71
title: 'V18-53 — Row-anchored review comments with resolve'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-08
completed: 2026-09-08
parent: PPDO-67
blocked_by: PPDO-68
related: PPDO-76, PPDO-77, PPDO-79
url: https://linear.app/ralphoksiprojects/issue/PPDO-71
uuid: e3da12dd-0f38-4236-8864-6df2becdbf95
---

# PPDO-71 — V18-53 — Row-anchored review comments with resolve

> ✅ **Done — PR #314, verified live.** Both resolve directions refuse, an encoder cannot comment, commenting works while the office is locked at PPDO, and the tally splits two ways in the UI.
>
> ⚠️ **The teeth check caught a hole in my own tests.** Inverting the resolve rule failed only ONE test — the encoder case passes either way, because an encoder holds no side at all, so it was covered by "not a reviewer" rather than by the side rule. Added the discriminating case: **a PPDO reviewer resolving a department head's comment**.
>
> ⚠️ **Migration** `AddAipReviewComments` (additive, one table). Applied locally; still needed against Azure SQL.
>
> ℹ️ **Scope note:** the filter chips expand and highlight matching rows rather than filtering the tree down to them — on the entry page the tree is the encoder's work surface. Filtering the tree itself belongs to [PPDO-74](https://linear.app/ralphoksiprojects/issue/PPDO-74/v18-56-ppdo-cross-office-review-screen)/[PPDO-76](https://linear.app/ralphoksiprojects/issue/PPDO-76/v18-75-query-first-review-page).

**In one line:** reviewers leave comments on a row, GitHub-style; the side that wrote a comment is the only side that may resolve it.

**Plan:** `Phase_Plan.md` §6, §12.5, §12.8 Q6 · **Size:** L · **Blocked by:** [PPDO-68](https://linear.app/ralphoksiprojects/issue/PPDO-68/phase-4-spec-aip-review-specmd) (spec) · ⚠️ **MIGRATION**

## Settled — and narrower than DECISION 10 recorded

|  |  |
| -- | -- |
| **Anchor** | **The row** — a gutter control on each program / project / activity row, the way GitHub anchors to a line. **Not** a field, **not** an expense line: the AIP grid is wide and field-level anchoring multiplies anchor points for no reviewer benefit |
| **Whole-submission comment** | ❌ **Does not exist.** B10 removed it |
| **Threading / replies** | ❌ **None.** Resolve is the only action on a comment |
| **Default visibility** | **Collapsed.** Rows carrying comments get a marker and a show-comments control; the body opens deliberately, so the grid stays readable |
| **Who authors** | Department-head reviewer and PPDO reviewer. ⚠️ **The encoder never comments** — that is why the count splits two ways, not three |
| **Deletion** | Resolved comments are **marked, never deleted** — they feed [PPDO-77](https://linear.app/ralphoksiprojects/issue/PPDO-77/v18-77-submission-history-and-show-history)'s "Show History" |

## ⚠️ Only the authoring side resolves — the one rule that must not be built backwards

An office may **not** resolve a PPDO reviewer's comment, and an encoder may **not** resolve their department head's. The recipient never clears the comment addressed to them.

Get this wrong and the soft re-submit gate becomes self-marking: the person being asked to change something clears the ask and re-submits having addressed nothing. That failure is silent — everything looks resolved.

## The unresolved count is a filter, not just a number

Because comments are collapsed by default, someone could re-submit having never opened one. So the count appears **on screen as filter buttons split by author** (department-head reviewer vs PPDO reviewer), and **the same split appears in the re-submit warning** (PPDO-72's soft gate). One merged number would imply an action the reader cannot take — they cannot resolve either set — and *"3 unresolved from PPDO"* is the sentence that actually changes whether someone re-submits.

## Scope

* New table + migration (snake_case, per `NAMING_CONVENTIONS.md`) — comment, author, authoring **side**, anchored node, resolved flag + who/when.
* Endpoints: create, list for an office, resolve. ⚠️ Commenting must **not** route through `ReviewerWriteGuard` — its own remarks say so.
* UI: gutter control, row marker, collapsed body, per-source filter buttons with counts.

## Watch for

⚠️ **The anchor is a tree node, not a grid coordinate.** Rows are programs, projects and activities in one tree, and a row's screen position changes as siblings are added. Anchor to the node's identity.
