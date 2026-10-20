---
id: PPDO-105
title: 'List tables: the record code is the link, long text is clamped — RecordCodeLink + ClampedText'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-17
completed: 2026-09-17
parent: (none)
blocked_by: (none)
related: PPDO-85, PPDO-94
url: https://linear.app/ralphoksiprojects/issue/PPDO-105
uuid: f606b7c5-7fec-46fe-b1d7-f990596d11f9
---

# PPDO-105 — List tables: the record code is the link, long text is clamped — RecordCodeLink + ClampedText

**In one line:** standardize how a list table shows an identifier beside long free text — the code is the click target and is never truncated; the text is clamped at 2 lines with more/less.

**Source:** Ralph, 2026-09-17, on the AIP Review search results: an activity name can be very long, so the name makes a poor click target. Asked for a pattern reusable on other pages. Option A chosen (code is the link, name clamped). · **Size:** S · **Stacked on:** [PPDO-94](https://linear.app/ralphoksiprojects/issue/PPDO-94/aip-review-same-drill-down-as-aip-entry-with-a-full-office-view) (PR [obiken-01/ppdo-portal#350](https://linear.app/ralphoksiprojects/review/feataip-aip-review-drill-down-full-office-view-ppdo-94-9be7a2718f07) changes the same `ResultTable`)

## Scope

1. `components/ui/RecordCodeLink.tsx` — the row's identifier as its one click target. Monospace, `whitespace-nowrap`, **never truncated**; the last segment emphasised the way `AipRefCode` does. Renders a `<Link>` (`href`) or a `<button>` (`onClick`), because the AIP search needs both — activities open a modal, programs and projects navigate. Plain text when neither is given (an unmatched legacy row has nothing to open). Named generically: PR No. (PR List) and Stock No. (Items Master) fit the same shape later.
2. `components/ui/ClampedText.tsx` — extract the WFP page's local clamp helper (line-clamp, more/less, title tooltip when clamped). `lines` prop, default 2. Keeps `whitespace-pre-line` so encoder line breaks survive ([PPDO-85](https://linear.app/ralphoksiprojects/issue/PPDO-85/long-aip-activity-names-keep-line-breaks-on-screen-grow-the-entry-box)). The WFP page moves onto it.
3. `DESIGN_SYSTEM.md` **§5 rule** — in a list table the identifier is the link and nothing else in the row is; long free text is clamped at 2 lines; identifiers are never truncated.
4. **First adopter: AIP Review search.** Reference code becomes the link; the name becomes `ClampedText` and stops being a link.

## Deliberate decisions

* **One link per row.** Two links to the same destination are noise for keyboard and screen-reader users, and a wrapped multi-line link is the problem being fixed.
* **2 lines, not a one-line ellipsis** — one line cuts too much of an AIP activity name; 2 matches the WFP precedent.
* ⚠️ **Exception: the Annex B grid never clamps.** It previews the printed form, and clamping would hide the row heights [PPDO-85](https://linear.app/ralphoksiprojects/issue/PPDO-85/long-aip-activity-names-keep-line-breaks-on-screen-grow-the-entry-box) warns about.
* **Adoption is incremental** — other pages move when next touched, not in this ticket.

## Acceptance

- [ ] On the AIP Review search, clicking a reference code opens the same place the name used to (modal for activities, drill-down for programs and projects)
- [ ] The name is no longer a link; a long name shows 2 lines with "more", and "more" expands it
- [ ] A short name shows no "more"
- [ ] The WFP page's expandable names behave exactly as before
- [ ] `DESIGN_SYSTEM.md` §5 lists both components and the rule
