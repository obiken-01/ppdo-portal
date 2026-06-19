# WFP Layout Specification — RAL-68 (Page) + RAL-79 (Report)

Source file: `PPDO_WFP 2026_FinalVersion_PPDORevwd_PBORevwd_PTORevwd.xlsx`
Reference sheet: `PPDO_WFP_2026_3FundS` (750 rows × 58 cols, consolidated 3 fund sources)

Both the WFP activity page (RAL-68) and the Excel export report (RAL-79) follow
the layout of this sheet. Font ~40 in source because it is printed on A3 paper.

---

## 1. Page Title Block (rows 1–3)

- Row 1: "WORK AND FINANCIAL PLAN FY {year}"
- Row 2: "DEPARTMENT: {office name}"
- Row 3: blank

---

## 2. Column Layout (rows 4–5 headers, data from row 6)

| # | Col | Header | Notes |
|---|-----|--------|-------|
| 1  | A | AIP REF CODE | Hierarchy ref code; 5–8 segments |
| 2  | B | PROGRAMS, PROJECTS AND ACTIVITIES | Name/description at each hierarchy level |
| 3  | C | RESOURCES NEEDED | Text; per expenditure line |
| 4  | D | RESPONSIBLE UNIT/DIVISION | Text; per expenditure line |
| 5  | E | SUCCESS INDICATOR | Text; per expenditure line |
| 6  | F | MEANS OF VERIFICATION | Text; per expenditure line |
| 7  | G | ACCOUNT CODE | e.g. `5-01-01-010`; expenditure lines only |
| 8  | H | OBJECT OF EXPENDITURE | e.g. "Salaries and Wages - Regular" |
| 9  | I | TOTAL APPROPRIATION | Numeric; user entry |
| 10 | J | 10% RESERVE | Checkbox + auto-computed amount (10% of col I) |
| 11 | K | TOTAL APPROPRIATION (NET) | = I − J; auto-computed |
| 12 | L | TIME FRAME — 1st Quarter | Numeric; user entry |
| 13 | M | 2nd Quarter | Numeric; user entry |
| 14 | N | 3rd Quarter | Numeric; user entry |
| 15 | O | 4th Quarter | Numeric; user entry |
| 16 | P | TOTAL (quarterly sum) | = L+M+N+O; auto-computed |
| 17 | Q | SOURCE OF FUND | Dropdown per expenditure line |

### DB Mapping (`wfp_expenditure_lines` → Excel column)

```
resources_needed               → C
responsible_unit               → D
success_indicator              → E
means_of_verification          → F
account_number_snapshot        → G
account_title_snapshot         → H
total_appropriation            → I
reserve_amount                 → J  (apply_reserve flag controls whether shown)
net_appropriation              → K
q1                             → L
q2                             → M
q3                             → N
q4                             → O
quarterly_total                → P
funding_source_snapshot        → Q
```

Hierarchy fields (from AIP via `wfp_activities` → `aip_activities` → parents):

```
aip_offices.ref_code / name    → A / B  (Office row)
aip_programs.ref_code / name   → A / B  (Program row)
aip_projects.ref_code / name   → A / B  (Project row)
aip_activities.ref_code / name → A / B  (Activity row)
```

---

## 3. Row Types & Colors (from source Excel)

| Row Type | Excel Fill Hex | Visual | Bold |
|----------|---------------|--------|------|
| Office | `FFD0D0D0` | Light gray | Yes |
| Program | `FFCAEDFB` | Light blue | Yes |
| Project | `FF8ED873` | Light green | Yes |
| Activity | `FFFFFF00` | Yellow | Yes |
| PS/MOOE/CO section header | white | White | **Yes** |
| Expenditure line | white | White | No |
| Sub-total (per PS/MOOE/CO section) | `FFFFFF00` | Yellow | No |
| Fund-source subtotal (bottom) | `FFB3E5A1` | Pale green | No |
| Grand total (bottom) | `FF8ED873` | Medium green | Yes |

> ⚠️ **Activity row color by fund source — OPEN QUESTION (Q1/Q2 below)**
> The user has requested that activity row color depend on the fund source used.
> Pending clarification before implementing.

---

## 4. Full Hierarchy Row Sequence

```
Office row          (gray, bold)         A=5-seg code, B=office name
  Program row       (blue, bold)         A=6-seg code, B=program name
    Project row     (green, bold)        A=7-seg code, B=project name
      Activity row  (yellow, bold)       A=8-seg code, B=activity name; cols C–F filled
        [blank A/B from here:]
        Section header: "Personal Services"   (white, bold, col H)
          Expenditure line 1              cols G–Q filled
          Expenditure line 2
          ...
        Sub-total row                    (yellow, col I = sum of PS lines)
        Section header: "MOOE"           (white, bold, col H)
          Expenditure line
          ...
        Sub-total row                    (yellow)
        Section header: "Capital Outlay" (white, bold, col H)
          Expenditure line
          ...
        Sub-total row                    (yellow)
      Activity row (next activity)
        ...
```

