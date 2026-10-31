---
type: hub
version: v1.9.0
status: current
tickets: []
updated: 2026-10-19
---

# v1.9 — Reviewer edits, comments, notifications and WFP export

Up: [Docs home](../HOME.md)

Previous: [v1.8](../v1.8/README.md) · Next: none · All releases: [Releases canvas](../Releases.canvas)

PPDO reviewer edits, comment threads on AIP activities and Investment Proposals, a notification bell, other-fund-source ceiling check, the initial WFP report and its bulk export. Frozen for UAT; see the v1.9.0 pre-deployment checklist.

Each document's `status` property says whether it is `current`, `shipped`, `superseded` or `reference` (see [HOME](../HOME.md#status-values)).

## Specs

- [v1.9.0 — AIP activity comments as threads](AIP_Comment_Threads_Spec.md) — AIP activity comments as threads
- [v1.9.0 — PPDO Reviewer Edits to a Submitted AIP](AIP_Reviewer_Edit_Spec.md) — PPDO reviewer edits to a submitted AIP
- [v1.9.0 — Bulk export of initial WFPs (background job)](Bulk_Initial_WFP_Export_Spec.md) — Background job that exports every initial WFP as a ZIP
- [v1.9.0 — AIP Ceiling Check on Other Fund Sources](Fund_Source_Ceiling_Spec.md) — AIP ceiling check on other fund sources
- [v1.9.0 — Initial WFP report from the consolidated AIP](Initial_WFP_Report_Spec.md) — Initial WFP report from the consolidated AIP
- [v1.9.0 — Comments on Investment Proposals](Investment_Proposal_Comments_Spec.md) — Comments on Investment Proposals
- [v1.9.0 — Notification bell](Notification_Centre_Spec.md) — Notification bell

## Checklists

- [v1.9.0 — Pre-Deployment Checklist](Pre_Deployment_Checklist_v1.9.0.md) — v1.9.0 pre-deployment checklist
- [v1.9.1 — Pre-Deployment Checklist](Pre_Deployment_Checklist_v1.9.1.md) — v1.9.1 pre-deployment checklist (.NET 10 runtime switch)

## Plans

- [v1.9.2 — Initial PPMP report from the consolidated AIP](Initial_PPMP_Report_Spec.md) — PPDO-263, accepted: the PPMP for FY 2028+ read from the AIP's procurement items
- [v1.9.2 — UAT Polish Plan](UAT_Polish_Plan.md) — role × page sweep, UI/UX consistency, performance re-measure and the Playwright foundation (PPDO-252)

## Audits

- [v1.9.2 — Performance audit 2026-11](Performance_Audit_2026-11.md) — re-measure after PPDO-182…189; bundle baseline done, UAT measurements pending (PPDO-257)
- [v1.9.2 — UAT pre-sweep findings](UAT_PreSweep_Findings.md) — suspects from reading the code, to confirm in the A1–A3 sweep (PPDO-252)
- [v1.9.1 — Loading guard audit](Loading_Guard_Audit.md) — which actions on the heavy pages could run before their data loaded, and the fixes (PPDO-244)
