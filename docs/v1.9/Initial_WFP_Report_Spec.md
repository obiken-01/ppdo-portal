---
status: accepted — 2026-10-15
version: v1.9.0
tickets: PPDO-224 (T1 backend), PPDO-225 (T2 frontend)
supersedes: —
---

# v1.9.0 — Initial WFP report from the consolidated AIP

Demo 3 (2026-10-14): "Create initial WFP Report. This can only be enabled when the AIPs are part of
the consolidated (mark as done) … the initial WFP report can be generated in our app." Settled with
Ralph on 2026-10-15 (§2, decisions 1–4).

## 1. Goal

For FY2028 and later, PPDO can produce each office's **initial WFP**, in the province's WFP form, as
soon as PPDO has marked that office's AIP as done (Consolidated). FY2028+ has no WFP entered in
this portal (`AipFiscalYears`): the AIP is the only plan, so the initial WFP is read off it. GSO's
WFP remains the working WFP afterwards.

## 2. Decisions

### Settled (Ralph, 2026-10-15)

1. **Built from the AIP on every request, read-only.** No WFP records are created and nothing is
   copied, so the report always matches the consolidated AIP.
2. **Quarters.** An itemised line puts each item's amount in the quarter it was entered in
   (`AipProcurementItem.PeriodNo`). A line without items is **spread evenly over the activity's
   months** (Start–End), by how many of those months fall in each quarter.
3. **The WFP reserve applies**, with the same rule as the WFP: `WfpReserveRule`, 10% of the line's
   net, rounded to the centavo.
4. **Preview and Excel in the existing WFP form.** The existing WFP report page and the
   province-form export (v1.4.4, `WfpReportExcelService`) are reused, fed from the AIP.

### Follow-ups — settled (Ralph, 2026-10-15; 5–8 confirmed, 9–12 are the proposed defaults)

5. **Available per office once every one of its AIP groups is Consolidated.** Before that, the
   office is not offered, and a direct request answers 409 "{Office}'s FY {fy} AIP is not marked as
   done yet."
6. **Who: PPDO and the office itself.** PPDO (the WFP report's readers today, `canOpenWfpReport`)
   for every office; an office's own users for **their own office only**, once it is Consolidated.
   ↩️ This widens PPDO-20 ("WFP report is PPDO-only") for entered years only: a guest office still
   has no entered WFP, but it does have an AIP to read this from. Office users go through
   `OfficeScope.Resolve` (own office); division-scoped staff are forced to their own division, as
   the WFP report already does (RAL-136). Needs a `Permission_Matrix.md` row.
7. **Reserve on MOOE and CO lines only, not PS.** PS is exempt from the AIP ceiling (DECISION C) and
   is not operational spending, so a 10% reserve on salaries would overstate the plan.
8. **Amounts are the AIP's base figures**, not the +30% printed uplift (DECISION G is presentation
   for the Annex B form only).
9. **Spreading rule, exactly.** The months come from the activity's Start and End (month names).
   Missing or unreadable, or End before Start: the whole year (January–December). Each quarter
   gets `amount × (months in that quarter ÷ total months)`, rounded to the centavo, with the
   rounding remainder added to the **last** quarter that has months, so Q1–Q4 always sum to the
   line exactly.
10. **An itemised line whose items do not add up to its amount** (possible on older rows): the
    items go to their quarters, and the difference is spread by rule 9. The line total is always
    the AIP's figure.
11. **Labelled "Initial WFP (from AIP)"** on the page and in the export's title row, so nobody
    takes it for a WFP that was entered.
12. **The division filter works as on the WFP report**, by the activity's division tag (PPDO-148).
    Untagged activities appear only in the whole-office report.

## 3. Behaviour