Activities with no expenditure lines for a section simply omit that section header
and sub-total row.

---

## 5. Bottom Totals Section

After all hierarchy rows, append:

1. **Fund-source subtotals** (one per funding source present in the WFP)
   - Fill: `FFB3E5A1` (pale green)
   - Col B: "Total — {funding source name}"
   - Col I: sum of `total_appropriation` for all lines with that funding source
   - Cols L–O: sum of q1–q4 per funding source
2. **Grand total row**
   - Fill: `FF8ED873` (medium green), bold
   - Col B: "GRAND TOTAL"
   - Col I: sum of all fund-source subtotals
3. **Signature block** (approx 6 rows)
   - Prepared by: {name} / {title}
   - Reviewed by: {name} / {title}
   - Approved by: {name} / {title}

Signature names/titles are not in the DB — they may be configurable or hard-coded
per office. **Open question — see Q7.**

---

## 6. "Not All Activities in WFP" Rule

The WFP is scoped to **one office** per AIP record (unique on `wfp_records.aip_record_id + office_id`).
Only AIP activities that:
1. Belong to that office's programs/projects in the AIP hierarchy, AND
2. Have a `wfp_activities` record linking them to this WFP

…appear in the report. The RAL-68 UI controls inclusion/exclusion (activity toggling).
Activities not in `wfp_activities` for this WFP are silently omitted from the report.

---

## 7. Open Questions (relay to stakeholders before RAL-68 / RAL-79 implementation)

**Q1 — Activity row color per fund source:**
User wants activity row background to depend on the fund source used for that activity.
- What exact color maps to each fund source? (GF → ? , GAD → ? , LDRRMF → ?)
- The individual fund source sheets in the Excel (GF_WFP_2026_rev_07_31_25,
  GAD_WFP_2026, LDRRMF_CF_WFP_2026) may hold the answer — check their activity row colors.

**Q2 — Mixed fund sources within one activity:**
`funding_source_id` is on the expenditure LINE (not the activity). A single activity
can have GF lines AND GAD lines in the same WFP activity entry.
- If mixed: which color wins for the activity row header?
- Or: should expenditure lines be grouped by fund source within each activity block,
  with the activity row color changing per group?

**Q3 — LBP Form No. 4:**
The workbook also contains "LBP Form No. 4" (performance indicators — different structure).
- Is LBP Form No. 4 in scope for RAL-79, or only the main WFP sheet (PPDO_WFP_2026_3FundS)?

**Q4 — Cols C–F (Resources Needed, Responsible Unit, Success Indicator, MoV):**
In the source Excel these appear once per activity (same row as the activity header),
but in the DB they are stored per expenditure line.
- In practice, do all expenditure lines under one activity share the same C–F values?
- Or do they vary per line? (Affects where to display them in the report layout.)

**Q5 — Excel export page setup:**
Source Excel uses ~font size 40 for A3 print.
- Should the exported Excel preserve A3 page setup + large font for direct printing?
- Or output in a smaller/normal font for digital viewing (the file is re-formatted
  before printing)?

**Q6 — 10% Reserve column:**
- Should column J (10% RESERVE) always appear, or only when ≥1 expenditure line
  has `apply_reserve = true`?

**Q7 — Signature block names:**
- Where do the Prepared by / Reviewed by / Approved by names + titles come from?
  Are they fixed per office, stored in a config table, or manually typed?

---

## 8. Excel Export Implementation Notes

Library: **ClosedXML** (already in backend — used in `AipXlsmParser.cs`).

```csharp
// Pseudocode sketch for WFP Excel generation
var wb = new XLWorkbook();
var ws = wb.Worksheets.Add("WFP FY{year}");

// Title block (rows 1–3)
// Headers (rows 4–5) — merged cells across TIME FRAME cols
// Data rows:
foreach (office) {
    WriteOfficeRow(ws, row++, office);        // gray fill, bold
    foreach (program) {
        WriteProgramRow(ws, row++, program);  // blue fill, bold
        foreach (project) {
            WriteProjectRow(ws, row++, project); // green fill, bold
            foreach (activity) {
                WriteActivityRow(ws, row++, activity); // yellow (or fund-source color)
                WriteExpenditureLines(ws, ref row, activity.Lines);
                // PS section header → PS lines → yellow subtotal
                // MOOE section header → MOOE lines → yellow subtotal
                // CO section header → CO lines → yellow subtotal
            }
        }
    }
}
// Fund-source subtotals (pale green)
// Grand total (medium green)
// Signature block
```

Column widths (approximate, from source Excel):
- A: 20 chars (ref code)
- B: 50 chars (description — widest column)
- C–F: 25 chars each (text fields)
- G: 15 chars (account code)
- H: 35 chars (object of expenditure)
- I–K: 18 chars each (amounts)
- L–O: 15 chars each (quarterly amounts)
- P: 15 chars (total)
- Q: 20 chars (fund source)
