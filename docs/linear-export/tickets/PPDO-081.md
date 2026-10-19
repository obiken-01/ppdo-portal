---
id: PPDO-81
title: 'AIP Entry: hub handoff, page gating (AIP admin-only, LDIP host-office), optional CC typology, expenditure column alignment'
status: Done
project: PPDO Portal
labels: 
milestone: 
created: 2026-09-08
completed: 2026-09-08
parent: PPDO-48
blocked_by: (none)
related: PPDO-61, PPDO-80
url: https://linear.app/ralphoksiprojects/issue/PPDO-81
uuid: 82133e8f-5eaa-4bf0-bdd6-cf3e3b3b3076
---

# PPDO-81 — AIP Entry: hub handoff, page gating (AIP admin-only, LDIP host-office), optional CC typology, expenditure column alignment

Follow-on to [PPDO-80](https://linear.app/ralphoksiprojects/issue/PPDO-80/aip-entry-fund-sources-psmooeco-cc-totals-on-the-row-form-order-unit), shipped in the same PR (#315).

## 1. The hub sends people to AIP Entry, not the record list

The Budget Planning readiness hub's action card said "Open AIP" and went to `/budget-planning/aip` — the record list, whose only action (`+ New AIP`) was already denied to an office encoder. It now reads **AIP Entry** and links to `/budget-planning/aip/entry`, carrying `?fiscalYear=` so the hub and the page agree on the year being planned. The entry page validates that param against the years it can offer and falls back rather than erroring.

## 2. The AIP record list is Admin/SuperAdmin; LDIP is host-office

Two rules, two reasons:

* **AIP list/detail/new/import-preview → Admin + SuperAdmin.** Since [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal)/62 the base record is created once per fiscal year by an Admin; `aip/entry` deliberately has no create path so there is one create route, not two.
* **LDIP → host office (PPDO), any role.** Gated on the *office*, not the role: PPDO planning staff work in the LDIP daily, but from FY2028 it is the closed list an AIP's programs must come from, so a guest office arriving to add a missing program finds every write control already Admin-refused.

Both rules live in `lib/budget-planning-access` so the sidebar and the route guards cannot drift.

⚠️ **Accepted consequence:** `aip/detail` is also the read view for FY≤2027 uploaded AIPs, so a guest office no longer has a route to last year's approved document. That data is PPDO-held today. If it is ever needed, the answer is a read-only view — not re-opening a list whose create and finalize actions are what had to be contained.

Incidentally fixes the sidebar highlighting **AIP** and **AIP Entry** together: the old `isActive("/budget-planning/aip")` prefix-matched its own sibling.

## 3. CC typology no longer blocks submit

Column (14) is filled only for an activity that actually carries a climate-change component, and most do not. The gate refused a blank, forcing encoders to invent a code for every ordinary operating activity — fiction in a column the province reports on. `missing-cc-typology` is removed from `AipSubmitService`, and the "needed to submit" marking is off both the read and edit views.

**eSRE stays required** — it classifies every activity, so a blank there is genuinely incomplete. The two were introduced together and read as a pair, but they are not one rule.

## 4. Expenditure table columns stay put

Under the browser's default `table-auto`, widths are recomputed from the widest cell — so the moment a row entered edit mode, its account picker and three money inputs seized width from the numeric columns and every row shifted under a header that no longer sat above the right figures. Fixed with `table-fixed` plus one colgroup; the account column is deliberately unsized so it absorbs the remainder rather than starving the numbers.

## Verification

1,895 backend tests pass. Live: the hub links to `/aip/entry?fiscalYear=2028`; `?fiscalYear=2029` lands on FY2029; a guest office (`pto.user`) sees only Dashboard and AIP Entry, and direct URLs to `/aip` and `/ldip` both redirect to the hub; every expenditure column centre matches its header to within 1px on both saved and editing rows.
