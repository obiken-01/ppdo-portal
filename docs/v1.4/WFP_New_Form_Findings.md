# WFP Rework (v1.4) — New WFP Form: Findings & Open Questions

> **Source file:** `D:\RalphFiles\PPDO\PPDO\WFP-NEW.xlsx` (received 2026-07-06, analyzed 2026-07-07).
> **Status:** PRE-REQUIREMENTS analysis. PPDO's detailed requirements are expected 2026-07-07;
> this document captures what the reference workbook itself tells us, what it changes vs. the
> current WFP module, and the questions to settle in the requirements discussion. Update or
> supersede this doc once requirements land — on conflict, the requirements win.
>
> **Context:** after the 2026-07-06 team demo, the WFP module is being redone — new UI/UX flow,
> new configuration entities, and a new WFP entry form that the WFP **report output** will be
> based on. Linear milestone: **v1.4 — WFP Rework** (`6773dac5-2a20-4eed-b144-94c0bf0ff802`).

---

## 1. Workbook anatomy

| Sheet | What it is |
|---|---|
| `WFP FINAL` | The form itself — **three identical per-fund-source blocks stacked on one sheet** for a single office (Provincial Budget Office in the sample): **General Fund** (rows 2–82), **Gender & Development Fund** (rows 88–168), **20% Development Fund** (rows 176–256). Each block is a complete WFP: header → PPA hierarchy with expenditure lines → per-fund summary totals. |
| `VALIDATION` | Dropdown source lists: nature of implementation (C), account code → object of expenditure (D:E, 318 rows), fund sources (J, 6 entries), expense classes (K), offices with office codes (M:N, 19 entries). |
| `Accounts with Reserve` | 42 MOOE account codes flagged `*` — the accounts subject to the reserve rule. Footnote: *"Equivalent of maximum of 10% Reserve on Operational Expenses."* |

No signature block (Prepared/Reviewed/Approved by) anywhere in the file.

## 2. Form structure (per fund block)

### 2.1 Header
```
WORK AND FINANCIAL PLAN FY 2027
FY 2027
DEPARTMENT/OFFICE: <dropdown ← VALIDATION!M3:M27>
SOURCE OF FUND:    <dropdown ← VALIDATION!J3:J13>
```

### 2.2 Columns (B → V; W is a hidden-ish checksum)

| Col | Header | Behavior in the sheet |
|---|---|---|
| B | AIP REF CODE | Program (6-seg) / Project (7-seg) / Activity (8-seg) codes — same scheme as our AIP module |
| C/D/E | PROGRAMS, PROJECTS AND ACTIVITIES | Indent columns: C = program title, D = project title, E = activity title. E is where the guidance says the descriptive fields apply |
| F | RESOURCES NEEDED | Free text (guidance: human/financial/material/equipment resources) — **activity-level** |
| G | RESPONSIBLE PERSON/UNIT | Free text — activity-level |
| H | SUCCESS INDICATOR | Free text — activity-level |
| I | MEANS OF VERIFICATION | Free text — activity-level |
| J | OUTCOME INDICATOR | Free text — activity-level |
| K | TARGET BENEFICIARIES | Free text — activity-level |
| L | NATURE OF IMPLEMENTATION | Dropdown per **expenditure line**: `Procurement` / `Non-procurement` / `Combined` |
| M | ACCOUNT CODE | Dropdown per line ← VALIDATION!D2:D337 (space-separated codes, e.g. `5 01 01 010`) |
| N | OBJECT OF EXPENDITURE | `=VLOOKUP(M, VALIDATION!$D$3:$E$403, 2, 0)` — account title auto-fill |
| O | TOTAL APPROPRIATION | `=SUM(P:Q)` → **Reserved + Net** |
| P | RESERVED | Manual amount ("Equiv. to 10% of Operational Expenses" note sits over this column) |
| Q | NET APPROPRIATION | `=SUM(R:U)` → sum of the four quarters |
| R–U | TIME FRAME (1st–4th Quarter) | Manual quarterly amounts |
| V | AMOUNT TO BE RELEASED | `=SUM(R:U)` — numerically identical to Q in every row |
| W | *(unlabeled)* | Checksum `=SUM(lines) - subtotal` on a few subtotal rows (should be 0) |

Amounts in the sample are plain pesos (25,000 quarterly on salaries) — **not** the ×1000
thousands convention AIP/LDIP use. (Confirm — §8 Q10.)

### 2.3 Row hierarchy (top → bottom inside one fund block)

