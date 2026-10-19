---
id: PPDO-5
title: 'LFC permission — the first cross-office flag (bypasses OfficeScope)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-01
parent: PPDO-28
blocked_by: PPDO-3
related: PPDO-18, PPDO-74
url: https://linear.app/ralphoksiprojects/issue/PPDO-5
uuid: 5daa09fb-bfdc-4ad2-9fee-5920ce989111
---

# PPDO-5 — LFC permission — the first cross-office flag (bypasses OfficeScope)

**V18-05** — plan: `docs/v1.8/Phase_Plan.md` §3.1; source: `AIP_Requirements_Review.md` §5.4. Depends on V18-03 ([RAL-244](https://linear.app/ralphoksiprojects/issue/RAL-244/reviewer-permission-overridecanreviewbudgetplanning)).

The Local Finance Committee reviews **every office's** submitted work. It is the first permission in this codebase that is explicitly **cross-office**: every other flag narrows to the caller's own office.

**Scope:** a per-user flag (`OverrideCanReviewLfc`), resolved SuperAdmin → true, else `Override ?? false`, and — the important part — its resolution must **bypass** `OfficeScope` rather than combine with it. LFC users may hold an `OfficeId` (they are real people in real offices); that must not narrow what they can review.

⚠️ **Do not build this as "reviewer + all offices".** That would silently inherit the reviewer's write-denial semantics from V18-04, which is a different rule with a different intent. Two flags, resolved separately, even where a person holds both.

The LFC *workflow* (approve, return, consolidated view) is Phase 4 — this ticket is the flag and its scope behaviour only.

**Done when:** an LFC user sees every office's data on the read paths that consume `OfficeScope`, a non-LFC office user is unaffected, and tests cover an LFC user *with* an `OfficeId` set (the case most likely to be got wrong).
