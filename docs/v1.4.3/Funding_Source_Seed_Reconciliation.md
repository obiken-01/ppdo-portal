# Funding Source Seed Reconciliation (O2)

> Resolves open item **O2** from `v1.4.3_Requirements.md`. Reconciles the existing `funding_sources`
> config seed against (a) the seven ceiling-managed fund sources for v1.4.3 and (b) the fund-source
> labels actually used in the AIP workbook (see `Funding_Source_Aliases.md`).
> Deliverable: `docs/v1.4.3/seed/funding_sources_seed.csv` — the reconciled seed (with the RAL-157
> `aliases` column), imported via **Config → Funding Sources → CSV upload**.

---

## 1. Current seed (before)

Six funds, matching the WFP workbook VALIDATION sheet (J3:J8) per `docs/v1.4/WFP_New_Form_Findings.md`:

1. General Fund
2. Special Education Fund
3. Trust Fund
4. Calamity Fund
5. 20% Development Fund
6. Gender & Development Fund

## 2. Target (the 7 ceiling-managed funds for v1.4.3)

General Fund · 20% Development Fund · 5% GAD Fund · 5% LDRRMF · 1% PCPC Fund ·
1% Senior Citizen / PWD Fund · Special Education Fund.

## 3. Reconciled seed (after)

| Code | Name | Active | Change | Notes |
|---|---|---|---|---|
| `GF` | General Fund | ✅ | keep | — |
| `20%DF` | 20% Development Fund | ✅ | keep | — |
| `GAD` | 5% GAD Fund | ✅ | **rename** | Was "Gender & Development Fund"; that name is retained as an alias so old data/AIP labels still match. |
| `LDRRMF` | 5% Local Disaster Risk Reduction and Management Fund | ✅ | **new (absorbs Calamity)** | LDRRMF is the post-RA 10121 name for the former 5% Calamity Fund. `Calamity Fund` / `5% CF` / `CF` are carried as aliases. Heavily used in the AIP (≈306 rows). |
| `PCPC` | 1% Provincial Council for the Protection of Children Fund | ✅ | **new** | In the AIP only inside `/`-combinations today; seeded for completeness. |
| `SCPWD` | 1% Senior Citizen / Persons with Disability Fund | ✅ | **new** | In the AIP only inside `/`-combinations today; seeded for completeness. |
| `SEF` | Special Education Fund | ✅ | keep | — |
| ~~`TF`~~ | ~~Trust Fund~~ | ❌ | **deactivate** | Appears in the AIP only inside combinations, never standalone — no purely Trust-funded expenditure. Set `is_active = false` on the existing row. |
| ~~`CF`~~ | ~~Calamity Fund~~ | ❌ | **deactivate** | Folded into `LDRRMF` (see above). Set `is_active = false` on the existing row. |

Net: **7 active ceiling-managed funds** (GF, 20%DF, GAD, LDRRMF, PCPC, SCPWD, SEF); **Calamity Fund
and Trust Fund deactivated**.

## 4. Judgment calls — CONFIRMED (Ralph, 2026-07-15)

1. **Calamity Fund → LDRRMF merge — CONFIRMED.** LDRRMF is the post-RA 10121 name for the former 5%
   Calamity Fund; `Calamity Fund` / `5% CF` / `CF` stay as LDRRMF aliases and the Calamity row is
   deactivated.
2. **Trust Fund — CONFIRMED deactivate.** It appears in the AIP only inside `/`-combinations, never
   standalone, so no purely Trust-funded expenditure needs it selectable. Set `is_active = false`.

## 5. How to apply (import safety)

The CSV upsert keys on **`Code`** (`FundingSourceService.ImportCsvAsync`), so:

1. **Export the current funding sources CSV first** (Config → Funding Sources → export) and confirm
   the existing Codes for the 6 seeded rows. If any real Code differs from this file (e.g. GAD is
   stored as `GADF`, or 20% DF as `DF`), **update this CSV to the existing Code** before importing —
   otherwise the import creates a duplicate new row instead of updating the existing one.
2. Confirm the two judgment calls in §4.
3. The `aliases` column requires **RAL-157** to be merged first (it adds the column). Until then, the
   rename / new rows / deactivation can be applied without the aliases column; re-import with aliases
   once RAL-157 ships. This file is the seed referenced by RAL-157 step 6.