```
FUNCTION BAND:  CORE FUNCTIONS | STRATEGIC FUNCTIONS | SUPPORT FUNCTIONS   ← NEW concept
 └─ PROGRAM        (B: 6-seg ref code, C: title, "no account code")
     └─ PROJECT    (B: 7-seg ref code, D: title)
         └─ ACTIVITY  (B: 8-seg ref code, E: title, F–K descriptive fields)
             └─ EXPENSE-CLASS SECTION: PERSONAL SERVICES / MOOE / CAPITAL OUTLAY
                 └─ EXPENDITURE LINE: L nature, M account, N object, O/P/Q, R–U quarters, V
                 └─ SUB-TOTAL (per expense class)
             └─ ACTIVITY GRAND TOTAL (echoes the activity ref code)
     └─ [PROJECT GRAND TOTAL — only where lines attach at project level]
 └─ PROGRAM GRAND TOTAL (echoes the program ref code)
FUND-BLOCK SUMMARY:
   TOTAL - PERSONAL SERVICES
   TOTAL - MOOE (Excluding Creation)
   TOTAL - CAPITAL OUTLAY
   TOTAL - PERSONAL SERVICES CREATION      ← "Creation" concept unclear (§8 Q3)
   TOTAL - MOOE - CREATION
   GRAND-TOTAL
```

Two structural liberties the sample takes, both relevant to the data model:
- **Expenditure lines can attach directly at PROJECT level** (Strategic Functions block: project
  `…-003-001` carries MOOE + CO lines with a PROJECT GRAND TOTAL and no activity row). Echoes the
  RAL-108 "detail above the leaf" problem from AIP.
- One program (`…-002`) has its MOOE section header **above** the activity row — the section/
  activity nesting is loose in practice; the app should impose a strict one.

## 3. Reference data (VALIDATION sheet) vs. current config

| List | Contents | vs. current system |
|---|---|---|
| Fund sources (J3:J8) | General Fund, Special Education Fund, Trust Fund, Calamity Fund, 20% Development Fund, Gender & Development Fund | **Exactly matches** our 6 seeded `funding_sources` ✅ |
| Offices (M3:N21, 19 rows) | Office name + numeric office code (PPDO = `1041`, PBO = `1071`, …) | Matches `offices.office_code` scheme, but 19 entries vs. 16 seeded — includes PGO sub-entries `1011-A`, `1011-2`, `1011-3` (⚠ §8 Q6) |
| Nature of implementation (C3:C5) | `Procurement` / `Non-procurement` / `Combined` | **NEW** — no equivalent anywhere in the system. Likely a new enum or tiny config list |
| Account codes (D3:E323, 318 rows + `N/A`) | Space-separated NGAS codes → object-of-expenditure titles | **Much bigger than our 143-account config**, and breaks two current assumptions (below) |
| Expense classes (K3:K5) | MOOE / CO / PS | Same three classes we already use |

### ⚠ The account list breaks two current config assumptions
1. **Format**: codes are space-separated (`5 01 01 010`), ours are dash-prefixed (`5-01-…`).
   Cosmetic, but import/matching needs a canonical form.
2. **Classification**: our `accounts` config derives PS/MOOE/CO from the `5-01-/5-02-/5-03-`
   prefix. The new form's **Capital Outlay lines use asset accounts starting `1 07 xx xxx`**
   (e.g. `1 07 05 010` Motor Vehicles), plus `1 04 xx` semi-expendables and even `2 01 02 040`
   Loans Payable. **Prefix-derived classification no longer works** — expense class must become
   explicit data on the account record. This is almost certainly one of the "new configs" PPDO
   mentioned.

The list also contains duplicate-disambiguation keys like `5 02 03 990 (4)` (same account used
on multiple lines) — an Excel workaround the app won't need, but the importer/report must handle.

## 4. Business rules visible in the formulas

1. **Total Appropriation = Reserved + Net Appropriation** (`O = P + Q`).
2. **Net Appropriation = Q1+Q2+Q3+Q4** (`Q = SUM(R:U)`) — the quarterly release plan must sum
   exactly to the net, by construction.
3. **Amount to be Released = SUM(quarters)** — always equals Net Appropriation in this design
   (V duplicates Q; §8 Q2).
4. **Reserve rule**: max 10% "of Operational Expenses", and the `Accounts with Reserve` sheet
   scopes it to 42 specific **MOOE** accounts. However the sample puts `10,000` Reserved on
   `5 01 01 010` (Salaries — PS, not in the list), which contradicts the sheet (§8 Q4).
5. **Totals cascade**: line → expense-class sub-total → activity grand total → (project grand
   total) → program grand total → per-fund summary → grand total.
6. **One document = one office × one fund source** (three fund blocks in the sample file). The
   summary totals are **per fund**, never across funds.

