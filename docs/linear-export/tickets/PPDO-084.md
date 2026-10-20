---
id: PPDO-84
title: 'V18-60 — AIP Annex B Excel export from the Consolidated AIP page'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-14
completed: 2026-09-14
parent: PPDO-83
blocked_by: (none)
related: PPDO-73, PPDO-85
url: https://linear.app/ralphoksiprojects/issue/PPDO-84
uuid: dc1f55e9-96e1-4f7b-b405-ed00284ae049
---

# PPDO-84 — V18-60 — AIP Annex B Excel export from the Consolidated AIP page

**In one line:** a cross-office reviewer downloads the FY2028+ AIP as the province's Annex B workbook — four sector sheets, columns A–R — and it matches the Consolidated AIP grid to the peso.

**Spec:** `docs/v1.8/AIP_Form_Spec.md` §1–§8 (the form) and **§9–§19 (the build)** · **Epic:** [PPDO-83](https://linear.app/ralphoksiprojects/issue/PPDO-83/epic-v180-phase-5-outputs) · **Size:** M · Settled with Ralph 2026-09-14

## Decisions

* **Same offices as the grid** — `SubmittedToPpdo` + `Consolidated`; a preamble line says "Includes N of M offices" while any are missing.
* **Cross-office reviewers only** (`CanReviewAllOffices`, SuperAdmin), from a **Download Excel** button on the Consolidated AIP page. No per-office download.
* **One workbook, four sheets, always** — an empty sector still gets its sheet with TOTAL `-`.
* **Real** `SUM` **formulas** for row Total, office subtotals and sheet TOTAL; activity cells hold values. Tests pin every formula's result to the builder's figure.
* **Printed figures only**, from [PPDO-73](https://linear.app/ralphoksiprojects/issue/PPDO-73/v18-55-the-consolidated-view-partial-by-design)'s `AipFormRowBuilder` / `AipPrintedFigures` — never re-derived. Amounts in thousands, `.00`.
* **Over-ceiling note** under TOTAL with draft wording in one constant (PPDC to agree). Rounding stays uplift-then-round (tracker G5, provisional).
* FY2028+ only · `AIP_FY<year>_<date>.xlsx` · "As of" = download month, Manila.

## API

`GET /api/budget-planning/aip/consolidated/export?fiscalYear=` → xlsx · 400 missing / FY≤2027 · 401 · 403 · 404 unopened year. One tree load for all four sheets.

## Build

Service method on `AipConsolidatedService` (reuses the builder) · ClosedXML writer in `PPDO.Infrastructure`, styled like `PpmpReportExcelService`, laid out from the province's FY2027 file (Arial Narrow 10, landscape, fit to one page wide, print titles 8:9) · endpoint · button. No migration.

## Acceptance

See spec §18.
