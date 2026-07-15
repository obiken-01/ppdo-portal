# Funding Source Aliases — AIP naming analysis & seed data

> Supports the new v1.4.3 ticket: add an **aliases / other-names** column to the
> `funding_sources` config so the WFP entry form can map an AIP activity's free-text fund-source
> label to a canonical fund source (instead of only matching Code/Name and otherwise defaulting to
> General Fund).
> Source data: `AIP_2027_PGOM_Test.xlsm` — column K "Funding Source (7)" across all four sectoral
> sheets (GENERAL / SOCIAL / ECONOMIC / OTHERS), data rows 9+.

---

## 1. The problem, confirmed from the AIP workbook

AIP fund-source values are **free text with no controlled vocabulary**. The same fund appears under
many spellings, and many rows list **multiple funds** separated by `/`. Distinct values observed
(overall row counts):

| Rows | AIP value | Maps to |
|---|---|---|
| 588 | `GF` | General Fund |
| 136 | `General Fund` | General Fund |
| 17 | `General fund` | General Fund |
| 1 | `Gen Fund` | General Fund |
| 591 | `20% DF/NGAs` | **multi** (20% DF + external NGAs) |
| 89 | `GF/20% DF` | **multi** |
| 69 | `20% DF` | 20% Development Fund |
| 8 | `20 % DF` | 20% Development Fund |
| 5 | `20%DF` | 20% Development Fund |
| 68 | `GAD FUND` | 5% GAD Fund |
| 53 | `GAD Fund` | 5% GAD Fund |
| 43 | `5% GAD` | 5% GAD Fund |
| 18 | `GAD` | 5% GAD Fund |
| 9 | `5% GAD Fund` | 5% GAD Fund |
| 4 | `GAD fund` | 5% GAD Fund |
| 157 | `LDRRMF` | 5% LDRRMF |
| 9 | `LDRRM Fund` | 5% LDRRMF |
| 8 | `5% LDRRMF` | 5% LDRRMF |
| 31 | `SEF` | Special Education Fund |
| 13 | `5% CF` | Calamity Fund *(only if active — see O2)* |
| 157 | `TF/DOH/NGAs/20% DF` | **multi** |
| 56 | `TF/DOH HFEP/20% DF` | **multi** |
| 47 | `DOH` | **external** (not a PPDO-managed fund) |
| 5 | `NGA (ER 1-94)` | **external** |
| 4 | `TIEZA` / 1 `TESDA` | **external** |
| … | `GF/TF/SC/PCPC`, `20% DF/NGAs/5%GAD`, `LDRRMF/GAD`, `Gen/Trust Fund`, … | **multi** |

Full distinct list (≈70 variants) is in the analysis output; the table above is the representative
set. Two structural facts drive the design:

1. **Single-token variants** (`GF`, `General fund`, `GAD FUND`, `20 % DF`, `LDRRM Fund`, …) *can* be
   mapped to one canonical fund via an alias table — this is what the new column is for.
2. **Slash/newline/comma combinations** (`GF/20% DF`, `TF/DOH/NGAs/20% DF`, …) are genuinely
   **multiple** fund sources and stay **ambiguous → unselected** in WFP entry (decision D5) — an
   alias column does not resolve these, and shouldn't try to.

---

## 2. Matching rule (for the WFP entry resolver)

Normalize before comparing:
- trim; collapse internal whitespace and newlines to a single space; case-insensitive;
- treat `20%DF` == `20% DF` == `20 % DF` (normalize spaces around `%`).

Then:
- if the normalized value contains `/`, `,`, or a newline → **multi/ambiguous → leave unselected**
  (do not alias-match a combination);
- else match the single token against the fund source's **Code**, **Name**, or any **alias**;
- if still no match and the value is blank/none → **default to General Fund** (D5);
- if no match but the value is a non-empty single unknown token (e.g. `DOH`, `TIEZA`) → leave
  unselected (it is an external fund the office doesn't hold a ceiling for).

---

## 3. Seed aliases (pipe-delimited `aliases` column)

Aliases are matched **in addition to** Code and Name, so there's no need to repeat the exact Code or
Name. Recommended delimiter: `|` (pipe) — avoids collision with the CSV comma and the `/` that
appears inside AIP values.

| Canonical fund source | Code | `aliases` (pipe-delimited) |
|---|---|---|
| General Fund | `GF` | `General fund\|Gen Fund\|Gen. Fund\|Gen Fund/` |
| 20% Development Fund | `20%DF` | `20% DF\|20 % DF\|20%DF\|DF\|20% Development Fund` |
| 5% GAD Fund | `GAD` | `GAD Fund\|GAD FUND\|GAD fund\|5% GAD\|5% GAD Fund\|Gender and Development Fund` |
| 5% Local Disaster Risk Reduction and Management Fund | `LDRRMF` | `LDRRM Fund\|5% LDRRMF\|LDRRMF Fund` |
| 1% Provincial Council for the Protection of Children Fund | `PCPC` | `PCPC Fund\|1% PCPC` |
| 1% Senior Citizen / Persons with Disability Fund | `SCPWD` | `SC\|Senior Citizen\|PWD\|1% SC` |
| Special Education Fund | `SEF` | `Special Education Fund` |

> **Depends on O2 (config reconciliation).** The exact `Code` values above are suggestions — align
> them with whatever the `funding_sources` table already carries. In particular:
> - Confirm the GAD row's real Code (seed today may be `GAD` with Name "Gender & Development Fund").
> - `PCPC` and `SCPWD` appear in the AIP **only inside combinations** (e.g. `GF/TF/SC/PCPC`), never
>   standalone — so their aliases won't match any single-value AIP row in this workbook, but the
>   column should still carry them for completeness / future data.
> - **Calamity Fund** (`5% CF`, `CF`) and **Trust Fund** (`TF`) appear in the AIP but are not in the
>   seven target funds. If they remain active, seed them too: CF → `5% CF|CF`, TF → `TF|Trust Fund`.

External labels seen in the AIP that must **not** be aliased to any PPDO-managed fund (they stay
unmatched → unselected): `DOH`, `DOH HFEP`, `NGAs`, `NGA (ER 1-94)`, `NDRRMC`, `TIEZA`, `TESDA`,
`DA-BAFE`, `PHILMEC`, `PGO`, `PCSO`, `GSO`, `Brgy. Aid`, `Outsource`, `PDL`, `ELCAC`.
