---
type: hub
version: v1.8.0
status: shipped
tickets: []
updated: 2026-10-19
---

# v1.8 — AIP redesign

Up: [Docs home](../HOME.md)

Previous: [v1.7](../v1.7/README.md) · Next: [v1.9](../v1.9/README.md) · All releases: [Releases canvas](../Releases.canvas)

The largest change so far: AIP redesigned for FY2028 onward (clean break, no migration), AIP Entry and Review, consolidated view and Annex B export, Investment Proposal, external AIP API, divisions and office setup. v1.8.0 plus patches v1.8.1 to v1.8.4 (optimisation, dashboard redesign, caching, concurrent-edit guard).

Each document's `status` property says whether it is `current`, `shipped`, `superseded` or `reference` (see [HOME](../HOME.md#status-values)).

## Specs

- [AIP Concurrent Edit — Requirements](AIP_Concurrent_Edit_Spec.md) — Optimistic concurrency and soft warning for AIP edits (V18-71)
- [v1.8.0 — AIP Entry: drill-down layout + delete Project/Activity](AIP_Entry_Layout_Spec.md) — AIP Entry drill-down layout and delete Project/Activity
- [v1.8.0 Phase 3 — AIP Entry](AIP_Entry_Spec.md) — Phase 3: AIP Entry
- [AIP Entry — Draft Mirror for Expenditure Lines](AIP_Expenditure_Draft_Spec.md) — IndexedDB draft mirror for expenditure lines
- [AIP Printable Form — Specification (V18-60)](AIP_Form_Spec.md) — The printable AIP form (DBM Annex B) the system must produce
- [v1.8.0 Phase 2 — AIP Foundation](AIP_Foundation_Spec.md) — Phase 2: AIP foundation
- [v1.8.0 Phase 6 (partial) — AIP Offline Caching Groundwork](AIP_Offline_Caching_Spec.md) — Offline caching groundwork (V18-64 to V18-67)
- [v1.8.0 — Consolidated AIP moves into the Report page](AIP_Report_Spec.md) — Consolidated AIP moves into the Report page
- [v1.8.0 — AIP Review: the same drill-down as AIP Entry, with a Full office view](AIP_Review_Layout_Spec.md) — AIP Review drill-down and Full office view
- [v1.8.0 Phase 4 — AIP Review & Consolidation](AIP_Review_Spec.md) — Phase 4: AIP review and consolidation
- [PPDO-20 (V18-30) — Budget Planning Dashboard](Budget_Planning_Dashboard_Requirements.md) — Budget Planning dashboard (PPDO-20)
- [v1.8.0 — Admin-configurable default fiscal year (PPDO-136)](Default_Fiscal_Year_Spec.md) — Admin-configurable default fiscal year (PPDO-136)
- [v1.8.0 — Division heads submit to the department head (PPDO-130)](Division_Submit_Spec.md) — Division heads submit to the department head (PPDO-130)
- [External AIP API + partner API keys — build spec](External_AIP_API_Spec.md) — External AIP API and partner API keys
- [v1.8.0 — PGOM Investment Proposal (Demo 2.15)](Investment_Proposal_Spec.md) — PGOM Investment Proposal (Demo 2.15)
- [v1.8.0 — Office setup: finance sets ceilings, department heads set up their own office](Office_Setup_Spec.md) — Finance sets office ceilings; department heads set up their own office

## Standards

- [Permission Matrix](Permission_Matrix.md) — Who can do what, pinned by tests

## Plans and ticket prompts

- [Demo 2.15 — Investment Proposal: ticket prompts](Investment_Proposal_Ticket_Prompts.md) — Ticket prompts for the Investment Proposal
- [v1.8.0 — Phase Plan & Work Items](Phase_Plan.md) — v1.8.0 phase plan and work items

## Findings

- [AIP Redesign — Working Notes](AIP_Redesign_Notes.md) — Ralph's initial AIP redesign description and working notes
- [Investment Planning dashboard — design pass](Dashboard_Design_Pass.md) — Design pass on the Investment Planning dashboard
- [PGOM Investment Proposal — Template Findings](Investment_Proposal_Findings.md) — PGOM Investment Proposal template findings
- [Office-User Path — Codebase Impact Assessment](Office_User_Path_Findings.md) — Impact assessment of letting non-PPDO office users log in

## Audits

- [Performance audit — 2026-10-04](Performance_Audit_2026-10-04.md) — Whole-codebase performance audit of 2026-10-04

## Retrospectives

- [PPDO Portal — Project Retrospective (Whole Project to Date)](RETROSPECTIVE.md) — Whole-project retrospective to v1.7.4

## Guides

- [AIP amounts — what each screen, API and file shows](AIP_Amount_States.md) — What each screen, API and file shows for an AIP amount

## Checklists

- [v1.8.0 — Pre-Deployment to Production Checklist](Pre_Deployment_Checklist.md) — v1.8.0 pre-deployment checklist
- [v1.8.2 — Pre-Deployment Checklist](Pre_Deployment_Checklist_v1.8.2.md) — v1.8.2 pre-deployment checklist

## Runbooks

- [PPDO-34 / V18-35 — AIP Units Migration Runbook](Units_Migration_Runbook.md) — Runbook for the AIP amounts-to-pesos migration

## Wireframes

- [AIP Review — wireframes](wireframes/aip-review/README.md) — AIP Review wireframes
- [Consolidated AIP — wireframes (PPDO-73)](wireframes/consolidated-view/README.md) — Consolidated AIP wireframes
- [Investment Planning dashboard — wireframes](wireframes/dashboard/README.md) — Investment Planning dashboard wireframes (proposals)
- [Investment Proposal — wireframes](wireframes/investment-proposal/README.md) — Investment Proposal wireframes
- [Submission history — wireframes (PPDO-77)](wireframes/submission-history/README.md) — Submission history wireframes

## Other files in this folder

- [v1.8.0_Open_Items_Tracker.xlsx](v1.8.0_Open_Items_Tracker.xlsx) — the open-questions tracker the Phase Plan refers to (spreadsheet, not a markdown doc)
