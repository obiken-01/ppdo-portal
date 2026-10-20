---
id: PPDO-153
title: 'Epic: Demo 2.15 — PGOM Investment Proposal (generate the Word proposal from the AIP)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-29
completed: 2026-10-04
parent: PPDO-122
blocked_by: (none)
related: PPDO-130, PPDO-132, PPDO-136
url: https://linear.app/ralphoksiprojects/issue/PPDO-153
uuid: a66e0c65-796e-4bfe-b53f-1d40c1477dfc
---

# PPDO-153 — Epic: Demo 2.15 — PGOM Investment Proposal (generate the Word proposal from the AIP)

Every FY2028+ AIP project needs a **PGOM Investment Proposal** — the province's 5-page GAD-oriented project proposal (summary, rationale, logframe, work plan, cost annex H-1, team, M&E, risks, CC typology). Offices write it by hand in Word today. The figures in it already live in the v1.8.0 AIP, and retyping them produces errors: all three FY2027 samples reviewed contain arithmetic mistakes, and one prints a project cost that disagrees with its own annex.

This feature lets an office open an AIP project, write only the narrative, and export a `.docx` whose numbers come from the AIP.

## Docs

* **Spec (authoritative):** `docs/v1.8/Investment_Proposal_Spec.md` — accepted 2026-09-29.
* **Findings:** `docs/v1.8/Investment_Proposal_Findings.md` — template study, the three sample proposals, every question answered 2026-09-27.
* **Ticket prompts:** `docs/v1.8/Investment_Proposal_Ticket_Prompts.md` (also pasted into each child ticket).

## Headline decisions

* One proposal per **AIP Project**, **FY2028+ only**. Title "PGOM INVESTMENT PROPOSAL".
* Draft → Final. Final snapshots the AIP values. Reopen: department head (own office) or host-office Admin/SuperAdmin.
* Annex H-1 is generated from `AipExpenditure` + `AipProcurementItem`, as `Lunch 394.00 x 30 pax x 3 days = 35,460.00` lines under each account, PS | MOOE | CO. Its total equals Section A's Project Cost by construction.
* Work plan rows are AIP activities, with proposal-only component groups and proposal-only zero-cost steps.
* Amounts are the exact encoded pesos (not the Annex B rounded +30% figures).

## Branching

Demo 2 convention for spec'd items (as [PPDO-130](https://linear.app/ralphoksiprojects/issue/PPDO-130/demo-28-division-head-submits-to-department-head-who-can-return-that) / [PPDO-136](https://linear.app/ralphoksiprojects/issue/PPDO-136/demo-214-admin-configurable-default-fiscal-year-for-investment)): feature branches off `release/1.8.0`, PRs into `release/1.8.0`. **One migration** (`AddInvestmentProposals`), added to `docs/v1.8/Pre_Deployment_Checklist.md` by T1.

## Children

T1 data model → T2 service/API → T3 HGDG calculator · T4 document builder → T5 Word export · T6 list page · T7 editor page · T8 signatory-defaults config card. See the spec §9 for blocked-by.

## Related

[PPDO-132](https://linear.app/ralphoksiprojects/issue/PPDO-132/demo-210-attach-a-project-proposal-pdfword-to-each-project) (Demo 2.10, attach a proposal file per project). Generating the proposal covers the Word document that request was about. Whether [PPDO-132](https://linear.app/ralphoksiprojects/issue/PPDO-132/demo-210-attach-a-project-proposal-pdfword-to-each-project)'s upload is still needed (e.g. for scanned signed copies) is decided there, not here.
