---
id: PPDO-8
title: 'Audit-log coverage for permission, role, office and division changes'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: (none)
related: PPDO-19, PPDO-77, RAL-200
url: https://linear.app/ralphoksiprojects/issue/PPDO-8
uuid: 25f4dede-71d5-4528-9af7-40a5cc456345
---

# PPDO-8 — Audit-log coverage for permission, role, office and division changes

**V18-07** — plan: `docs/v1.8/Phase_Plan.md` §3.1.

Verify that every write that changes *who can do what* lands in the audit log (`AuditLog`, surfaced on `config/audit-log`), and add whatever is missing:

* `users` — role change, office change, division change, each `Override*` flag, activate/deactivate, password reset.
* `divisions` — the `Can*` flag set, office reassignment, deactivate.
* offices — deactivate (an inactive office silently narrows every scoped query).

[RAL-200](https://linear.app/ralphoksiprojects/issue/RAL-200/api-audit-trail-logging-for-inventory-prs-deliveries-distributions) did this pass for Inventory; follow that shape. Log the **before and after** value, not just "changed", and never log the password hash or the recovery answer hash.

**Why it is in Phase 1 rather than later:** V18-23's self-service password reset is only acceptable *because* every reset is detectable afterwards (`AIP_Requirements_Review.md` §2.6 sub-decision k). This is that detective control, and V18-05's cross-office LFC flag is the kind of grant that should never be un-auditable.

**Done when:** each change listed above produces an audit row with old → new, and a test covers the user-permission path.
