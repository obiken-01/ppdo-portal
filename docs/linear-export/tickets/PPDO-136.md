---
id: PPDO-136
title: 'Demo 2.14 — Admin-configurable default fiscal year for Investment Planning pages'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-30
parent: PPDO-122
blocked_by: (none)
related: PPDO-153, PPDO-177
url: https://linear.app/ralphoksiprojects/issue/PPDO-136
uuid: 7e3cc428-35ba-4bbc-a0fb-cc922cbceb1d
---

# PPDO-136 — Demo 2.14 — Admin-configurable default fiscal year for Investment Planning pages

Logged 2026-09-23, from a demo 2 follow-up: Ralph wants an admin to be able to set which fiscal
year Investment Planning pages default to, rather than each page picking one on its own (today
that's effectively "current calendar year" or "the one open record", depending on the page).

## Why this matters

Investment Planning spans several pages — AIP Entry, AIP Review, WFP, Report, the dashboard's
readiness board — and each currently resolves "which fiscal year" independently. As FY2028
entry ramps up alongside FY2027 close-out work, encoders and reviewers need a single, admin-set
default so they land on the right year without hand-editing a `fiscalYear` query param on every
page, every time the working year rolls over.

## Feature — needs a decision before a spec

Open questions this needs answered before it can be ticketed properly (see `docs/SPEC_STANDARD.md`
§2):

* **Scope of "default"**: one province-wide default (PPDO sets it once for everyone), or
  per-office (a guest office might reasonably still be closing out FY2027 while PPDO has already
  moved its own encoders to FY2028)?
* **Where does it live?** A new config value (e.g. alongside the existing config pages) vs. a
  computed default (e.g. "the newest AIP record not yet Archived"). A computed default needs no
  admin screen at all but can't represent "PPDO wants everyone to start seeing FY2028 pages on
  October 1st before any FY2028 AIP record is even open yet."
* **Which pages actually read it?** AIP Entry and Review clearly. Does WFP/Report, whose fiscal
  year selection today may follow a different convention?
* **Override**: can a user still manually pick a different year (e.g. to check FY2027 numbers
  after the default has moved to FY2028), or does the admin default become the only year offered?

## Non-goals (tentative, confirm when scoped)

* Not a per-user preference — this is an admin-level default, not "remember what I last picked."
* Not changing how a fiscal year is opened/closed (`AipRecord.Status` lifecycle) — purely which
  year loads by default.

Needs a spec (`docs/SPEC_STANDARD.md`) before implementation, same as every other v1.8.0 feature
item in this epic.
