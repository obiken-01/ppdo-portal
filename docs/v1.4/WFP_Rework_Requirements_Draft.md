# WFP Rework (v1.4) — Requirements Draft (reviewed & expanded)

> **Status:** DRAFT for the requirements discussion. Base text = Ralph's 2026-07-07 draft from
> the demo/meeting; reviewed against `docs/v1.4/WFP_New_Form_Findings.md` (the WFP-NEW.xlsx
> analysis) and the current v1.2 codebase. Everything marked **★ REC** is a proposed
> addition/change for better backend or UI/UX flow — confirm or strike. Everything marked
> **⚠ FLAG** is a contradiction or gap that needs a decision.
>
> Companion doc: [WFP_New_Form_Findings.md](WFP_New_Form_Findings.md) (form column map,
> reference-data deltas, template defects, original 13 questions).

---

## 1. Scope in one paragraph

Rebuild WFP entry around the new form: users build each activity's budget from **expenditure
entries** (account + frequency + amounts), where **procurement** entries are computed bottom-up
from **line items** (name × qty × price, reusable as presets) instead of estimated, and
**non-procurement** entries are typed per frequency period. The system rolls periods up to
Q1–Q4 / Net / Total, enforces reserve rules, and **monitors AIP budget + division allocation
ceilings live**. Divisions remain the DATA-ENTRY scope; the generated **report is per office**,
laid out exactly like WFP-NEW.xlsx (per-fund-source blocks). New config pages: expanded chart
of accounts, price index, procurement presets.

## 2. Frequency model

| Code | Name | Periods entered | Roll-up to WFP quarters |
|---|---|---|---|
| M | Monthly | 12 (Jan–Dec) | Q1 = Jan+Feb+Mar, Q2 = Apr+May+Jun, Q3 = Jul+Aug+Sep, Q4 = Oct+Nov+Dec |
| Q | Quarterly | 4 (Q1–Q4) | direct |
| B | Bi-annual | 2 (1st Half, 2nd Half) | 1st Half → Q1, 2nd Half → Q3 |
| A | Annual | 1 | → Q1 by default; *"possible it will be in Q4"* |

