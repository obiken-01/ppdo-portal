---
id: PPDO-28
title: 'Epic: v1.8.0 Phase 1 — Identity, Configuration & Landing'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-02
parent: (none)
blocked_by: (none)
related: PPDO-32, PPDO-48
url: https://linear.app/ralphoksiprojects/issue/PPDO-28
uuid: 7078cb35-aebb-435a-941a-c962ef493fd6
---

# PPDO-28 — Epic: v1.8.0 Phase 1 — Identity, Configuration & Landing

Parent for **Phase 1** of the v1.8.0 phase plan — the only phase that is unblocked by the open AIP decisions, and the one every later phase depends on (its permission model).

**Plan:** `docs/v1.8/Phase_Plan.md` **§3** (work items V18-01 … V18-31). Companion docs: `docs/v1.8/AIP_Requirements_Review.md` (the technical reasoning), `docs/v1.8/Office_User_Path_Findings.md` (office isolation + the reviewer model), `docs/v1.8/AIP_Redesign_Notes.md`.

## Four clusters

1. **Permissions (V18-01…08)** — rename `CanManageAllocation`; add the PBO ceiling flag; add the reviewer flag plus a `DenyReviewerWrite` guard (the codebase's first *subtractive* permission); add LFC as the first cross-office flag; permission matrix + audit coverage; User Management form restructure.
2. **Configuration (V18-09…15)** — CC-typology and eSRE config tables (free strings today), `ProgramDivision` string keys → FKs, office/division config extensions, config dashboard tiles, and the office-only scoping rule written into the resolver.
3. **Landing page (V18-16…21)** — per user / division / office, resolved server-side, one frontend helper replacing five hardcoded redirect sites.
4. **Password reset (V18-22…27)** — Option B (self-service via a user-set recovery answer), settled 2026-08-14, plus fixing the shared default password.

Then **V18-28…31** — the three dashboards and the user profile as landing targets.

## Branch / PR convention

Feature branches off `release/1.8.0`; PRs target `release/1.8.0` (**not** `main`). Per-ticket implementation prompts follow `docs/TICKET_PROMPT_STANDARD.md` and are written when a ticket moves to In Progress — the descriptions here are scope, not prompts.

## Open, does not block starting

§10 of the plan recommends splitting the milestone (v1.8.0 = Phase 1, v1.9.0 = the AIP redesign, v1.10.0 = offline). If that is accepted these tickets move to the new milestone; nothing about their content changes.

⚠️ Shipping Phase 1 does **not** make production office-user accounts safe. That still waits for AIP ownership in Phase 2 — an office account today has destructive access to PPDO's AIP via `DELETE /aip/{id}`.
