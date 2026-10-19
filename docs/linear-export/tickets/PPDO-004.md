---
id: PPDO-4
title: 'Scope rule: division is not a scoping axis for non-PPDO office users'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: (none)
related: PPDO-17, PPDO-38, PPDO-57, PPDO-66, RAL-228
url: https://linear.app/ralphoksiprojects/issue/PPDO-4
uuid: 706874cc-15d3-4160-8d2a-886facf3d8ae
---

# PPDO-4 — Scope rule: division is not a scoping axis for non-PPDO office users

**V18-15** — plan: `docs/v1.8/Phase_Plan.md` §3.2; source: `AIP_Redesign_Notes.md` §2.1.

Ralph's requirement: *"For PPDO, the AIP creation will be separated by division, same with WFP. But other offices will have only access to their office's work regardless of their division."*

So the model is genuinely two-axis, and it is what **neither** existing feature does — WFP scopes by `(OfficeId, DivisionId)` with division always participating; LDIP scopes by `OfficeId` alone with division never participating. The new rule: **division participates only when the caller is PPDO.**

`Division` is office-scoped, so a non-PPDO user can legitimately have a `DivisionId` — it just must not narrow what they see.

Scope: express this once, in the scope resolver next to `OfficeScope`/`DivisionScope` (`PPDO.Application/Common`), with tests, and document it in the permission matrix (V18-06). No feature consumes it yet — that is deliberate: Phase 3 reads it, and it is far cheaper to get right before three call sites each invent their own version.

⚠️ Remember `OfficeScope`'s inversion ([RAL-228](https://linear.app/ralphoksiprojects/issue/RAL-228/officescope-primitive-office-clamp-helper-for-budget-planning)): a null `OfficeId` means PPDO-internal = full access, the **opposite** of `DivisionScope`'s null rule.

**Done when:** the rule is one tested function, and the two null rules are documented side by side so nobody copies the wrong one.