- **★ REC — store the period amounts, not just the quarter roll-ups.** A child table
  (`wfp_expenditure_periods`: period_no, amount) keeps the data at the grain the user entered
  it. Benefits: re-opening an entry shows the original 12 monthly values (not a lossy Q
  split), carry-forward works naturally, and a monthly cash-program report becomes possible
  later at zero schema cost. Quarters/Net/Total are always **computed server-side**, never
  stored as entered facts (matches the "app owns the math" conclusion from the findings —
  the template's own formulas are full of copy-paste errors).
- **★ REC — Annual placement is a per-entry choice, not a rule.** Since A "is in Q1 although
  it's possible it will be in Q4", give Annual entries a small "Charge to: Q1 | Q2 | Q3 | Q4"
  selector, default Q1. Cheaper than a rule change later, and covers mid-year one-offs.
  *(Same question could apply to B — assume fixed Q1/Q3 unless PPDO says otherwise.)*
- **⚠ FLAG — frequency is per EXPENDITURE, not per activity** (implied by "user can add new
  expenditure under the same activity"): one activity can hold monthly utilities and an annual
  license. Confirm — this drives where the frequency picker sits in the flow (§4).

## 3. Where each fact lives (entry grain)

| Fact | Level | Notes |
|---|---|---|
| Program / Project / Activity | Picked from the **Final AIP** hierarchy | Users never type ref codes; WFP PPAs must exist in AIP (⚠ amendment behavior — Q-list) |
| Resources Needed, Responsible Person/Unit, Success Indicator, Means of Verification, Outcome Indicator, Target Beneficiaries | **Project level, optional** (demo decision) | ⚠ FLAG: the printed form shows these columns on the ACTIVITY row (F–K guidance text sits on the activity line). Storing at project level is fine — but confirm the report prints them on the project row, or repeats them per activity |
| Function band (CORE/STRATEGIC/SUPPORT) | Program level (report layout needs it) | ⚠ FLAG: absent from the draft flow but required to lay out the report. Needs a home — see §9 Q3 |
| Fund source | Per expenditure entry, **defaulted from the AIP activity's funding source**, overridable | Report groups by it (one form block per fund) |
| Frequency, Nature (Procurement/Non-proc), Account, Reserve toggle | Per expenditure entry | |
| Amounts | Per period (non-proc) or per period × line item (procurement) | |
| Division | On the WFP record (entry container) — "division is only introduced to WFP for population/data entry" | Report merges divisions per office |

## 4. Entry flow (reviewed)

### 4.1 Ordering — recommendation on the open point

The draft has: *Program → Project → Frequency → Activity → reserve → fund source → account*
and asks whether Activity-before-Frequency is better. **★ REC: yes — pick the Activity first,
and move Frequency into the expenditure entry itself:**

```
CONTEXT (sticky, chosen once, reused across entries)
  1. Program        (Lookup from Final AIP, scoped to user's office/division assignments)
  2. Project        (Lookup; + optional descriptive-fields accordion, project-level)
  3. Activity       (Lookup; shows the AIP activity's budget + fund source as context)

EXPENDITURE ENTRY (repeats; "+ Add expenditure" under the current activity)
  4. Account        (searchable: code + object title; shows nature + reserve-eligibility badges)
  5. Nature         (auto from account config; editable only if account allows both — §5.3)
  6. Frequency      (M / Q / B / A)
  7. Fund source    (default = AIP activity's fund source; overridable)
  8. Reserve        (toggle; enabled only for reserve-eligible accounts; default amount = 10% — §6)
  9. Amounts        (frequency grid OR procurement item table — §5)
 10. Review & save  → stay in context: "Add another expenditure" / "Change activity" / "Done"
```

Why this order beats frequency-first:
- Frequency describes **how one expenditure is spread**, not the activity — choosing it before
  the activity forces a re-selection round-trip whenever two accounts under the same activity
  differ in frequency (the common case: monthly electricity + quarterly training).
- Account-before-frequency lets the account's config drive the screen (nature → which entry
  UI; reserve eligibility → whether the toggle shows) so the user never sees an irrelevant step.
- The draft's own loop ("add new expenditure under the same activity") already treats
  activity as the sticky context — this just makes the UI match.

### 4.2 Context header (always visible during entry)

**★ REC:** a sticky header with the two ceilings the draft says we must monitor:

```
┌────────────────────────────────────────────────────────────────────────┐
│ FY 2027 · PBO · Planning Division                                       │
│ Division allocation: ₱2,400,000 original · ₱1,150,000 remaining  [bar] │
│ AIP budget — this activity: ₱500,000 · WFP entered: ₱380,000     [bar] │
└────────────────────────────────────────────────────────────────────────┘
```

Both meters refresh from the server after every save (computed at read time — no cached
"remaining" column to drift; two users in one division can enter concurrently).

## 5. The two amount-entry modes

### 5.1 Non-procurement — frequency grid

- One input per period (12/4/2/1 per §2), `MoneyInput`, pesos.
- **Carry-forward:** draft says "when user click next, the previous value will remain the
  same". **★ REC: make it explicit instead of implicit** — after the first period is typed,
  offer **"Apply to all remaining periods"** and a per-period "copy previous" affordance.
  Implicit auto-fill surprises users who have one different month; explicit fill is just as
  fast (one click) and predictable. Tab/Enter advances through the grid.
- Live computed strip under the grid: per-quarter roll-up → Net → (+Reserved) → Total.

### 5.2 Procurement — line-item table per period

- Table columns: **Item name · Unit · Qty · Unit price · Line total (computed)**.
  **★ REC: add `Unit`** (ream/box/piece/liter) — the draft has name/qty/price only, but a
  price index without units produces ambiguous prices, and GSO/procurement docs always carry
  units. Cheap now, painful to retrofit.
- **Item name + price come from the Price Index** (§7.1): typeahead search; picking an entry
  fills name/unit/price. Price is **snapshotted** onto the WFP item (editable) — later price-
  index updates must not silently change a saved WFP.
- **Per-period tables with carry-forward:** the same item set persists as the user steps to
  the next period (per draft); qty editable per period. "Copy previous period" / "Apply items
  to all periods" buttons, same explicitness rule as 5.1.
- **Period amount = Σ(qty × price)** for that period; roll-ups as usual. The WFP row still
  shows only account/approp/reserved/Q1–4 — items exist so Total Appropriation is **computed,
  not estimated** (draft's NOTE, kept as a hard rule: line items are entry scaffolding;
  the printed form is unchanged).
- **Presets:** "Save as preset" (named, tied to the account) and "Load preset" in the table
  toolbar. Loading copies preset items into the entry (snapshot, editable) — presets are
  templates, not live links. Maintained in the preset config page (§7.2).
  **★ REC:** capture presets *from* real entries (the natural flow the draft describes) AND
  allow curating them in config; both write the same `procurement_presets` rows.

### 5.3 ⚠ FLAG — nature of implementation has THREE values in the form

The form's dropdown is `Procurement / Non-procurement / **Combined**`; the draft only
branches two ways. Also, the draft derives the branch from the ACCOUNT ("depending if the
selected account code is Procurement or non-Procurement") while the form has nature as a
per-LINE dropdown. **★ REC:** put a `default_nature` on the account config (most accounts are
unambiguous — salaries never procure; office supplies always do), let the line inherit it,
and allow override only where the account is marked ambiguous. **Ask PPDO what "Combined"
means operationally** — if it's "part items, part direct amount," the entry could allow both
sub-entries under one account line; if it's rare, drop it and split into two lines.

## 6. Reserve rule

- **✔ RESOLVED (Ralph, 2026-07-07) — the rate is 10%**; the draft's "19%" was a typo. Matches
  the form's "Equiv. to 10% of Operational Expenses" and the `Accounts with Reserve` sheet's
  "maximum of 10%" note. Still open: hard cap vs. editable default, and 10% *of what* base
  (§11 Q1).
- Eligibility comes from config: `accounts.is_reserve_eligible` (the 42 flagged MOOE accounts
  from the workbook's `Accounts with Reserve` sheet). The toggle in the entry flow only
  appears for eligible accounts. *(⚠ the workbook's own sample reserves on a PS account —
  eligibility list vs. sample contradiction is Q4 in the findings; the config flag makes
  either answer a data change, not a code change.)*
- **★ REC:** when toggled on, default `ReserveAmount = 10% × Total` (rate from a config value,
  not hard-coded), editable downward, validated `≤ cap`. Reserved is **excluded from the
  quarterly release plan** (form: Net = ΣQ, Total = Reserved + Net) — so the quarters the user
  types are the NET plan; the UI must label this clearly ("amounts to be released, net of
  reserve").
- Current schema already has `ApplyReserve` + `ReserveAmount` on `WfpExpenditureLine` — reuse.

## 7. Config pages (new)

### 7.1 Price Index
- `price_index_items`: name, unit, unit_price, category?, is_active (+ audit). Snake_case,
  soft delete, `{data,error,message}` envelope, CSV upsert/export — clone of the existing
  config-page pattern (accounts/offices/funding_sources).
- Searched from the procurement item table (typeahead by name).
- **★ REC:** track `price_updated_at` and show it in search results — stale prices are the
  main data-quality risk of a price catalogue. (Full price history = out of scope unless PPDO
  asks.)
- ⚠ Q: who maintains it (PPDO only via `CanManageConfig`, or offices too)?

### 7.2 Procurement Presets
- `procurement_presets`: account_id, name, is_active; child `procurement_preset_items`:
  price_index_item_id?, name/unit/price snapshot, default_qty.
- Created from the entry flow ("Save as preset") or the config page; loading always copies.
- ⚠ Q: presets shared across all offices/divisions, or scoped? (Draft implies shared —
  "can be used by other activity that will procure same items." Recommend shared + created_by
  shown.)

### 7.3 Chart of Accounts (expanded) — prerequisite for everything above
- Import the ~318-account NGAS chart from the workbook's VALIDATION sheet; add explicit
  `expense_class` (PS/MOOE/CO — the `1 07 xx` CO asset accounts break the current `5-0x`
  prefix rule), `default_nature`, `is_reserve_eligible`. Canonicalize code format (space vs
  dash) once, at import.
- This is a **config migration with blast radius** (existing WFP lines, reports, config UI
  filter by prefix-derived type today) — its own ticket, first in sequence.

#### 7.3.1 Can procurement/non-procurement be derived from the existing account data? (investigated 2026-07-07)
Revisited the v1.1 Chart-of-Accounts artifacts: `annex_b_charts_of_account.pdf` (DBM/COA Annex B)
→ the extracted `AIP/chart_of_accounts.csv` (571 rows: `account_title, account_number,
normal_balance, description`). **There is NO explicit procurement flag in that source** — the PDF
never classifies accounts that way; it only carries title, code, normal balance, and a prose
usage description. So the column must be *added*, but it can be **pre-populated by rule** rather
than hand-tagging 300+ rows:

- **Primary signal = NGAS account GROUP** (the 3rd segment, `5-02-xx`). NGAS groups are already
  procurement-homogeneous by design — e.g. `5-02-03` Supplies & Materials = procurement,
  `5-02-01` Traveling = non-procurement, `5-02-12` General Services (janitorial/security) =
  procurement (contracted), `5-02-13` Repairs & Maintenance = procurement, `5-01-xx` PS = always
  non-procurement, `1-04/1-07` inventories & PPE = procurement.
- **Secondary signal = description keywords** (purchase/contract/construction/repair vs
  salary/allowance/subsidy/tax) — only needed to split the few *mixed* groups (`5-02-05`
  Communication: telephone = non-proc, internet subscription = proc; `5-02-06`; `5-02-99` Other
  MOOE catch-all).
- **A handful stay genuinely ambiguous** (`5-02-02` Training, `5-02-99-030` Representation,
  `5-02-99-990` Other MOOE) and are flagged `Combined`/low-confidence for a human to confirm —
  which is exactly why the form has the third **Combined** nature and why `default_nature` should
  be an *editable* config column, not a hard-coded rule.

**Validation:** the rule engine was cross-checked against the **19 real account→nature choices
observed in WFP-NEW.xlsx** (the `L` column of the sample) → **19/19 match**. A first-pass draft is
generated at **`D:\RalphFiles\PPDO\PPDO\AIP\accounts_nature_draft.csv`** with columns
`account_number, account_title, expense_class, default_nature, confidence (HIGH/MEDIUM/LOW),
is_reserve_eligible, rationale` — 306 expense/asset accounts, all **42** reserve-eligible accounts
flagged (incl. 4 semi-expendable codes `5-02-03-210/220`, `5-02-13-210/220` that are in the
WFP-NEW lists but were **missing from the older Annex-B PDF** → newer COA additions to append).

**Recommendation:** ship the draft CSV as the seed for the new columns, but treat `default_nature`
as reviewable config (PPDO confirms the LOW-confidence/Combined rows) — do **not** bake the
classification into code, so future COA changes are a data edit. Distribution of the draft:
~90% HIGH-confidence, ~7% MEDIUM, ~1.5% Combined/LOW.
- ⚠ The draft is a *proposal from rules + one sample file* — PPDO must sign off on the final
  procurement column before it drives Finalize-time validation.

## 8. Ceiling monitoring & validation (Division Allocation Fund)

Two independent checks, both **computed server-side** and returned with every save/read:

1. **AIP budget check (per activity):** Σ of WFP expenditure totals for the activity
   (across ALL divisions of the office) vs. the AIP activity's total.
   ⚠ **Units:** AIP amounts are stored in **thousands** — compare against `aip_total × 1000`;
   WFP is entered in pesos (per the workbook sample).
2. **Division allocation check:** user's division's `division_allocations.Amount` (original)
   minus Σ of ALL WFP expenditure totals entered by that division for the FY = **remaining**.
   Both values displayed in the context header (§4.2) per the draft.

**★ REC — warn on save, block on Finalize.** During drafting, exceeding a ceiling shows a
non-dismissable inline warning (amount over, which ceiling) but the save succeeds — offices
iterate and the numbers move. `Finalize` refuses while any ceiling is exceeded (admin
override = existing Unlock pattern). A hard block on every save makes trial-and-error entry
miserable; no block at all makes Final documents unreliable. ⚠ Confirm this split with PPDO.

**★ REC — also check the PPA→division assignment** (v1.2 `program_divisions`): if a program is
assigned to divisions, the Activity picker should only offer PPAs assigned to the user's
division (prevents two divisions budgeting the same activity by accident). ⚠ Confirm whether
assignment is mandatory or advisory.

## 9. Data-model sketch (evolves v1.2 — not greenfield)

Current `WfpExpenditureLine` already carries account snapshots, ApplyReserve/ReserveAmount,
Net, Q1–Q4, and fund-source snapshots — the rework **adds the entry scaffolding around it**:

```
wfp_records            (KEEP grain: aip_record_id, office_id, division_id, fiscal_year,
                        status Draft/Final/Archived — division = entry container)
wfp_nodes / reuse      program/project/activity refs from AIP + function_band (program)
                        + the six descriptive fields (project level, nullable)
wfp_expenditures       (per line: account_id + snapshots, nature, frequency,
                        funding_source_id + snapshots, apply_reserve, reserve_amount,
                        annual_quarter_choice; Q1–Q4/net/total COMPUTED on save)
wfp_expenditure_periods (expenditure_id, period_no 1–12/1–4/1–2/1, amount)
wfp_procurement_items  (expenditure_id, period_no, price_index_item_id?, name/unit/price
                        snapshot, qty; line_total computed)
price_index_items      (§7.1)
procurement_presets / procurement_preset_items (§7.2)
accounts               + expense_class, default_nature, is_reserve_eligible (§7.3)
```

Server computation pipeline on every save (one code path, used by entry AND report):
`period amounts (or Σ items per period) → quarter roll-up per §2 → Net = ΣQ →
Total = Net + Reserved → expense-class sub-totals → activity/project/program totals →
per-fund summary`. The report renders these values — never re-derives in Excel formulas.

⚠ Q: migrate/retire existing v1.1/v1.2 `wfp_activities`/`wfp_expenditure_lines` data?
(Findings Q13 — still open.)

## 10. WFP report

- Basis = WFP-NEW.xlsx layout: per **office**, one block per fund source used, function bands →
  program/project/activity rows → expense-class sections → lines → the totals cascade,
  summary block (PS / MOOE / CO / Creation rows / Grand total).
- Aggregates ALL of the office's division records; divisions never appear on the output.
- **★ REC:** an on-screen read-only "form view" (same layout) doubles as the pre-Finalize
  review screen; the `.xlsx` export writes values (or app-generated formulas), fixing the
  template's broken ranges rather than reproducing them.
- ⚠ "Creation" totals (PS CREATION / MOOE CREATION rows) still undefined — findings Q3. If
  they survive, a `is_creation` flag on the expenditure (or program) is the likely mechanism.

## 11. Open questions (updated — draft answered 4 of the original 13)

**Answered by this draft:** divisions = entry-only (Q1) ✔ · fund source is per-entry,
defaulted from AIP, report groups per fund (Q7-shape) ✔ · WFP PPAs come from AIP (Q9 first
half) ✔ · account config expansion confirmed needed (Q11) ✔.

**Still open / newly raised** *(2026-07-07: rate typo resolved — 10% confirmed; Q2 and Q3
below acknowledged by Ralph as pending with the team)*:
1. Reserve mechanics (rate = **10% ✔ confirmed**): hard cap or editable default? 10% of what
   base — the line's total, or the fund block's operational expenses?
2. What does nature **"Combined"** mean, and is nature account-derived or per-line? (§5.3)
   — *Ralph will ask the team.*
3. **Function bands** CORE/STRATEGIC/SUPPORT — chosen where (program property in WFP entry,
   AIP attribute, or office config)? Required for report layout. (§3) — *not yet answered
   to Ralph either; carry to the requirements meeting.*
4. Reserve eligibility: only the 42 MOOE accounts, or any line (sample reserves on PS)?
5. **"Creation"** totals — meaning + what marks a line/program as Creation? (§10)
6. Descriptive-fields **report rendering** (data itself is settled: entered once per project,
   optional). The six fields are COLUMNS on the printed form, so the generator must pick which
   ROW carries the text — **(A)** print once on the project row (recommended: matches where
   the data lives, no duplication), or **(B)** repeat the project's values on every activity
   row beneath it (what the sample workbook's guidance placement suggests). Cosmetic only —
   entry is unaffected. (§3)
7. Annual frequency → Q1 default with per-entry override OK? Bi-annual fixed Q1/Q3? (§2)
8. Ceiling checks: warn-on-save / block-on-finalize split OK? AIP check per activity total
   or per expense class? (§8)
9. "Remaining division allocation" = allocation − Σ WFP totals of that division-FY — right
   definition? Anything else draws it down?
10. Project-level expenditures (no activity) — allowed, per the form's Strategic block? (ties
    to RAL-108's identical AIP question)
11. Price index + presets: maintained by whom, shared across offices? (§7)
12. Existing WFP data: migrate or archive? AIP amendment mid-WFP behavior? Signature block?
    (findings Q9/Q12/Q13)

## 12. Suggested ticket breakdown (v1.4 milestone, dependency order)

| # | Ticket | Depends on |
|---|---|---|
| 1 | Config: expanded NGAS chart of accounts — explicit `expense_class`, `default_nature`, `is_reserve_eligible`; code canonicalization; migration + CSV + config-UI updates | — |
| 2 | Config: Price Index (schema + API + page, CSV round-trip) | — |
| 3 | Config: Procurement Presets (schema + API + page) | 1, 2 |
| 4 | WFP schema rework: expenditures + periods + procurement items; computation pipeline service (TDD: frequency roll-ups, reserve math, totals cascade) | 1 |
| 5 | Ceiling monitoring service: AIP-budget + division-allocation checks, remaining-allocation endpoint | 4 |
| 6 | Entry UI: context picker + expenditure wizard (frequency grid, procurement table, presets, live totals, ceiling meters) | 2–5 |
| 7 | WFP report: office-level generator in the new form layout + xlsx export + review screen | 4 |
| 8 | Migration/retirement of v1.1–v1.2 WFP entry + data | 4–7, Q12 answer |

---

*Reviewed/expanded by Claude Code 2026-07-07 from Ralph's demo-meeting draft + the WFP-NEW.xlsx
findings. Decisions pending: everything marked ⚠ FLAG and §11.*
