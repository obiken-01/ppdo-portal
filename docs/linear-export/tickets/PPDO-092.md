---
id: PPDO-92
title: 'Report page: AIP (Annex B) report type; remove the Consolidated AIP page'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-15
completed: 2026-09-17
parent: (none)
blocked_by: PPDO-90
related: PPDO-20, PPDO-116
url: https://linear.app/ralphoksiprojects/issue/PPDO-92
uuid: c74e838c-8702-4415-b1f0-11dfb5508ef1
---

# PPDO-92 — Report page: AIP (Annex B) report type; remove the Consolidated AIP page

**In one line:** the consolidated AIP grid, sector tabs and Export move onto `/budget-planning/report` as a report type; the standalone page and its sidebar item go away.

**Spec:** `docs/v1.8/AIP_Report_Spec.md` §2 decisions 1, 2, 7–10, §3, §6, §10 · **Size:** M · **Blocked by:** [PPDO-90](https://linear.app/ralphoksiprojects/issue/PPDO-90/aip-report-scope-open-the-consolidated-aip-endpoints-to-department)

## Scope

* Extract the grid, tabs, header, printed-figures note and activity-modal wiring from `aip/consolidated/page.tsx` into `components/aip/report/AipAnnexBReport.tsx`; render it from the Report page. No change to figures or layout.
* Report Type options filtered per user: WFP/PPMP host-office only (existing [PPDO-20](https://linear.app/ralphoksiprojects/issue/PPDO-20/budget-planning-dashboard-one-page-that-resolves-per-person) rule), AIP for either reviewer flag. Default = first allowed; a hand-typed disallowed `?type=` falls back.
* AIP selectors: FY (FY2028+) · Office — cross-office reviewer gets "— all submitted offices (consolidated) —" + each office; department head locked to own office · no Division select.
* Empty/not-submitted/not-opened/error states per spec §6; Export disabled with a reason when there is nothing to export.
* `lib/budget-planning-access.ts`: `canOpenAipReport`, `canOpenWfpReport`; Sidebar shows Report for anyone with an allowed type; **remove the Consolidated AIP item**.
* `/budget-planning/aip/consolidated` → redirect to `/budget-planning/report?type=AIP&fiscalYear=&sector=`; delete the old page; point the AIP Review search header link at the report.

## Acceptance (spec §10, abridged)

- [ ] No "Consolidated AIP" sidebar item for anyone; the old URL lands on the report with FY and sector kept
- [ ] Cross-office reviewer: consolidated AIP matches the old page, Export downloads the same workbook
- [ ] Guest-office department head sees Report, only the AIP type, office locked, own rows even in Draft, own-office export
- [ ] PPDO division encoder with no review flag sees WFP and PPMP but no AIP

## Manual-coding candidate?

**The redirect stub + removing the sidebar item — yes** (tiny, reversible, sibling redirects exist). The rest — no.
