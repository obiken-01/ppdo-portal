# AIP amounts — what each screen, API and file shows

> **For the PDC demo and for anyone reconciling two figures that "should" match.** The portal keeps
> one set of amounts — exactly what each office encoded — and shows them in five ways depending on
> where you look. None of them is wrong; each answers a different question.
>
> Current to v1.8.0 as of 2026-09-14. The Annex B Excel export is PPDO-84; the external
> API is a draft.

---

## 1. The five ways an amount is shown

| State | What it means | Used for |
|---|---|---|
| **Full value (₱)** | Exactly what the office encoded, in pesos, to the centavo — ₱1,000,400.00 | Typing an amount, the Budget Planning dashboard, Allocation, WFP, every API response |
| **Full value (₱000)** | The same exact amount divided by 1,000 to match the form's unit — 1,000.40. Nothing is rounded to the thousand; only the display stops at two decimals (₱10), and the saved amount keeps every peso | Reading amounts on AIP Entry, AIP Records and AIP Review |
| **Rounded up** | Each figure rounded **up** to the next ₱1,000, then added together. No +30% | The **General Fund ceiling check** only |
| **Rounded up, with +30%** | MOOE and CO are raised by 30%, then every figure (PS, MOOE, CO, climate change) is rounded up to the next ₱1,000, then added. **FY2028 onward only** | The **official AIP form** — the Consolidated AIP page, its Excel file, and the "printed" figures in the external API |
| **Abbreviated** | A full value shortened for a small card — ₱1.5M | Readiness board cards only. The exact figure is one click away in the table |

The two rounded states are shown in ₱000 on screen and in Excel, and in pesos in the API — the unit
column in each table below says which.

⚠️ **The ceiling is checked against "rounded up", but the form prints "rounded up, with +30%".** An
office that encodes exactly up to its ceiling passes every check and prints an AIP whose MOOE and CO
read up to 30% over that ceiling. This is intended (DECISION G) — the printed form carries a note
saying so.

---

## 2. One activity, followed everywhere

An activity with **PS ₱500,250**, **MOOE ₱1,000,400** and **no CO**, funded from the General Fund,
in FY2028:

| Where you look | PS | MOOE | CO | Total | State |
|---|---|---|---|---|---|
| **As encoded** (stored) | ₱500,250.00 | ₱1,000,400.00 | — | ₱1,500,650.00 | Full value (₱) |
| **AIP Entry / AIP Review** | 500.25 | 1,000.40 | — | 1,500.65 | Full value (₱000) |
| **Ceiling strip — "Encoded (MOOE + CO)"** (₱000) | *not counted* | 1,001.00 | — | 1,001.00 | Rounded up |
| **Consolidated AIP page and Excel** (₱000) | 501.00 | 1,301.00 | — | 1,802.00 | Rounded up, +30% |
| **Budget Planning dashboard** | | | | ₱1,500,650.00 | Full value (₱) |
| **Readiness board card** | | | | ₱1.5M | Abbreviated |

How the printed MOOE is reached: ₱1,000,400 × 1.3 = ₱1,300,520 → rounded **up** to ₱1,301,000 →
shown as **1,301.00** in thousands. PS gets no +30%: ₱500,250 → ₱501,000 → **501.00**. The row Total is
the sum of the printed figures, **1,802.00** — never a separate rounding of ₱1,500,650.

---

## 3. Screens

| Screen | Figure | Unit | State |
|---|---|---|---|
| **AIP Entry** | Activity row PS / MOOE / CO / Total, expenditure line amounts | ₱000 | Full value (₱000) |
| AIP Entry | Amounts **while typing** — line PS / MOOE / CO, item unit price and line total | ₱ | Full value (₱), with a `= 1,000.40 ₱000` echo under the box |
| AIP Entry — ceiling strip | **General Fund ceiling**, as PBO published it | ₱000 | Full value (₱000) |
| AIP Entry — ceiling strip | **Encoded (MOOE + CO)** — General Fund lines only, PS exempt | ₱000 | **Rounded up** |
| AIP Entry — ceiling strip | **Remaining** = ceiling − encoded | ₱000 | Full value (₱000) minus rounded up; may be negative |
| **AIP Records** (the record tree) | Every amount | ₱000 | Full value (₱000) |
| **AIP Review** — one office, activity modal | Every amount | ₱000 | Full value (₱000) |
| **Consolidated AIP** (FY2028+) | Columns PS, MOOE, CO, Total, CC adaptation, CC mitigation; office subtotals; TOTAL | ₱000 | **Rounded up, +30%** |
| Consolidated AIP (FY2027 and earlier) | Same columns — old years are not re-rendered | ₱000 | Full value (₱000) |
| **Budget Planning dashboard** | Money tiles — ceiling, allocated, costed in AIP, remaining | ₱ | Full value (₱) |
| Dashboard — Divisions table | Allocated, costed in AIP, remaining | ₱ | Full value (₱) |
| Dashboard — Offices table | Ceiling, costed in AIP | ₱ | Full value (₱) |
| Dashboard — readiness board | "₱1.12M of ₱10M" on each card | ₱ | **Abbreviated** |
| **Allocation** | Ceilings, division allocations | ₱ | Full value (₱) |
| **WFP**, **Report** (WFP / PPMP) | Every amount | ₱ | Full value (₱) — these are not the AIP form |

