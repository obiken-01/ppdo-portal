---
id: PPDO-53
title: 'V18-43 — Multi-fund toggle on AIP expenditure entry, default single'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-03
completed: 2026-09-07
parent: PPDO-48
blocked_by: PPDO-52
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-53
uuid: 4b32ab90-4fb3-4556-83ce-4687a3625184
---

# PPDO-53 — V18-43 — Multi-fund toggle on AIP expenditure entry, default single

**In one line:** most activities draw on one fund, some draw on several — a toggle, defaulting to single, keeps the common case one field.

**Plan: **`docs/v1.8/Phase_Plan.md` §5 · **Size:** S · **Blocked by:** V18-42

## Settled

* **One fund source per expenditure line** (spec decision 4, settled). Multi-fund is expressed as *several lines*, not a fund list on one line.
* **Default single** (whiteboard W8). The toggle exists so the multi-fund case is possible, not so every encoder meets it.

## Why it matters beyond convenience

The FY≤2027 format carried **no per-fund breakdown at all** — the sample file shows combined fund sources in one cell. The new format having one fund per line is what makes fund-level checks possible at all, and it is what `docs/External_AIP_API_Contract.md` will be able to export to GSO.

⚠️ Note for that conversation: *"AIP carries no per-fund breakdown"* is a **FY≤2027** limit, not a permanent one. If it gets quoted at GSO as a present-tense fact, the fund-mix problem relocates somewhere with less information.

## Watch out

⚠️ **A blank ceiling row means zero, not unlimited. **`GetDivisionAllocationAsync` resolves a missing allocation to `0m`. That is V18-46's problem to enforce, but it is this ticket that decides which funds an encoder can *pick* — and offering a fund with no allocation row is offering one that will fail at submit with a confusing message.

## Done when

* Single-fund is the default and stays one field
* The toggle reveals per-line fund selection; each line carries exactly one fund
* An activity's lines may span several funds, and the activity's totals aggregate across them
* The fund list an encoder sees is coherent with what V18-46 will check at submit

## Out of scope

The ceiling check itself (V18-46) · procurement lines within an expenditure (V18-80) · the printed form's fund columns (Phase 5).
