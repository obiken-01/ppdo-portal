---
id: PPDO-209
title: 'AIP Entry: default to procurement when the selected account''s default nature is Procurement'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.4 — Minor fixes
created: 2026-10-14
completed: 2026-10-14
parent: (none)
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-209
uuid: fddbfe14-b5f1-4a94-90b2-2abcc982f51d
---

# PPDO-209 — AIP Entry: default to procurement when the selected account's default nature is Procurement

From Demo 3 (2026-10-13).

## Current

WFP Entry pre-fills the expenditure "Nature" from `Account.DefaultNature` ("Procurement" / "Non-Procurement" / "Combined") when an account is picked. AIP Entry does not: picking a procurement account (e.g. office supplies) leaves the user to switch to procurement-item entry by hand.

## Target

When an account is chosen on an AIP expenditure line, and the account's `DefaultNature` is **Procurement**, the line defaults to procurement-item entry (Price Index items), the same way WFP does.

* **Default only, never a gate** (same contract as WFP, see `Account.DefaultNature` doc comment). The user can still switch.
* Only applies when the account is picked/changed on a new or unsaved line. Never flip an existing saved line's mode on load, and never discard entered procurement items or amounts silently.
* Null `DefaultNature` → no change from today's behaviour.
* Check how AIP models "Combined" before mapping it; if AIP has no combined mode, treat Combined as no default (state the choice in the PR).

## Acceptance

- [ ] New line, pick an account whose default nature is Procurement → line is in procurement mode.
- [ ] Pick a Non-Procurement or no-default account → amount entry as today.
- [ ] User can switch away from the default.
- [ ] Re-opening a saved line does not change its mode.
- [ ] Backend routing (`ApplyProcurementRoutingAsync`) still accepts/refuses as before.

Sibling to copy from: the WFP entry wizard's DefaultNature pre-fill.

Branch: `fix/v1.8.4-…` → PR into `release/1.8.4`.
