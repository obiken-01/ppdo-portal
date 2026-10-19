---
id: PPDO-83
title: 'Epic: v1.8.0 Phase 5 — Outputs'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-14
completed: 2026-09-15
parent: (none)
blocked_by: (none)
related: PPDO-12, PPDO-13, PPDO-14, PPDO-15, PPDO-16, PPDO-73, PPDO-86
url: https://linear.app/ralphoksiprojects/issue/PPDO-83
uuid: 91c47bf3-69b2-4d64-b297-5cea3fdd6bd6
---

# PPDO-83 — Epic: v1.8.0 Phase 5 — Outputs

**In one line:** what the AIP produces once offices have entered it and PPDO has reviewed it — the province's official form, and a read-only feed for the other systems that need the AIP.

**Plan:** `docs/v1.8/Phase_Plan.md` §7 · **Form spec:** `docs/v1.8/AIP_Form_Spec.md` · **API draft:** `docs/external-api/` (draft 1.0.0) and `docs/External_AIP_API_Contract.md`

## Scope and order — Ralph, 2026-09-14

1. **AIP Excel file first** — V18-60, the Annex B workbook for the PDC and the Sangguniang Panlalawigan (statutory June 7). Downloaded from the Consolidated AIP page; built from [PPDO-73](https://linear.app/ralphoksiprojects/issue/PPDO-73/v18-55-the-consolidated-view-partial-by-design)'s `AipFormRowBuilder` and `AipPrintedFigures`, so the file and the grid cannot disagree.
2. **Then the external AIP endpoints** for other systems (GSO, PBO, …) — the existing [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-for-gso-phase-2-implementation) epic ([PPDO-12](https://linear.app/ralphoksiprojects/issue/PPDO-12/external-aip-api-4-external-endpoints-aip-aipfiscal-years-health-audit)..15): API-key schema, key authentication, read service, endpoints.
3. **With them, config pages for API-key subscriptions** — issuing, scoping and revoking keys ([PPDO-86](https://linear.app/ralphoksiprojects/issue/PPDO-86/external-aip-api-5-configuration-api-access-page-config-endpoints)).

**Step 1 shipped:** [PPDO-84](https://linear.app/ralphoksiprojects/issue/PPDO-84/v18-60-aip-annex-b-excel-export-from-the-consolidated-aip-page), PR [obiken-01/ppdo-portal#327](https://linear.app/ralphoksiprojects/review/featbudget-planning-aip-annex-b-excel-export-consolidated-aip-sidebar-1ca70c5795e6) (merged 2026-09-14). **Steps 2–3 prepared 2026-09-14:** spec `docs/v1.8/External_AIP_API_Spec.md`; tickets [PPDO-15](https://linear.app/ralphoksiprojects/issue/PPDO-15/external-aip-api-1-partner-api-keys-schema-issuing-service) → [PPDO-13](https://linear.app/ralphoksiprojects/issue/PPDO-13/external-aip-api-2-key-authentication-office-scope-per-key-rate-limit), [PPDO-14](https://linear.app/ralphoksiprojects/issue/PPDO-14/external-aip-api-3-external-aip-read-service-schema-100-pesos-as) (parallel) → [PPDO-12](https://linear.app/ralphoksiprojects/issue/PPDO-12/external-aip-api-4-externalv1-endpoints-health-aip-fiscal-years-schema), and [PPDO-86](https://linear.app/ralphoksiprojects/issue/PPDO-86/external-aip-api-5-configuration-api-access-page-config-endpoints) — all under [PPDO-16](https://linear.app/ralphoksiprojects/issue/PPDO-16/epic-external-aip-api-partner-api-keys-v180-phase-5-step-2), all Todo.

## Already in place

* V18-59's backend half: `AipRounding` + `AipPrintedFigures` (round up per figure, +30% on MOOE/CO for FY2028+, uplift-then-round).
* The row builder and the on-screen preview ([PPDO-73](https://linear.app/ralphoksiprojects/issue/PPDO-73/v18-55-the-consolidated-view-partial-by-design), Consolidated AIP page).

## Not in this epic (yet)

V18-61 Project Profile, V18-62 canonical dataset, V18-63 office data files — may be absorbed by the external API; revisit after step 2.

## Open

* **Tracker G5** — uplift-then-round vs round-then-uplift, provisional (a bare "yes" to an either/or).
* **Covering-note wording** for the printed over-ceiling figures (`AIP_Form_Spec.md` §6.2) — draft wording ships, PPDC to agree.
