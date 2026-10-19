---
id: PPDO-20
title: 'Budget Planning dashboard — one page that resolves per person'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-02
parent: PPDO-28
blocked_by: (none)
related: PPDO-17, PPDO-31, PPDO-92, PPDO-198, RAL-166, RAL-230
url: https://linear.app/ralphoksiprojects/issue/PPDO-20
uuid: 5e165350-25f3-49a5-a167-b822134df27c
---

# PPDO-20 — Budget Planning dashboard — one page that resolves per person

**V18-30** · plan: `docs/v1.8/Phase_Plan.md` §3.5

## 📖 The spec is authoritative — read it first

`docs/v1.8/Budget_Planning_Dashboard_Requirements.md` (merged in PR #280, written against `docs/SPEC_STANDARD.md`).

It carries the settled decisions with their reasoning, the given/when/then behaviour table including every permission case, the API contract, UI states, non-goals, the ticket split, and the acceptance checklist. **This description is scope only — the spec is what you implement against.**

Also read: `docs/v1.8/Permission_Matrix.md` (every gate here is a row in it), `docs/DESIGN_SYSTEM.md`, `docs/PERFORMANCE_GUIDELINES.md`.

## Scope

Replace the two hardcoded shapes — a 2×2 readiness hub for everyone, plus two PPDO-only sections bolted below it — with **one page, six bands, gated by flags that already exist**. Eight account types can reach it: PPDO encoder / division reviewer / cross-office reviewer / finance officer, guest-office encoder / office reviewer / PBO officer, and system admin.

This also makes the page a viable landing target, which it is not while office users land on a hub reporting nothing they can act on.

## Decisions settled 2026-09-02 (full reasoning in the spec §2)

* **WFP is off the dashboard entirely — PPDO included.** It is about to become an update to what AIP creation produced, so reporting on its current shape teaches a model that goes wrong within a release.
* **Money comes from the AIP** — *costed*, not *planned in WFP*.
* **Submission means the AIP**, for every account. v1.8.0's subject is the AIP.
* **Guest offices get a 3-stage rail** (ceiling → AIP → AIP submission); PPDO gets 5.
* **WFP and Report are PPDO-internal** — shipped, see below.
* **Fiscal year, office and division are all context fields**; guest offices get no division field, because division does not narrow them.
* **One status vocabulary** — Todo / In progress / Review / Done. `Over ceiling`, `Behind`, `Cannot submit` stay outside the four as risk pills.
* **Stacked bars, not donuts**, for ceiling-by-fund. Removes Chart.js from this page.
* **Ceiling row actions deep-link** to `/budget-planning/allocation?officeId=N` rather than opening a modal, reusing [PPDO-17](https://linear.app/ralphoksiprojects/issue/PPDO-17/allocation-page-office-picker-for-cross-office-ceiling-holders-pull)'s office picker. Only *bulk set* gets a modal.

**No schema change.** Every addition is a read.

## Shipped

* **PR #279** — WFP + Report hidden from guest offices (sidebar link, route gate, prefetch). Plus a follow-on found while testing: three frontend places keyed on `CanManagePpdoAllocation` alone and now also require the host office, matching the endpoints. Live-verified across PTO, PBO, PPDO Staff and SuperAdmin.
* **PR #280** — the spec, plus stale division-code corrections in three older docs.

## Remaining — spec §9

|  | Scope | Blocked by |
| -- | -- | -- |
| ~~A~~ | ~~Hide WFP + Report from guest offices~~ | ✅ PR #279 |
| **B** | `GET /dashboard/offices` + `OfficeSummaryDto` + scope resolution + tests | — |
| **C** | Replace `DivisionWfpStatusDto` with `DivisionSummaryDto` (AIP-based), rename `WfpByDivision` → `ByDivision`, update the frontend type and current page. **Breaking response change** | — |
| **D** | Shared components: `PipelineRail`, `StatusPill`, `StackedFundBar`, `ActionCard` | — |
| **E** | Dashboard rebuild — context bar, action band, rail, tiles | D |
| **F** | Table lane — division table (PPDO), office table (cross-office / PBO / admin) | B, C, D |
| **G** | Ceiling row deep-links + bulk-set modal | F |

B, C and D are independent and parallelisable.

## ⚠️ Two traps the spec calls out

1. **Scope resolution on the new endpoint** must go through `OfficeScope.ResolveForReview` or `ResolveForCeiling` — **never **`OfficeScope.Resolve`. Teaching `Resolve` either grant silently promotes a cross-office *reader* into a cross-office *editor*, with no diff at any write site to notice it (`Permission_Matrix.md` §4).
2. `DivisionWfpStatusDto` **is not merely missing a field** — three of its members are WFP concepts, so it is replaced rather than extended. The **per-division clamp for division-scoped Staff rides on that payload** and must be carried over, not dropped with the old DTO.

## Manual-implementation candidate

**D is a good one** — small blast radius, presentational, and `RowActions` is a near-identical sibling to pattern-match against. **B is explicitly not** — its scope resolution is exactly the class of thing where a wrong choice compiles cleanly and leaks data.

## Open, non-blocking

* Is **"unspent" / "remaining"** the right word for allocation minus costed? May be *unprogrammed* or *unobligated* in PBO's vocabulary — one question to the finance officers before it is stamped across four bands and an export.
* Does a **cross-office reviewer** land on the roll-up or their last-opened office? Drawn as the roll-up.
* **Cut-off date** — referenced in copy, no such field exists. Non-goal here; Phase 4 owns it.

---

### Superseded framing (kept for the reasoning, not as instruction)

[RAL-230](https://linear.app/ralphoksiprojects/issue/RAL-230/stop-office-users-fetching-the-ppdo-scoped-budget-planning-dashboard) stopped office users fetching the PPDO-scoped dashboard and deliberately left them the readiness hub, on the reasoning that a real office dashboard "belongs to the redesign". The open question — keep the hub, or build the real thing — was **closed 2026-08-25 (tracker D6): build it**.

The risk that decision carried still shapes the scope: much of what an office dashboard would naturally show (submission state, review queue, cut-off) does not exist until Phase 4. The spec's answer is to render those from constants so the layout does not move when Phase 4 fills them in — build what is real, leave room, do not scaffold placeholders for data with no source.

**Still live from** [RAL-230](https://linear.app/ralphoksiprojects/issue/RAL-230/stop-office-users-fetching-the-ppdo-scoped-budget-planning-dashboard)**:** the fiscal-year deadlock. `fiscalYear` used to be seeded from the PPDO dashboard response while the office-readiness effect was gated on `fiscalYear != null`; the standalone `/budget-planning/fiscal-years` endpoint is what breaks that circle. **Any new office view must source the year the same way.**