| # | Given | When | Then |
|---|---|---|---|
| 1 | FY2028, PTO's groups all Consolidated | PPDO opens the WFP report, picks PTO | Preview in the WFP form, titled "Initial WFP (from AIP)", grouped by fund as the WFP report is |
| 2 | Same | Export | `.xlsx` in the province WFP form, same title |
| 3 | An activity Jan–Jun with a ₱120,000 MOOE line, no items | Preview | Q1 60,000, Q2 60,000, Q3 0, Q4 0; reserve 12,000 |
| 4 | An activity Mar–May, ₱100,000 | Preview | Q1 33,333.33, Q2 66,666.67 (remainder to Q2) |
| 5 | An itemised line, items in Q2 and Q4 | Preview | Each item's amount in its quarter |
| 6 | A PS line | Preview | Spread as rule 9, no reserve |
| 7 | Activity with no Start/End | Preview | Spread over all four quarters |
| 8 | PTO has one group still at SubmittedToPpdo | Office picker | PTO not offered; a direct request answers 409 |
| 9 | PPDO edits a PTO line after Consolidated (PPDO-212) | Preview again | New figures (decision 1) |
| 10 | FY2027 | WFP report | Unchanged: the entered WFP, as today |
| 11 | A PTO user, PTO Consolidated | Opens the report | Offered, PTO only; another office's id answers 404 |
| 12 | A PTO user, PTO not yet Consolidated | Opens the report | The type is offered but empty: "Your FY {fy} AIP is not marked as done yet." |
| 13 | A FY2027 guest-office user | Report page | No WFP type for FY ≤ 2027, as today |

## 4. API contract

The existing WFP report endpoints, unchanged in shape; the source switches on the fiscal year
(`AipFiscalYears`):

- `GET budget-planning/wfp/report/offices?fiscalYear=` — for an entered year, the offices whose
  groups are all Consolidated (status shown as "Consolidated").
- `GET budget-planning/wfp/report/preview?officeId=&fiscalYear=&divisionId=` — for an entered year,
  `WfpReportDto` built from the AIP, plus a new `source: "Wfp" | "Aip"` field. `409` when not
  Consolidated.
- `GET budget-planning/wfp/report/export?…` — the same, as `.xlsx`.

## 5. Data model

None. No migration.

## 6. UI states

- WFP report page, entered year: the office picker lists Consolidated offices only. Empty:
  "No office's FY {fy} AIP is marked as done yet."
- Title "Initial WFP (from AIP)" and a note under it: "Read from {Office}'s consolidated AIP. Q1–Q4
  for lines without items are spread over the activity's months."
- Loading, error and the division filter as the WFP report today.

## 7. Non-goals

- No WFP records, no editing, no copy into GSO's system.
- No change to the FY ≤ 2027 WFP report.
- No PPMP from the AIP (a separate question if they want it).
- Guest offices get no FY ≤ 2027 WFP report (unchanged).

## 8. Deployment notes

No migration. Frontend and backend deploy together.

## 9. Ticket split

| Ticket | Scope | Model | Depends on |
|---|---|---|---|
| T1 Backend | AIP → `WfpReportDto` builder (quarters, reserve, fund grouping, division filter); Consolidated gate; eligible-office list; `source`; export reuse; the office-user read rule (decision 6) + Permission_Matrix row; tests for rules 2, 3, 6, 7, 9, 10 | Opus (money mapping, scope) | — |
| T2 Frontend | Report page for entered years: picker, title, note, empty states; offer the type to guest-office users for entered years | Sonnet | T1 |

## 10. Acceptance checklist

- [ ] FY2028: only offices whose AIP is fully Consolidated are offered.
- [ ] PTO's initial WFP preview matches its AIP total per fund (base figures).
- [ ] A Jan–Jun line splits evenly over Q1–Q2; an itemised line follows its items' quarters.
- [ ] Reserve is 10% on MOOE/CO lines and absent on PS.
- [ ] The Excel export opens in the province WFP form with the "Initial WFP (from AIP)" title.
- [ ] FY2027 WFP report is unchanged.
- [ ] As a PTO user: PTO's initial WFP opens once PTO is Consolidated; another office's id answers 404.

## 11. Test focus

- Rule 9: uneven months, remainder placement, missing/unreadable months, End before Start.
- Rule 10: items not adding up to the line.
- The gate: one group not Consolidated hides the office.
- Totals: the report's per-fund total equals the AIP's base total for the office.
