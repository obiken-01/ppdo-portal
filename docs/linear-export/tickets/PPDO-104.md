---
id: PPDO-104
title: 'A second scrollbar appears on every portal page in Edge — sr-only legend escapes the app shell'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-97
url: https://linear.app/ralphoksiprojects/issue/PPDO-104
uuid: 4da9d832-63e7-45f6-89f6-aea2f7a9352c
---

# PPDO-104 — A second scrollbar appears on every portal page in Edge — sr-only legend escapes the app shell

**Regression from** [PPDO-97](https://linear.app/ralphoksiprojects/issue/PPDO-97/aip-entry-expenditure-form-add-account-psmooeco-by-account-class)**.** Ralph reported a second vertical scrollbar on AIP Entry; reproduced in Edge, not Chrome, and confirmed present with all extensions disabled (InPrivate).

## Root cause — measured, not guessed

`document.scrollingElement` was `clientHeight 698 / scrollHeight 1047` — the document overflowed by **349px**. `<body>`'s only non-zero child was the app shell at exactly 698px, so the shell was correct and something inside it was escaping.

Exactly one element extended past the fold:

```
{ t: "LEGEND", c: "sr-only", pos: "absolute", b: 1047 }
```

[PPDO-97](https://linear.app/ralphoksiprojects/issue/PPDO-97/aip-entry-expenditure-form-add-account-psmooeco-by-account-class) added a fund-mode radio group as `<fieldset>` + `<legend className="sr-only">Funding mode</legend>`.

* Tailwind's `sr-only` is `position: absolute`.
* The app shell (`(portal)/layout.tsx`, `flex h-screen … overflow-hidden`) is **not** `relative`, so the legend's containing block is the initial containing block — the page.
* `overflow: hidden` only clips descendants whose containing-block chain passes through the clipping element, so the shell never clipped it, and the document grew to fit it.
* Chromium and Edge lay an absolutely positioned `<legend>` out differently, which is why Chrome looked clean.

## Fix

Replace the `<fieldset>` / `sr-only <legend>` with `<div role="radiogroup" aria-labelledby=…>`, labelled by the **visible** question that was already on screen. Same grouping semantics for screen readers, nothing positioned.

`sr-only` is used nowhere else in the codebase — this was the only instance.

## Considered and not done

Adding `relative` to the app shell would make its `overflow-hidden` clip any future absolute descendant. It is defence-in-depth, but it changes the containing block for every positioned element in the portal, so it deserves its own decision rather than riding along with a one-line fix.

## Acceptance

- [ ] No second scrollbar on AIP Entry in Edge, with an activity selected
- [ ] `[document.scrollingElement.clientHeight, document.scrollingElement.scrollHeight]` returns two equal numbers
- [ ] The fund-mode radios still read as one group to a screen reader
