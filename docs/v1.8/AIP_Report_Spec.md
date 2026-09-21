---
status: draft
version: v1.8.0
tickets: R1 = PPDO-90 (endpoint scope), R2 = PPDO-92 (Report page AIP type; blocked by 90)
supersedes: AIP_Review_Spec.md §6.3a "Opened from" and the Consolidated AIP sidebar item (§6.4, PPDO-84) — grid, figures and export content are unchanged
---

# v1.8.0 — Consolidated AIP moves into the Report page

## 1. Goal

The Annex B consolidated AIP (PPDO-73 grid, PPDO-84 export) is a standalone page reachable only by
the PPDO cross-office reviewer. At the 2026-09-15 PDC demo it was decided that it belongs on the
**Report** page as a report type — beside WFP and PPMP, with the same sector tabs and Export button —
and that a **department head (office reviewer) can open it for their own office** to see their office's
work as it will print. The standalone page and its sidebar item are removed.

## 2. Decisions (settled)

1. **New report type "Annual Investment Program (AIP — Annex B)"** on `/budget-planning/report`. The grid,
   sector tabs, printed figures, activity modal and Export are the existing PPDO-73/84 ones, moved — not
   rebuilt. — *The server builder (`AipFormRowBuilder`) is already the single source for grid and file.*