## 5. What changes vs. the current WFP module (v1.2)

| Aspect | Current (v1.1/v1.2) | New form |
|---|---|---|
| Document scope | `wfp_records` unique on `(aip_record_id, office_id, division_id)` — per office **per division** | Per office **per fund source**; divisions appear nowhere on the form (⚠ §8 Q1 — biggest open question) |
| Fund source | Not a WFP dimension (fund colors only used in the report) | First-class: one WFP block per fund |
| Function grouping | None | CORE / STRATEGIC / SUPPORT function bands above programs — **new classification** with no home in the AIP hierarchy today (§8 Q5) |
| Activity descriptive fields | None on WFP (AIP holds expected outputs etc.) | Six new activity-level fields: Resources Needed, Responsible Person/Unit, Success Indicator, Means of Verification, Outcome Indicator, Target Beneficiaries |
| Expenditure line shape | PS/MOOE/CO sections with amounts | Adds per-line **Nature of Implementation** + **Account Code → Object of Expenditure** lookup + **Reserved / Net / Total appropriation** split |
| Quarterly breakdown | `wfp_activities` quarterly? (per-activity modal, PS/MOOE/CO sections) | Quarterly **release plan per expenditure line** (R–U), with Net = ΣQ enforced |
| Account config | 143 accounts, class derived from `5-0x` prefix | ~318 accounts incl. `1 xx` asset codes; class must be explicit; reserve-eligible flag needed |
| Report | WFP Excel report keyed on fund-source colors | Report output **is this form** — the entry data must be able to regenerate it 1:1 |

## 6. Template defects found (why the app should own the math)

The workbook's formulas contain multiple copy-paste faults — worth listing both to avoid
faithfully reproducing them in the report export, and as evidence for generating all totals
server-side instead of trusting spreadsheet math:

- `O48 = SUM(O39:O39)` and `O52 = SUM(O43:O43)` — sub-totals referencing the **wrong activity
  block entirely** (rows from the previous program).
- `O40 = SUM(O36:O39)` and `P29 = SUM(P25:P28)` — ranges that include the **section header row**.
- Several SUB-TOTAL rows are missing Q2/Q3 formulas (`S35`, `T35` empty while `R35`, `U35` sum).
- `PROGRAM GRAND TOTAL` at row 42 (and 128 in the GAD block) has **no amount formulas at all**.
- `TOTAL - PERSONAL SERVICES CREATION` uses the **same cell refs** as `TOTAL - PERSONAL SERVICES`
  (`P18,P35` both times) — if both rows are populated, `GRAND-TOTAL = SUM` of the five rows
  **double-counts PS**. Either "Creation" refs are placeholders or the grand total is wrong (§8 Q3).
- Checksum column W exists only for the first four sub-totals of block 1, nowhere else.
- Typos/inconsistencies: `PERSONAL SREVICES`, `MAINTENANCE AND OPERATING EXPENSES` vs
  `…AND OTHER OPERATING…`, VLOOKUP range `D3:E403` vs dropdown range `D2:D337` vs actual data
  ending at row 323.

**Implication:** entry in the app should capture only leaf-level facts (lines + quarters);
every total, sub-total, and checksum is computed — the Excel export then renders values, not
formula chains (or renders correct formulas generated from one code path).

## 7. Early UI/UX observations (to develop AFTER the requirements discussion)

Deliberately brief — flow design happens once requirements land; these are constraints the
form itself already implies:

- **The document grain drives navigation.** If a WFP = office × fund source, the natural flow is:
  pick office (auto for office users) → see one card/tab per fund source → enter each fund's plan.
  Tabs per fund mirror the workbook's three stacked blocks better than one giant page.
- **The PPA tree should come pre-populated from the Final AIP** (program/project/activity + ref
  codes read-only), with WFP-specific entry limited to: the six descriptive fields per activity,
  and an expenditure-line grid (nature/account/reserved/quarters) under each activity. Users
  should never type a ref code.
- **The line grid is the workhorse.** A per-activity table with: account picker (searchable,
  showing code + object title), nature dropdown, Reserved, Q1–Q4, computed Net/Total per row and
  live sub-totals per expense class. Validation inline: quarters must sum to Net (automatic if Net
  is computed), reserve only on eligible accounts (once Q4 is answered), reserve ≤ 10% cap.
- **Existing patterns to reuse**: `MoneyInput`, `Lookup` combobox (v1.2), the WFP finalize/unlock
  status flow, `{ data, error, message }` envelope, flat design.
- **Report = same data, print layout.** Because the entry form and report share the structure,
  a read-only "form view" that matches the workbook layout (and exports to `.xlsx`) can double as
  the review screen before Finalize.

