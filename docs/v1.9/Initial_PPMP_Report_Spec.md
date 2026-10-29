---
type: spec
version: v1.9.2
status: current
tickets: [PPDO-263]
updated: 2026-10-29
status_note: "draft — decisions 6–9 await Ralph"
tickets_note: "PPDO-263; split into T1 backend and T2 frontend (§9)"
supersedes_note: "—"
---

# v1.9.2 — Initial PPMP report from the consolidated AIP

Up: [v1.9 hub](README.md)

Ralph, 2026-10-29: bring the PPMP report back for PPDO users and let it use the AIP as its data
source, the way the initial WFP does. PPDO-265 already restored the v1.5 PPMP (from WFP procurement
items) for FY 2027 and earlier. This spec covers FY 2028 and later.

## 1. Goal

For FY 2028 and later, PPDO can produce each office's **initial PPMP**, in the province's PPMP form,
once PPDO has marked that office's AIP as done (Consolidated). FY 2028+ has no WFP entered in this
portal, so there are no WFP procurement items to read. The AIP's own procurement items take their
place. The report is **indicative**: the working PPMP is prepared in GSO's PBO-Connect app.

## 2. Decisions

### Settled (Ralph, 2026-10-29, on the ticket)

1. **Title "Initial PPMP (from AIP)"**, matching "Initial WFP (from AIP)". No rename to "Indicative"
   before the November training.
2. **The note** says what "Initial" leaves out, in the same words as the WFP (PPDO-264):
   "**Indicative only.** The figures are read from {Office}'s consolidated AIP. The working PPMP is
   prepared in GSO's PBO-Connect app."
3. **AIP source for FY 2028+ only** (`AipFiscalYears.IsEntered`). FY 2027 and earlier keep the v1.5
   PPMP from WFP procurement items (PPDO-265, shipped).

### Proposed (need Ralph's yes)

4. **Built from the AIP on every request, read-only.** Same as the initial WFP: nothing is stored or
   copied, so a PPDO edit after Consolidated shows at once.
5. **Offered once every AIP group of the office is Consolidated.** The same gate as the initial
   WFP. Before that the office is not offered, and a direct request answers 409 "{Office}'s FY {fy}
   AIP is not marked as done yet."
6. **Item-level, from the AIP's procurement items.** ↩️ This changes the ticket's proposal 3 (one
   row per account, quantity blank). The AIP already holds what a PPMP row needs: each
   `AipProcurementItem` has a name, unit, unit price, quantity, number of days, its quarter
   (`PeriodNo`, 1–4) and, when picked from the Price Index, the stock card. So the report is the
   v1.5 PPMP shape, fed from the AIP:
   - one row per item, grouped by item identity (Price Index id, name, unit, unit price) within
     its account, as v1.5 does;
   - each item's quantity and amount go into its own quarter. No spreading over months is needed:
     an AIP item always has a quarter;
   - Stock Card No. and Category come live from the Price Index; free-typed items leave them blank.
   This also answers the ticket's ⚠️ under question 5: for FY 2028 the colleague who prepares
   PPDO's PPMP still gets a list of procurement items per activity, taken from the AIP.
7. **Procurement only.** An AIP line with no items is left out, as v1.5 leaves out
   non-procurement expenditures. Totals are the sum of the items, never the AIP line's amount. A
   line whose items cover only part of its amount shows just the items.
8. **PPDO only.** Readers are the same as for the restored PPMP: `CanAccessWfp`, the host-office
   rule from PPDO-265. Guest-office department heads do **not** get it, unlike the initial WFP.
   The working PPMP is GSO's, and the ticket asks for PPDO users only.
9. **No new endpoint.** The two PPMP endpoints switch their source on the fiscal year, as the WFP
   report's did (PPDO-224). `PpmpReportDto` gains `source: "Wfp" | "Aip"`. This keeps v1.9.2's
   "no new endpoints" rule intact.

### Also proposed (defaults from the ticket)

10. **Grouped by fund source**, one table and one Excel sheet per fund, from the line's
    `FundingSourceNameSnapshot` (blank = GENERAL FUND), as the initial WFP does.
11. **Division filter** works by the activity's division tag (PPDO-148), as on the initial WFP.
    Untagged activities show only in the whole-office report.
12. **Amounts are the AIP's base figures**: no +30% printed uplift and no WFP reserve. A PPMP has
    no reserve column.

### Open follow-ups (not blocking)

- **Bulk export** of every office's initial PPMP, like the initial WFP's ZIP job. Not in this
  version.
- **Showing the unitemised part.** Decision 7 drops AIP lines without items, so a fund's PPMP total
  can be lower than its AIP total. A footnote such as "₱X of this fund's AIP has no procurement
  items" could make that visible. Not in this version unless Ralph wants it.

## 3. Behaviour