---

## 4. JSON — what the API returns

Every amount the portal's API returns is in **pesos** — no endpoint sends ₱000. The screens divide.

| Endpoint | Field | State |
|---|---|---|
| AIP records, AIP Entry and AIP Review reads | Activity `ps` / `mooe` / `co` / `total`, expenditure lines, procurement items | Full value (₱) |
| `GET …/aip/{id}/readiness` | `ceiling.ceiling` | Full value (₱) |
| | `ceiling.encodedBaseRounded` | **Rounded up** (₱) |
| | `ceiling.remaining` | `ceiling` − `encodedBaseRounded` (₱) |
| `GET …/aip/consolidated` | Every row's `amounts`, and `total` | **Rounded up, +30%** (₱), FY2028+; Full value (₱) for FY2027 and earlier |
| `GET …/dashboard`, `…/dashboard/offices` | Ceiling, allocated, costed | Full value (₱) |
| **External AIP API** (draft, `docs/external-api/`) | `amounts` | Full value (₱) — for ceilings and WFP limits |
| | `printedAmounts` (FY2028+) | **Rounded up, +30%** (₱) — matches the signed form |

The external API writes money as a **decimal string** (`"1000400.00"`), not a number, so no
consuming system turns it into a float.

---

## 5. Excel files

| File | Direction | Unit | State |
|---|---|---|---|
| **AIP Annex B export** — Consolidated AIP page (PPDO-84) | Out | ₱000 | **Rounded up, +30%.** Activity cells hold the printed values; row Total, office subtotals and TOTAL are `SUM` formulas over them |
| AIP `.xlsm` upload — FY2027 and earlier | In | The file is in ₱000; it is stored ×1,000 as pesos | File: Full value (₱000) → stored: Full value (₱) |
| WFP report export | Out | ₱ | Full value (₱) |
| PPMP report export | Out | ₱ | Full value (₱) |

---

## 6. Questions this answers in a demo

**"Why does AIP Entry say 1,000.40 but the Consolidated AIP says 1,301.00?"** Entry shows what was
typed, in thousands. The Consolidated AIP shows the form: +30% on MOOE, then rounded up to the thousand.

**"The dashboard says ₱1,500,650.00 and AIP Entry says 1,500.65 — which is right?"** Both. The same
amount, once in pesos and once in thousands of pesos.

**"Why does the ceiling strip say 1,001.00 when the activity is 1,000.40?"** The ceiling is checked the
way the form is built — each figure rounded up first — but **without** the 30%.

**"Three ₱1,200 activities show 3.60 in the tree but 6.00 as encoded."** Rounding happens per figure,
before adding: 2 + 2 + 2 = 6, not 3.6 rounded up to 4. The form is built the same way.

**"The printed AIP is over the ceiling."** By up to 30% on MOOE and CO — the uplift the PDC decided to
print, while ceilings stay checked against the encoded amounts.

**"Can the numbers change before the final form?"** One rule is still provisional: whether the 30% is
added **before** or **after** rounding (tracker G5). Today it is before. Changed, ₱1,000,400 MOOE would
print 1,302.00 instead of 1,301.00 — at most ₱1,000 per figure.

---

## 7. For developers — where each state is computed

| State | Server | Browser |
|---|---|---|
| Full value (₱) | Stored in pesos (`MigrateAipAmountsToPesos`, DECISION E); every DTO carries pesos | `formatMoney` (`lib/money.ts`) on dashboard, allocation, WFP; `fmtPesos` (`lib/aip-units.ts`) beside AIP inputs |
| Full value (₱000) | — never sent; the server has no ₱000 field | `fmtThousands` (grid cells, zero → blank) and `fmtThousandsReadout` (readouts, zero → `0.00`) in `lib/aip-units.ts` — divide by 1,000, no rounding to the thousand |
| Rounded up | `AipRounding.UpToThousand`, summed in `AipCeilingService` → `AipCeilingStatusDto.EncodedBaseRounded` | Renders the server's figure with `fmtThousandsReadout`; never rounds |
| Rounded up, +30% | `AipPrintedFigures.ForActivity` / `Sum`, via `AipFormRowBuilder` — the one source for the Consolidated grid, the Excel export and the external API's `printedAmounts` | `fmtThousands` on the server's figure; never rounds or uplifts |
| Abbreviated | — | `formatMoneyShort` in `lib/money.ts` (readiness board) |

⚠️ **The browser never rounds or applies the 30%.** Every rounded figure arrives from the server. A
second rounding in the page is how two screens come to disagree.

⚠️ **Never assert `printed = 1.3 × encoded`.** Once both are rounded they are not 1.3× each other.

Related: [AIP_Form_Spec.md](AIP_Form_Spec.md) §6 and Part II ·
[Phase_Plan.md](Phase_Plan.md) §12.2 (DECISION G) ·
[../external-api/README.md](../external-api/README.md) §2