## 8. Consolidated questions for PPDO (settle in the requirements discussion)

1. **Divisions.** The new form has no division anywhere. Does the v1.2 per-division WFP scoping
   (`wfp_records` unique on office+division, staff see own division only) survive the rework —
   e.g. divisions contribute lines that roll up into one office×fund document — or is WFP now
   office-level only? This decides whether `division_id` stays on the WFP tables.
2. **Net Appropriation vs Amount to be Released.** Both are ΣQ1–Q4 in every row — they can never
   differ as built. Is "Amount to be Released" meant to track something else later (e.g. actual
   allotment releases during the year), or is it intentional duplication for the printed form?
3. **"Creation" totals.** What do `TOTAL - PERSONAL SERVICES CREATION` and `TOTAL - MOOE -
   CREATION` mean — newly created plantilla positions / new items? What marks a line or
   program as "Creation"? (In the sample the PS-Creation formula just repeats the PS refs, which
   double-counts in the grand total — see §6.)
4. **Reserve scope.** `Accounts with Reserve` lists only MOOE accounts, yet the sample reserves
   10,000 on Salaries (PS). Which is right: reserve restricted to the 42 listed MOOE accounts, or
   allowed on any line? And is 10% a hard cap the app should enforce (10% of what exactly —
   the line's total, the fund block's operational total)?
5. **Function bands.** Where do CORE/STRATEGIC/SUPPORT FUNCTIONS come from — a fixed property of
   each AIP program chosen at WFP time, a per-office config, or free assignment per document?
   The AIP has no such classification today.
6. **Office list.** VALIDATION has 19 offices including `1011-A` / `1011-2` / `1011-3` PGO
   variants; our config has 16. Are those PGO sub-offices real WFP-filing units we must add to
   Config → Offices (and if so, do they need their own `office_ref_code`s)?
7. **Fund sources per office.** Does every office file all applicable funds in ONE submission
   (the workbook stacks three), or is each office×fund an independently draftable/finalizable
   document? Affects the status workflow grain.
8. **Project-level expenditure lines.** The Strategic Functions sample attaches lines directly to
   a project with no activity. Allowed generally, or an artifact? (Same shape as the parked
   RAL-108 AIP question — ideally both get one consistent answer.)
9. **AIP linkage.** Must every WFP program/project/activity exist in the Final AIP for that FY
   (current model links `wfp_records.aip_record_id`), or can WFP introduce PPAs that aren't in
   the AIP? What happens when the AIP is amended after WFPs are drafted?
10. **Units.** Sample amounts look like plain pesos (not ₱000 like AIP/LDIP). Confirm WFP is
    entered and reported in pesos.
11. **Account config source.** Can PPDO supply the full ~318-account list (with expense class and
    reserve-eligibility flags) as a CSV for the config upload, replacing/extending the current
    143-account chart? Who maintains it going forward?
12. **Signatures.** The workbook has no Prepared/Reviewed/Approved block. Does the printed WFP
    need one (names/positions per office), and should the app store those names?
13. **Existing WFP data.** v1.1/v1.2 `wfp_records`/`wfp_activities`/`wfp_expenditure_lines` hold
    entered data. Migrate, archive read-only, or discard when the rework lands?

## 9. Likely data-model direction (sketch, pending answers)

Rough shape only — to be turned into a real schema in the requirements doc:

- **Config (new/changed):**
  - `accounts`: replace prefix-derived typing with explicit `expense_class` (PS/MOOE/CO), add
    `is_reserve_eligible`; import the expanded NGAS chart (space- or dash-canonical form, TBD).
  - `nature of implementation`: 3-value enum (`Procurement`/`Non-procurement`/`Combined`) —
    enum in code unless PPDO wants it configurable.
  - Function bands (CORE/STRATEGIC/SUPPORT): enum vs. config, pending Q5.
- **WFP (reworked):**
  - `wfp_records`: grain likely `(fiscal_year, office_id, funding_source_id)` (+ `division_id`
    pending Q1); Draft/Final/Archived unchanged.
  - WFP PPA nodes referencing/copying AIP hierarchy nodes + `function_band` + the six
    activity-level descriptive fields.
  - `wfp_expenditure_lines`: `wfp_node_id`, `account_id` (+ code/title snapshot),
    `nature_of_implementation`, `reserved_amount`, `q1..q4`; `net`/`total` computed.
- **Report:** one generator producing the workbook layout per office (three-ish fund blocks),
  totals computed server-side (§6).

---

*Prepared by Claude Code, 2026-07-07 — from WFP-NEW.xlsx only; supersede with the PPDO
requirements discussion outcomes.*
