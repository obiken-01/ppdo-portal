---
id: PPDO-7
title: 'Permission matrix doc + PermissionService test sweep'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: (none)
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-7
uuid: 0fc7fa4c-1a86-4322-b59b-3dda466e0bab
---

# PPDO-7 — Permission matrix doc + PermissionService test sweep

**V18-06** — plan: `docs/v1.8/Phase_Plan.md` §3.1.

After V18-01…05 the permission model carries ~11 flags across three mechanisms — role bypass, division defaults, per-user overrides — plus two scope dimensions (`OfficeScope`, `DivisionScope`) whose null rules are **opposites** (null `OfficeId` = PPDO = full access; null `DivisionId` = empty scope). That is past the point where "read `PermissionService`" is a reasonable answer to "who can do what".

Two deliverables:

1. **One matrix document** (suggest `docs/v1.8/Permission_Matrix.md`, linked from `CLAUDE.md`): every flag × role × division default × override × office/division scope, with the resolution rule stated per flag and the two subtractive/cross-office exceptions (V18-04, V18-05) called out explicitly.
2. **A** `PermissionService` **test sweep** that pins each row of the matrix, so the doc cannot silently go stale.

**Done when:** every flag in the codebase appears in the matrix, and every matrix row has a test.
