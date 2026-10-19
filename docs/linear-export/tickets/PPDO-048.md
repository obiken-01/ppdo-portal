---
id: PPDO-48
title: 'Epic: v1.8.0 Phase 3 — AIP Entry'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-08
parent: (none)
blocked_by: (none)
related: PPDO-1, PPDO-28, PPDO-32, PPDO-67
url: https://linear.app/ralphoksiprojects/issue/PPDO-48
uuid: 076dcd5d-e5f5-41a0-ba38-3b14b80794b1
---

# PPDO-48 — Epic: v1.8.0 Phase 3 — AIP Entry

**In one line:** the screen an office actually uses to build its AIP — three-stage entry, generated ref codes, a reservation ledger, and a submit gate that enforces the ceiling.

Plan: `docs/v1.8/Phase_Plan.md` §5 · Follows: [PPDO-32](https://linear.app/ralphoksiprojects/issue/PPDO-32/epic-v180-phase-2-aip-foundation) (Phase 2 — AIP Foundation), complete and merged to `release/1.8.0`

## ↩️ Record model reversed 2026-09-05 — read this first

**One base AIP record per fiscal year, holding every office.** An Admin opens the year (which populates each office's programs from its own LDIP), then offices populate their own subtree once PBO has set their ceiling.

This reverses V18-40's office-owned shape (one record per office per year), which was an engineering inference from the §12.6 workflow rather than the province's model. `AipOffice` already carries the ownership FK, so per-office status, locking and submission work on the office row — nothing in the workflow needed a per-office *record*.

⚠️ **Cheap because nothing deployed.** `AddAipRecordOwningOffice` never ran against production, so it is dropped rather than reversed, and V18-42 had not started.

**Three tickets land ahead of the entry UI:** [PPDO-61](https://linear.app/ralphoksiprojects/issue/PPDO-61/reverse-the-office-owned-aip-record-shape-one-base-record-per-fiscal) (reverse the shape) → [PPDO-62](https://linear.app/ralphoksiprojects/issue/PPDO-62/open-a-fiscal-year-admin-creates-the-base-aip-record-and-populates) (open a fiscal year) + [PPDO-63](https://linear.app/ralphoksiprojects/issue/PPDO-63/remove-carry-forward-office) (remove carry-forward). [PPDO-51](https://linear.app/ralphoksiprojects/issue/PPDO-51/v18-41-aip-programs-sourced-from-a-valid-ldip-closed-list) is superseded in part — its closed-list rule stands, its office-owned seeding does not. Spec PR #299.

## ✅ Spec written 2026-09-03 — `docs/v1.8/AIP_Entry_Spec.md`

Authoritative for this phase (PR #295). Read it fully before starting any child; `Phase_Plan.md` §5 is the work-item list it expands, not a substitute.

**Two open questions live in the spec's §2, neither blocking the phase, each blocking one ticket:**

* **P3-a** (blocks V18-80 / [PPDO-54](https://linear.app/ralphoksiprojects/issue/PPDO-54/v18-80-aip-expenditure-procurement-lines-from-the-price-index)) — how far AIP procurement lines go: full WFP treatment or item selection plus a cost. Tracker W13-b.
* **P3-b** (blocks V18-42 / [PPDO-52](https://linear.app/ralphoksiprojects/issue/PPDO-52/v18-42-three-stage-aip-entry-ui-sub-office-group-projectactivity)) — ⚠️ `Phase_Plan.md` **§12.6 says convert** `ProgramDivision` **to real FKs *before* Phase 3 builds on it, and only half of that shipped.** [PPDO-1](https://linear.app/ralphoksiprojects/issue/PPDO-1/programdivision-replace-ref-code-string-keys-with-real-fks) did the office side; the **program** side is still ref-code keyed, and that is the half carrying PPDO's division visibility. The reason it stayed a string lapses for FY2028+, which has no re-upload. Spec §5.4.

## Why this phase exists

Phase 2 gave the AIP an owner, a place to record what money is *for*, and one unit. **It shipped no user-facing feature, deliberately.** Phase 3 is where FY2028 becomes enterable — which matters because V18-38 froze the `.xlsm` importer at FY2027, so **until this phase lands there is no way to create an FY2028 AIP at all**. That is the honest state of a clean break part-built, and it is the thing this phase closes.

## Children — 11 tickets

Suggested order: `V18-81 + V18-44 + V18-41` in parallel → `V18-42` → `V18-43`, `V18-80` · `V18-45` → `V18-46` → `V18-47` · then `V18-49` (needs both V18-42 and V18-46) · `V18-48` anytime.

| # | Ticket | Size |
| -- | -- | -- |
| V18-81 | Block FY2028+ WFP creation as "not supported yet" | S |
| V18-44 | Server-side, concurrency-safe ref-code generation | M |
| V18-41 | Programs sourced from a valid LDIP (closed list) | S |
| V18-42 | Three-stage entry UI — sub-office group, project/activity, expenditures | L |
| V18-43 | Multi-fund toggle, default single | S |
| V18-80 | Expenditure procurement lines from the Price Index | M |
| V18-45 | AIP reservation ledger (`AipDivisionAllocationLedger`) | M |
| V18-46 | AIP ceiling service — validate at submit | M |
| V18-47 | Office-level ceiling checks (offices without divisions) | S |
| V18-48 | PBO ceiling management UI — picker half already shipped | S |
| V18-49 | Completeness checklist + the submit gate | M |

## Five things to know before starting

1. V18-42 is the largest single item and it should be a lift, not a design. `LdipForm.tsx` ([RAL-61](https://linear.app/ralphoksiprojects/issue/RAL-61/ui-ldip-entry-form-createedit)) already implements the exact three-stage interaction — sub-office group name inside the add-a-program mini-form, existing names as suggestions, a new name starting a new group, continuous program numbering across groups sharing a ref code. Phase_Plan §12.6a says lift it. Redesigning it is how the two forms end up subtly different.

**2. ⚠️ The sub-office group is not the division.** Both attach at program level and they are orthogonal: the group is `(Sector, Name)` on `AipOffice`, **prints** on the form, and applies to every office; the division is `ProgramDivision`, **never prints**, and applies to the host office only. Conflating them produces a document that is wrong in a way no test will catch. Table in §12.6a.

**3. ⚠️ Do not clamp** `remaining` **at zero — anywhere.** After a ceiling cut an office's remaining allocation is legitimately **negative**, and that is precisely the state that must block submit. A `Math.Max(0, …)` in the ledger, the DTO or the UI hides the only signal the office has. Standing warning on V18-48, and it binds V18-45/46/49 too.

4. The ceiling check is narrower than it reads. `mooe + co`, **General Fund only** (DECISION H was withdrawn 2026-08-26 — this has flipped twice, check §12.3 before re-deriving it). PS is exempt as an expense class. It compares the **rounded** figures the document prints, and the **base** figures — the +30% uplift of DECISION G is presentation-only and is **not** in the comparison. ⚠️ Non-GF funds must be **explicitly excluded**, not merely left without ceiling rows: `GetDivisionAllocationAsync` resolves a missing allocation to `0m`, so a blank row means *zero*, not *unlimited*.

**5. ⚠️** `WfpCeilingService` **is not touched by this phase.** Zero diff. The plan once proposed retiring its allocation check for FY2028+; that was reversed 2026-08-26 — the check runs in four already-FY-parameterised methods, so "retire for FY2028+" would *add* four conditionals rather than delete code, and it is the only fund-scoped check in the system. V18-81 blocks FY2028 WFP creation instead, which makes the AIP/WFP double-count impossible rather than silently wrong, and lets V18-45 ship as a reservation ledger with the netting rule written down but unbuilt.

## Out of scope

Review, return and consolidation (Phase 4) · the printable AIP form (Phase 5) · offline (Phase 6) · the AIP↔WFP netting mechanism (deferred with V18-45, §12.1) · amendment after approval (V18-73, and note the terminal authority is the SP resolution, not the LFC).