| # | Given | When | Then |
|---|---|---|---|
| 1 | FY 2028, PTO's groups all Consolidated; a PPDO user with WFP access | Report page, type PPMP, picks PTO | Preview in the PPMP form, titled "Initial PPMP (from AIP)", with the note, one table per fund |
| 2 | Same | Export | `.xlsx` in the province PPMP form, one sheet per fund, title row and note line as in §6 |
| 3 | A line with 10 reams in Q1 and 5 in Q3 at ₱250 | Preview | One row: qty 15, est. ₱3,750; Q1 10 / ₱2,500, Q3 5 / ₱1,250 |
| 4 | The same Price Index item on two lines of one account | Preview | One row, quantities and amounts added |
| 5 | An item with qty 2, ₱1,000, 3 days | Preview | Qty 2, est. ₱6,000 (days multiply into the amount, as v1.5) |
| 6 | A free-typed item | Preview | Listed, Stock Card No. and Category blank |
| 7 | A line with no items | Preview | Not listed (decision 7) |
| 8 | A line of ₱10,000 whose items total ₱6,000 | Preview | The ₱6,000 of items only |
| 9 | An activity with lines but none itemised | Preview | The activity does not appear |
| 10 | An office whose AIP has no items at all | Preview | Empty state: "{Office}'s FY {fy} AIP has no procurement items." |
| 11 | PTO has one group still at SubmittedToPpdo | Office picker | PTO not offered; a direct request answers 409 |
| 12 | PPDO edits a PTO item after Consolidated | Preview again | New figures (decision 4) |
| 13 | Division filter set to Division A | Preview | Only activities tagged Division A |
| 14 | FY 2027 | PPMP | Unchanged: the v1.5 PPMP from WFP items (PPDO-265) |
| 15 | SuperAdmin | Any office | Allowed (holds `CanAccessWfp`) |
| 16 | PPDO Admin or PPDO Staff with WFP access | Any office | Allowed; division-scoped Staff are forced to their division (RAL-136) |
| 17 | PPDO Staff without WFP access | Report page | No PPMP type; API answers 403 |
| 18 | A guest-office head (Admin, or `CanReviewBudgetPlanning`) | Report page | No PPMP type; API answers 403, even for their own office (decision 8) |
| 19 | A guest-office encoder | Report page | No Report page at all (PPDO-240, unchanged) |

## 4. API contract

The existing PPMP endpoints, unchanged in route and auth (JWT, then `CanAccessWfp`, else 403).
The source switches on `AipFiscalYears.IsEntered(fiscalYear)`.

- `GET budget-planning/ppmp/report/preview?officeId=&fiscalYear=&divisionId=`
  - 200: `PpmpReportDto`, plus `source: "Wfp" | "Aip"`.
  - 403: caller lacks `CanAccessWfp`.
  - 404: office not found, or no AIP for the year.
  - 409 (entered year only): "{Office}'s FY {fy} AIP is not marked as done yet."
- `GET budget-planning/ppmp/report/export?…` — the same, as `.xlsx`. Same errors.
- **Office picker:** the page keeps using `GET budget-planning/wfp/report/offices`. For an entered
  year it already lists only Consolidated offices, and a PPDO reader sees them all. No change.

## 5. Data model

None. No migration.

## 6. UI states

Report page, type PPMP, an entered year (FY 2028+):

- **Heading:** "Initial PPMP (from AIP)", with the note from decision 2 under it, as the initial WFP
  shows its own (PPDO-264). The printed header reads "INITIAL PPMP (FROM AIP) — PROJECT
  PROCUREMENT MANAGEMENT PLAN".
- **Office picker:** Consolidated offices only. Empty: "No office's FY {fy} AIP is marked as done
  yet."
- **No items:** "{Office}'s FY {fy} AIP has no procurement items."
- **Loading, error, division filter:** as the PPMP report today.
- **Forbidden:** the type is not offered to a user without `CanAccessWfp`.

Excel, entered year: the title row reads "INITIAL PPMP (FROM AIP) — PROJECT PROCUREMENT MANAGEMENT
PLAN (PPMP)". Row 6 (the free row above the column headers) carries "Indicative only. The figures
are read from {Office}'s consolidated AIP. The working PPMP is prepared in GSO's PBO-Connect app.",
on every sheet. The FY 2027 export is unchanged.

## 7. Non-goals

- No PPMP records, no editing, no copy into PBO-Connect.
- No change to the FY 2027 PPMP (PPDO-265).
- No guest-office initial PPMP, not even for department heads.
- No bulk export.
- No rows for AIP lines without items.

## 8. Deployment notes

No migration and no new settings. Frontend and backend deploy together. It reaches UAT with the next
`release/1.9.2` → `uat` merge.

## 9. Ticket split

| Ticket | Scope | Model | Depends on |
|---|---|---|---|
| T1 Backend | `PpmpReportService` builds from AIP items for an entered year (decisions 4–7, 10–12); Consolidated gate and 409; `source` on the DTO; Excel title row and note line; tests for §3 rows 3–13 and 15–18; Permission_Matrix row | Opus (money mapping, scope) | — |
| T2 Frontend | Report page: offer PPMP for entered years to PPDO readers; heading, note, empty states; Playwright spec | Sonnet | T1 |

Both are small enough for one session, one PR, if Ralph prefers.

## 10. Acceptance checklist

- [ ] FY 2028, as a PPDO user with WFP access: PPMP is offered, and only Consolidated offices are listed.
- [ ] PTO's preview is titled "Initial PPMP (from AIP)" with the note.
- [ ] Each item sits in its quarter, and the same item on two lines appears once with the sums.
- [ ] A fund's PPMP total equals the sum of that fund's AIP procurement items for the office.
- [ ] AIP lines without items are not listed.
- [ ] The Excel export matches the preview and carries the title and note on every sheet.
- [ ] A guest-office head and a PPDO Staff without WFP access get no PPMP type, and the API answers 403.
- [ ] A not-yet-Consolidated office answers 409.
- [ ] The FY 2027 PPMP is unchanged.

## 11. Test focus

- The mapping: quarter placement, grouping by identity, days multiplier, free-typed items.
- Decision 7: unitemised lines and partly itemised lines.
- The gate: one group not Consolidated hides the office and answers 409.
- Access: 403 for a guest-office head (red-test it; the initial WFP rule would let them through).
- Totals: per fund, the report equals the sum of the AIP's item `LineTotal`s for the office.

## Related

- [v1.9.0 — Initial WFP report from the consolidated AIP](Initial_WFP_Report_Spec.md), the pattern this follows
- [v1.5 PPMP report findings](../v1.5/PPMP_Report_Findings.md), the form and its rules