2. **Two scopes, one type.**
   - **Consolidated** — every office at `SubmittedToPpdo` or `Consolidated`. Unchanged from today.
     Cross-office reviewer only.
   - **One office** — a single office's rows and TOTAL.
   A cross-office reviewer picks either from the Office select ("— all submitted offices (consolidated)
   —", the default, or one office). A department head's Office select is **locked to their own office**.
   — *Mirrors the WFP report's "— all divisions (consolidated) —" pattern on the same page.*
3. **Which workflow states each scope shows.**
   - Cross-office reviewer, consolidated or one office: **with PPDO only** (`SubmittedToPpdo`,
     `Consolidated`) — unchanged; PPDO does not read an office's work before it is sent.
   - Department head, own office: **any state.** — *The point is seeing their own work as it will print
     before sending it on.* ✅ **Confirmed by Ralph 2026-09-16** (was assumed from the meeting note "so
     they can see their own office's work").
4. **A department head is pinned to `users.office_id`, never resolved through `OfficeScope.Resolve`.** A
   department head **in the host office (PPDO)** resolves to `SeeAll` there, which would hand them the
   whole province. The pin is explicit and tested for a PPDO department head. — *Same trap as tracker B4.*
5. **A requested `officeId` outside the caller's scope is clamped, not refused.** A department head's
   `officeId` is ignored in favour of their own. — *No existence oracle, matching the read paths.*
6. **Routes keep their names.** `…/aip/consolidated` and `…/aip/consolidated/export` gain an optional
   `officeId`. — *Renaming routes buys nothing and breaks the external bookmarks and tests.*
7. **Type options are filtered per user.** WFP and PPMP stay host-office only (PPDO-20); AIP is offered to
   either reviewer. A guest-office department head sees **only** AIP; a PPDO division encoder with no
   review flag sees **only** WFP and PPMP. Default = first allowed type.
8. **The Report sidebar item shows for anyone with at least one allowed type** (today: host office only).
9. **`/budget-planning/aip/consolidated` redirects** to `/budget-planning/report?type=AIP&fiscalYear=&sector=`
   carrying its params, then the page file is deleted. The AIP Review search header link points at the
   report. — *Old links keep working for a release.*
10. **FY2028+ only** for the AIP type (the fiscal-year select lists the entered years), as today.

### Open follow-ups (not blocking)

- Decision 3's department-head "any state" — confirm.
- Whether the Excel for one office should print "N of M submitted" at all (today's preamble counts
  offices). Default in §4: it prints the office name instead.

## 3. Behaviour

| Case | Given | When | Then |
|---|---|---|---|
| Happy path — consolidated | Cross-office reviewer, FY2028, 3 of 25 offices with PPDO | Type AIP, office "all submitted", Generate | Today's consolidated grid: sector tabs with "n of m", 3 offices, TOTAL |
| Happy path — one office (PPDO) | Cross-office reviewer | Picks GSO (SubmittedToPpdo), Generate | Only GSO's rows and a TOTAL equal to GSO's subtotal |
| Happy path — department head | Guest-office dept head, own office in Draft | Opens Report | Type is AIP (only option), office locked to theirs, Generate shows their rows |
| Export — consolidated | Cross-office reviewer, consolidated preview | Export to Excel | Today's four-sheet workbook, unchanged |
| Export — one office | Dept head preview | Export to Excel | Four-sheet workbook with only their office; sheets with none of their groups carry the empty preamble; filename includes office code |
| Edge: office not with PPDO | Cross-office reviewer picks an office in DepartmentReview | Generate | Empty state "\<Office\> has not submitted to PPDO yet" — no rows, Export disabled |
| Edge: office in one sector | Dept head whose groups are all GENERAL | Clicks Social tab | "Your office has no programs in Social Services" |
| Edge: FY not opened | Any | Generate | "FY \<year\> has not been opened yet"; Export disabled |
| Edge: legacy link | Anyone | Opens `/budget-planning/aip/consolidated?fiscalYear=2028&sector=SOCIAL` | Redirected to the report with type AIP, FY2028, Social tab |
| Failure: dept head asks for another office | Dept head | `GET …/consolidated?officeId=<other>` | Their own office's sheet (clamped), 200 |
| Failure: no flag | Staff with neither reviewer flag | `GET …/consolidated` | 403 |
| Failure: export error | — | Export fails | Inline error under the button; preview stays |
| Role: PPDO division encoder | Host office, no review flag | Opens Report | Types WFP and PPMP only; no AIP option; AIP call → 403 |
| Role: PPDO department head | Host office, `CanReviewBudgetPlanning` only | AIP type | Locked to PPDO's own office — **never** another office (decision 4) |
| Role: user with both flags | Cross-office + dept head | AIP type | Cross-office behaviour (the wider grant wins) |
| Role: SuperAdmin | — | AIP type | Cross-office behaviour |

## 4. API contract

### `GET /api/budget-planning/aip/consolidated?fiscalYear=&sector=&officeId=` — JWT + (`CanReviewAllOffices` OR `CanReviewBudgetPlanning`)

- `officeId` optional.
- Scope resolution (service, not handler):
  - `CanReviewAllOffices` → `officeId` null = consolidated (with-PPDO offices); set = that office, with-PPDO only.
  - else `CanReviewBudgetPlanning` → office = `caller.OfficeId` (ignore the param); **any state**. Null office → 200 empty sheet, `Opened` as per record.
  - else → 403.
- 200: `ApiResponse<AipConsolidatedSheetDto>`, gaining:
  ```
  scope: "Consolidated" | "Office",
  office: { officeId, officeCode, officeName, workflowStatus } | null   // null for Consolidated
  ```
  For `Office`, `submittedOffices`/`totalOffices` and the sector counts describe that one office (1/1 or
  0/1 by the scope's state rule), so the header copy stays correct.
- 400: `fiscalYear is required.` / bad sector (existing).
- 403: `{ error: "Only an AIP reviewer can open this report." }`.

### `GET /api/budget-planning/aip/consolidated/export?fiscalYear=&officeId=` — same gate and scope rules

- 200: the `.xlsx`. One-office workbook: same four sheets filtered to that office; preamble shows the
  office name in place of "N of M offices"; filename `AIP{FY}_{OfficeCode}_{yyyyMMdd}.xlsx`.
  ↩️ **As built the stamp is the DATE, not `yyyyMMddHHmmss`** (PPDO-90). The workbook carries `AsOf`, a
  `DateOnly` — the "As of MONTH YEAR" line the form itself prints — and threading a second, finer clock
  through it only to make two same-day downloads differ buys nothing: every browser already suffixes a
  repeated file name. The date is what the document is dated by.
- 400 FY≤2027 / 404 unopened (existing). 403 as above.

Log the existing export `LogInformation` with an added `Scope` and `OfficeId`.

## 5. Data model changes

**None.** No migration.

## 6. UI states

### Report page — AIP type (`/budget-planning/report?type=AIP`)

| State | Content |
|---|---|
| **Selector row** | Report Type (filtered, decision 7) · Fiscal Year (FY2028+) · Office (cross-office: "all submitted (consolidated)" + offices; dept head: disabled, own office) · no Division select · Generate Preview · Export to Excel (after a preview) |
| **Before generate** | Existing copy: "Select … and click Generate Preview to view the report." |
| **Loading** | Grid-shaped skeleton (today's consolidated skeleton), selector row stays |
| **Success** | Header "FY … · N of M offices submitted to PPDO" (consolidated) or "\<Office\> · \<status pill\>" (one office); sector tabs; grid; printed-figures note under it; activity click opens `AipActivityReviewModal` |
| **Empty sector** | Consolidated: "No \<Sector\> office has submitted to PPDO yet". One office: "\<Office\> has no programs in \<Sector\>" |
| **Office not with PPDO** (cross-office, one office) | "\<Office\> has not submitted to PPDO yet." Export disabled with that reason |
| **Not opened** | "FY \<year\> has not been opened yet" |
| **Error** | Inline error with *Try again*; export errors inline under the button |
| **Forbidden** | Type not offered; a hand-typed `?type=AIP` without a reviewer flag falls back to the first allowed type |
| **Print** | Existing report print CSS applies; tabs and selector hidden |

**Components.** Extract from `aip/consolidated/page.tsx` into `components/aip/report/AipAnnexBReport.tsx`
(tabs, header, grid, note, modal wiring) and render it from the report page. Reused: `AipActivityReviewModal`,
`aipHeaderRow`, `fmtThousands`, `useToast`, `ConfigPageHeader`. New access rule in
`lib/budget-planning-access.ts`: `canOpenAipReport(me) = canAccessBudgetPlanning && (canReviewAllOffices ||
canReviewBudgetPlanning)`, and `canOpenWfpReport(me)` for the existing host-office rule, so Sidebar and page
guard read one function each.

## 7. Non-goals

- **Any change to the grid, printed figures, uplift, rounding or Excel layout** (`AIP_Form_Spec.md`).
- **WFP and PPMP report behaviour.**
- **A division filter for AIP** — the Annex B form has no division axis.
- **FY≤2027 AIP report.**
- **Renaming the API routes** (decision 6).
- **"Budget Planning" → "Investment Planning" rename** — separate label ticket.
- **The new Project fields' report** — separate spec once the fields are known.

## 8. Deployment notes

- Migrations: **none.**
- Dependencies / config: none.
- Keep the `aip/consolidated` redirect for at least one release after v1.8.0 ships.

## 9. Ticket split

| Ticket | Scope | Blocked by |
|---|---|---|
| **R1** — Report scope on consolidated endpoints | §4: widened gate, scope resolution incl. the host-office dept-head pin, `officeId`, DTO additions, one-office export + filename. TDD. Permission change → sign-off before merge | — |
| **R2** — AIP report type on the Report page | Extract `AipAnnexBReport`; type filtering; Office select scopes; access rules; Sidebar (Report visibility, remove Consolidated AIP item); redirect + delete old page; AIP Review link | R1 |

Manual-coding candidate: **R2's redirect stub + removing the Consolidated AIP sidebar item** (tiny, reversible,
sibling redirects exist). R1 is not — it is a permission gate.

## 10. Acceptance checklist

- [ ] The sidebar has no "Consolidated AIP" item for anyone
- [ ] Opening `/budget-planning/aip/consolidated?fiscalYear=2028&sector=SOCIAL` lands on the Report page with AIP, FY2028 and the Social tab selected
- [ ] As the cross-office reviewer, AIP with "all submitted offices" shows the same grid, counts and TOTAL the old page showed, and Export downloads the same workbook
- [ ] As the cross-office reviewer, picking one office that has submitted shows only that office, and TOTAL equals its subtotal
- [ ] As the cross-office reviewer, picking an office still in DepartmentReview shows "has not submitted to PPDO yet" and Export is disabled
- [ ] A guest-office department head sees Report in the sidebar, sees only the AIP type, and the Office select is locked to their office
- [ ] That department head sees their office's rows while the office is still in Draft
- [ ] That department head's Export downloads a workbook containing only their office, named with their office code
- [ ] A PPDO department head (host office, `CanReviewBudgetPlanning` only) sees PPDO's rows only, never another office's
- [ ] A PPDO division encoder with no review flag sees WFP and PPMP but no AIP type
- [ ] Clicking an activity in the AIP report opens the read-only activity modal
- [ ] Printing the AIP report hides the selector row and tabs

## 11. Test focus

- `AipConsolidatedServiceTests` — dept head gets own office at every workflow state; dept head's `officeId`
  param is ignored; **host-office dept head is pinned to the host office id, not every office**; cross-office
  consolidated unchanged; cross-office one office filters to that office and still excludes non-PPDO states;
  neither flag → Forbidden; both flags → cross-office; one-office export contains only that office's rows,
  filename carries the code.
- `AipConsolidatedFunctionsTests` — gate admits either flag, 403 for neither, `officeId` parsed and passed.
- `PermissionMatrixTests` — no new flag; add the row for the widened read in `Permission_Matrix.md` §4
  (department head: own office, any state, pinned) so the matrix stays complete.
