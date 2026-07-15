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

## 3. Reconciled seed (after) — UPDATED against the live export

Ralph exported the actual live `funding_sources` table (`20260715_143947_funding_sources_live.csv`,
2026-07-15) — the reconciliation described in the original version of this section (rename GAD,
add LDRRMF/PCPC/SCPWD, deactivate Trust/Calamity) had **already been applied directly in the DB**
before this doc's guesses were written. The live table already matches the 7-fund target exactly,
with these real Codes (note two differ from my original guess):

| Code (live) | Name | Active | Notes |
|---|---|---|---|
| `GF` | General Fund | ✅ | — |
| `20%DF` | 20% Development Fund | ✅ | — |
| `GAD Fund` | 5% GAD Fund | ✅ | **Code is `GAD Fund`, not `GAD`** as originally guessed. |
| `LDRRMF` | 5% Local Disaster Risk Reduction and Management Fund | ✅ | — |
| `PCPC` | 1% Provincial Council for the Protection of Children Fund | ✅ | — |
| `SC/PWD` | 1% Senior Citizen / Persons with Disability Fund | ✅ | **Code is `SC/PWD`, not `SCPWD`** as originally guessed. |
| `SEF` | Special Education Fund | ✅ | — |

**No Trust Fund or Calamity Fund row exists in the live table at all** — nothing to deactivate;
the earlier plan's §4 judgment calls are moot (there was never a Calamity/Trust row to merge or
turn off). Net: **7 active funds, matching the target exactly.** The only remaining gap is the
`aliases` column itself (RAL-157's schema addition) and populating it — that's what
`docs/v1.4.3/seed/funding_sources_seed.csv` now does, using these exact live Codes/Names/
descriptions/colors so importing it only adds aliases and touches nothing else.

## 4. How to apply (import safety)

The CSV upsert keys on **`Code`** (`FundingSourceService.ImportCsvAsync`). `funding_sources_seed.csv`
already uses the live Codes above verbatim (including `GAD Fund` and `SC/PWD`), so importing it after
RAL-157 merges will update each existing row in place — no duplicates, no other field changes besides
adding `aliases`.
