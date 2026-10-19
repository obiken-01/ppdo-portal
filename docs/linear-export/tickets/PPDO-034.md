---
id: PPDO-34
title: 'V18-35 — AIP units: thousands → pesos, every fiscal year'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-09-02
completed: 2026-09-03
parent: PPDO-32
blocked_by: (none)
related: PPDO-64
url: https://linear.app/ralphoksiprojects/issue/PPDO-34
uuid: 5afa2236-100e-4589-93f1-345c916f1b64
---

# PPDO-34 — V18-35 — AIP units: thousands → pesos, every fiscal year

**In one line:** AIP stores money in thousands while everything it touches stores pesos — migrate the data, delete all six `×1000` conversions, and fix the 19 places in the UI that still assume thousands.

**Spec: **`docs/v1.8/AIP_Foundation_Spec.md` §5.3 · **Size:** L · **Blocks:** V18-36 · ⚠️ **MIGRATION (data-only)** · ⚠️ **Highest-risk ticket in the phase**

## Why migrate rather than partition

Under a partition, `AipActivity.Total` stops being readable without knowing the fiscal year — permanently — and six conversion sites become six FY-conditional branches that must stay correct forever. Getting one wrong in the *permissive* direction is **silent**: the ceiling simply never trips again, and the first symptom is a WFP over its AIP activity, found by somebody adding up a printed report by hand.

Migrating deletes the failure mode. Partitioning schedules it. (DECISION E, 2026-08-25.)

## Scope

**1. The data.**

```sql
UPDATE aip_activities SET
  ps = ps*1000, mooe = mooe*1000, co = co*1000, total = total*1000,
  cc_adaptation = cc_adaptation*1000, cc_mitigation = cc_mitigation*1000
```

Every fiscal year. **Shape partitions at FY2028; units do not.**

**2. Delete all six** `×1000` **sites** — do not make them FY-conditional. Verified 2026-09-02:

| Layer | Site |
| -- | -- |
| Backend | `WfpCeilingService.cs:60`, `:106`, `:220` |
| Frontend | `wfp/page.tsx:274`, `:377`, `:1409` |

`WfpCeilingService`'s class doc claims the conversion happens *only* there. True of backend services, **false of the codebase** — fix or delete that comment.

**3. The display and entry half — mandatory, and bigger than Phase_Plan §4 says.**

The plan named 2 sites. There are **19**, all in `aip/detail/page.tsx` (2,057 lines, the largest file in the repo):

| What | Where | Count |
| -- | -- | -- |
| Thousands headers | `:2015` `Amount (in ₱000)`, `:2016` `CC Expenditure (₱000)` | 2 |
| Raw display cells | `:369–374` — ps, mooe, co, total, ccAdaptation, ccMitigation | 6 |
| **Money inputs** | `MoneyInput` in the inline add/edit rows | **11** |

⚠️ **Entry is in thousands too.** A value typed as `250` today means ₱250,000. If only display is fixed, every edit silently divides the record by a thousand.

⚠️ Left untouched, `250` renders as `250000` under a header still promising ₱000 — **₱250 million, 1000× high**, on a page people read numbers off. That is the exact failure mode this migration exists to remove, relocated from a ceiling check to a report.

**4. **`types/budget-planning.ts:908` documents LDIP's budget as "in thousands (₱000), **like AIP totals**". That cross-reference goes false — correct it in the same commit.

## ⚠️ Blocked on P2-a

Does the detail page **drop** the `(in ₱000)` headers and show full pesos, or **divide by 1000 at render** and multiply on save, keeping the province's convention? **This needs the finance officers, not a dev decision.** Spec default: divide at render.

## Watch out

* **Verification is arithmetic — do it. **`SUM(total)` per fiscal year before and after; the ratio must be exactly 1000 for every year. Reversible by dividing.
* **Back the database up first.** The only Phase 2 migration that rewrites existing values.
* **Do not combine this with extracting** `aip/detail/page.tsx`**.** RETROSPECTIVE says extract before redesigning, and that is right — but a unit migration plus an extraction in one diff hides which change produced a wrong number. Migration first, extraction second.

## Out of scope

Moving LDIP to pesos (it crosses no boundary — see spec §2 decision 6). Phase 5's printable form, which inherits pesos and must be built peso-aware from the start.

## Done when

* `SUM(total)` per year after = before × 1000, exactly, every year
* Zero `* 1000` remain (grep)
* The 2 headers, 6 cells and 11 inputs agree with each other and with P2-a
* Typing `250` and reloading shows what it showed before Phase 2
