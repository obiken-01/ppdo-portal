---
id: PPDO-100
title: 'Implementing office becomes a multi-select office picker (slash-separated codes)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-16
completed: 2026-09-16
parent: (none)
blocked_by: (none)
related: PPDO-80
url: https://linear.app/ralphoksiprojects/issue/PPDO-100
uuid: 7409b4ec-268e-4ef3-9f67-829f3cd774a0
---

# PPDO-100 — Implementing office becomes a multi-select office picker (slash-separated codes)

**In one line:** an activity's Implementing Office stops being free text and becomes a multi-select picker over the office config, storing office **codes** joined by `/`.

**Source:** 2026-09-15 PDC/finance demo notes, storage decided 2026-09-16 · **Size:** M · **Demo:** required on or before 2026-09-23

## Decisions (Ralph, 2026-09-16)

1. **No schema change.** `aip_activities.ImplementingOffice` already holds a string that Annex B prints verbatim; several offices are stored in that same column.
2. **Separator is** `/` — the separator the exported form already uses, and the same convention as multi-fund AIP values (`FundingSource.Aliases` doc: "GF/20% DF"). So what is stored is what is printed.
3. **Strict select in use.** Only offices from config can be picked. ⚠️ Build the component so it *can* accept a typed value that is not in the list ("SJDH" + Enter), because Ralph expects to need it later — but **wire this instance strictly**, free entry off.
4. **The encoding office is not shown in the field.** The encoder's own office is the *proponent*; this field names who *implements*. Drop today's `defaultImplementingOffice` prefill of the user's own office code ([PPDO-80](https://linear.app/ralphoksiprojects/issue/PPDO-80/aip-entry-fund-sources-psmooeco-cc-totals-on-the-row-form-order-unit)).

## Scope

* New shared multi-select in `components/ui/` — chips, type-to-filter, remove per chip, `allowCustomValues` prop off here. `Lookup` is single-select (`value: number | null`) and is the component to pattern-match, not to extend.
* `AipActivityFields` reads the stored string, splits on `/`, resolves each segment against the office list, and writes back the joined codes. An **unresolvable segment from an imported FY≤2027 record is kept and shown as a plain chip** — never silently dropped.
* Read-only display (`AipActivityRow`, the consolidated grid, the export) is unchanged: it prints the stored string.

## Acceptance

- [ ] Picking PEO and PGSO stores `PEO/PGSO`, and the Annex B cell reads `PEO/PGSO`
- [ ] Typing a value not in config cannot be committed on this field
- [ ] A new activity's Implementing Office starts **empty**, not prefilled with the encoder's office
- [ ] An imported activity whose value is not an office code still shows that value and does not lose it on save
- [ ] The component's free-entry mode is covered by a test even though this call site disables it
