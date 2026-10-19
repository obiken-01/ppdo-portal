---
id: PPDO-124
title: 'Demo 2.2 — eSRE config is ignored by the AIP dropdowns'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-23
completed: 2026-09-23
parent: PPDO-122
blocked_by: PPDO-111
related: PPDO-25, PPDO-85
url: https://linear.app/ralphoksiprojects/issue/PPDO-124
uuid: 2be9d719-740b-48ae-b1e0-71954be7b207
---

# PPDO-124 — Demo 2.2 — eSRE config is ignored by the AIP dropdowns

*"Values from eSRE config is not used in the dropdown in aip details."*

Confirmed, and it is worse than the detail page: **all three AIP editors ignore the config table.**

| File | Source used |
| -- | -- |
| `components/aip/AipActivityRow.tsx` | `AIP_ESRE_OPTIONS` |
| `components/aip/AipAddActivityRow.tsx` | `AIP_ESRE_OPTIONS` |
| `components/aip/entry/AipActivityFields.tsx` | `AIP_ESRE_OPTIONS` |

`AIP_ESRE_OPTIONS` is a hardcoded four-item list in `lib/aipConstants.ts`:

```ts
export const AIP_ESRE_OPTIONS = [
  { value: "SS", label: "SS — Social Services" },
  { value: "ES", label: "ES — Economic Services" },
  { value: "ID", label: "ID — Infrastructure Development" },
  { value: "EN", label: "EN — Environment" },
];
```

Meanwhile `listEsreCodes()` exists in `lib/config.ts`, backed by a full CRUD config page at `/config/esre-codes` with `code`, `name`, `description` and `isActive`.

⚠️ **So editing eSRE config today changes nothing.** The admin screen is decorative, which is worse than not having one — someone will add a code, see it saved, and never learn it isn't offered.

## Approach

Fetch the active codes once at the page level and pass them down, rather than three components each firing their own request (CLAUDE.md: *"Fetch shared state once"*). Same shape as Demo 2.3, which touches the same three files — do them in one PR.

## Decisions needed

* An activity holding a code that has since been **deactivated** — keep showing it, or blank it? (Suggest: keep it, marked inactive. Silently dropping a saved value loses data.)
* Fall back to the hardcoded list if the config fetch fails, or show an empty picker?

## Acceptance

- [ ] Adding an eSRE code in config makes it selectable in all three editors
- [ ] Deactivating one removes it from the pickers
- [ ] `AIP_ESRE_OPTIONS` is gone, not just unused
- [ ] Existing saved values still render
