---
type: audit
version: v1.9.2
status: current
tickets: [PPDO-283]
supersedes: []
updated: 2026-11-03
---

# v1.9.2 — Negative test inventory: every form that saves data

Up: [v1.9 hub](README.md)

Step one of PPDO-283. Each saving form, field by field: what the page checks, what the API checks, and where the two do not match. It was written **before** any test, so the gaps show first.

How this was made:

- By **reading the code** on `release/1.9.2` (2026-11-03). Nothing was run. A row marked "suspected" needs a test to confirm it.
- "Page" means the browser checks it. "API" means the service, a validator, or the database column.
- Line numbers are as of this commit. They will drift.

## Summary

- **25 forms audited** field by field: 7 AIP, 5 other Budget Planning, 4 Inventory, 9 Admin / Config / personal. One of them (Recovery answer, D9) was not on the ticket's list. Part E lists about a dozen more saving forms found but not audited yet.
- **86 field rows with a gap.** By type (a row can have two): 31 page-only, 18 API-only, 19 neither, 30 DB-only, 2 disagree.
- **27 suspected bugs**, 10 of them AIP. See [Suspected bugs](#suspected-bugs).
- **Leave warning:** 2 of 25 forms (AIP Entry, Investment Proposals).
- **Double submit:** every form checked disables Save while saving (AIP comments not checked yet).

## How to read the tables

**Gap** column:

| Value | Meaning |
|---|---|
| — | Page and API agree. No gap. |
| **page-only** | The page stops it, the API lets it in. A direct API call (or a page bug) gets bad data saved. Fails PPDO-283's rule "the API refuses bad input on its own". |
| **API-only** | The API refuses it, the page sends it anyway. The user sees the API's message after the round trip. Fine if the message is readable. |
| **neither** | Nothing stops it. |
| **DB-only** | Only the database column limit stops it. That is a SQL error, so a **500**, not a readable 400. |
| **disagree** | Page and API (or API and database) use different limits. |

**Error shape** column (per form): what the user sees on a 400, 409 or 500.

- The API has two error shapes. Budget Planning and Config endpoints send JSON `{ error }`. Inventory, Users, Calendar, Announcements and Resource Links send **plain text**. The 500 handler (`ExceptionHandlingMiddleware.cs:105`) always sends JSON `{ error: "An unexpected error occurred … quote reference X" }`.
- A page that reads the plain-text shape breaks on a 500, because the 500 is JSON. See the inventory forms.

**Covered by PPDO-271** marks a case that already has a Playwright spec (see `frontend/e2e/README.md` → "Bad-path specs").

## Findings that cut across every form

1. **FluentValidation is installed but almost unused.** `backend/PPDO.Application/Validators/` holds one validator (`InvestmentProposal/UpdateProposalValidator.cs`). Every other folder holds only a `.gitkeep`. All other API checks are hand-written `if` lines in the services.
2. **Text length is mostly checked only by the database.** Most services check "required" but not "too long". Text over the column limit makes a SQL truncation error, which comes back as a **500**. Exceptions that do check length: Investment Proposals, Create PR header fields, Items Master description, AIP comments, the AIP reviewer reason.
3. **Very large amounts have no limit anywhere.** `MoneyInput.tsx` blocks the minus sign and keeps two decimals, but has no upper bound. Money columns are `decimal(18,2)` (16 digits before the point). A number past that, or a quantity × price × days past it, is expected to fail in SQL (500) or lose precision in the browser (past 2^53). No form caps it.
4. **Negative amounts are blocked by the page, not the API, in most money forms.** `MoneyInput` cannot type a minus sign. The API refuses negatives only in: AIP expenditure lines and items, WFP, Delivery and Distribution quantities, Price index, Procurement presets, Stock balances.
5. **The leave-with-unsaved-changes warning exists on only two surfaces:** AIP Entry and the Investment Proposal editor (`components/ui/UnsavedChanges.tsx`). Every other form loses typed data on navigation without asking.
6. **Session expiring mid-form** is handled once for all forms by the Axios refresh interceptor in `lib/api.ts`. If the refresh also fails, the user goes to `/login` and the typed data is lost. Only AIP Entry (local drafts, PPDO-112/192) keeps it. Not a per-form finding, so it is not repeated below.
7. **Whitespace-only text.** Almost every API "required" check uses `IsNullOrWhiteSpace`, and almost every page trims. Where a field is required this is handled. Where a field is optional, whitespace is usually saved as-is (no trim) — harmless, not listed per form.

---

# Part A — AIP forms (fix first)

## A1. AIP Entry — activity details

Page: `components/aip/entry/AipActivityFields.tsx` (save at line 235). API: `AipService.UpdateActivityDetailsAsync` (`AipService.cs:1467`). Columns: `AipActivityConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Activity title | Required, trimmed (`:236`). No max; soft "may be cut off on print" counter (`AipActivityNameCounter`) | Required, trimmed (`AipService.cs:1490`). Column `nvarchar(max)` | — |
| eSRE code | Select from config list | Must be in `AipEsreCode.AllowedValues` (`:1492`) | — |
| Implementing office(s) | Office chips; proponent always added | Stored as sent, no check (`:1508`) | **page-only** (any text saves through the API) |
| Start month | Select of 12 months (`:452`) | Stored as sent, no check. Column `nvarchar(50)` | **page-only** (any text; over 50 chars is a 500) |
| End month | Select of 12 months (`:460`) | Same as start | **page-only** |
| Start before end | Not checked | Not checked | **neither** — "December → January" saves |
| Expected outputs | Free text, trimmed | Stored as sent, `nvarchar(max)` | — |
| CC adaptation | `MoneyInput`: no minus, no max | No check (`:1512`) | **page-only** (negative); **neither** (very large); CC larger than the activity total is not checked either |
| CC mitigation | Same | Same | Same |
| CC typology code | Select from Config → CC Typologies | Stored as sent, not checked against config. Column `nvarchar(50)` | **page-only** |

- **Double submit:** Save is disabled while saving (`:549`). — 
- **Leave warning:** yes (`useAipUnsavedChange`, `:311`), plus a local draft (PPDO-112).
- **Errors:** 409 → conflict panel with Overwrite / Discard (`:262`). 400 and 500 → `aipErrorMessage`, readable. ⚠️ `aipConflict` (`lib/aip.ts:87`) returns null when a 409 has no body, so that case falls to a dead-end message — the opposite of what its own comment says. Today every AIP 409 sends a body, so this is latent.
- **Duplicates:** none expected (ref codes are generated).
- **Dates:** see "start before end" above.

## A2. AIP Entry — add program / project / activity (inline name rows) and project details

Page: the "+ Add …" row is `AipAddRow` in `components/aip/entry/AipEntryPanelParts.tsx:218`. Callers: `AipProgramPanel.tsx:80` (project), `AipProjectPanel.tsx:136` (activity). Project details: `AipProjectPanel.tsx:221`. Programs come from the LDIP via `AipAddProgramsPanel.tsx`. API: `AipService.AddProjectAsync` (`:1123`), `AddActivityAsync` (`:1163`), `UpdateProjectAsync` (`:1336`), `AddProgramsWithGroupAsync` (`:2072`).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| New project / activity name | Add button disabled while blank (`AipEntryPanelParts.tsx:251`), trimmed | Required, trimmed | — |
| Project title (edit) | Required, trimmed (`AipProjectPanel.tsx:221`) | Required | — |
| Project description, objective | Trimmed, optional | `nvarchar(max)` | — |
| Programs to add (LDIP picker) | Checkbox list; already-added ones disabled; one group at a time | Checked server-side (`AddProgramsWithGroupAsync`) | — |

- **Double submit:** Add is disabled while busy (`:251`); project Save disabled while saving (`AipProjectPanel.tsx:314`).
- **Leave warning:** yes, for a typed-but-not-added name (`AipEntryPanelParts.tsx:222`).
- **Errors:** ⚠️ **the "+ Add" row has `try … finally` and no `catch`** (`AipEntryPanelParts.tsx:254`). A refused add (record submitted, division locked, 403, 500) shows **nothing**: the row stays open, no message, and the promise rejection is unhandled. Project details use `aipErrorMessage` — readable.
- **Duplicates:** ref codes are allocated by the server; duplicate *names* are allowed (by design — the LDIP decides programs).

## A3. AIP Entry — expenditure line

Page: `components/aip/entry/AipExpenditureTable.tsx` (save at `:422`, row checks at `:1102`). API: `AipExpenditureService.AddAsync` (`:100`) / `UpdateAsync`. Columns: `AipExpenditureConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Account | Required: "Pick an account." under the field, Save disabled (`:1102`, `:1177`) — **PPDO-271** | Optional. Not checked (`AipExpenditureService.cs:111`). A missing account is caught only later, at the submit gate | **page-only** |
| Funding source | Required in multi-fund mode (`:1104`) | Optional; an unknown id is refused readably (`ApplySnapshotsAsync`) | **page-only** (missing); — (unknown) |
| PS / MOOE / CO | `MoneyInput`: no minus; total must be > 0 ("Enter an amount.", `:1108`) — **PPDO-271** | Each ≥ 0 (`:582`) — **PPDO-271** red-tests the API refusal. A zero line is allowed by design | **page-only** (all-zero line); — (negative) |
| Amount vs account's expense class | Columns not allowed by the class are disabled | Refused with a readable message (`:571`) | — |
| Very large amount | No max | No max; `decimal(18,2)` | **neither** (suspected 500) |
| Reviewer reason (PPDO reviewer mode) | 1–500 chars, trimmed; Save disabled until valid (`lib/aip-reviewer-edit.ts:32`) | 1–500, trimmed (`AipReviewerEditComment.cs:28`) | — |

- **Double submit:** Save disabled while busy (`:1233`). **PPDO-271** tests one POST on double-click (PPDO-244).
- **Leave warning:** yes, plus local draft (PPDO-192).
- **Errors:** 409 → line conflict panel — **PPDO-271**. 400 / 500 → `aipErrorMessage`, readable.
- **Duplicates:** two lines with the same account and fund are allowed (by design).

## A4. AIP Entry — procurement items (itemised line)

Page: `components/aip/entry/AipProcurementItemTable.tsx`. API: `AipExpenditureService.ValidateItems` (`:595`) and the item mapping (`:680`). Columns: `AipProcurementItemConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Name | Free text, no `maxLength`, not checked for blank (`:365`) | Required (`:600`). No length check. Column `nvarchar(300)` | **API-only** (blank); **DB-only** (over 300 → 500) |
| Unit | Free text, no `maxLength`, may be blank (`:374`) | **Not checked.** `i.Unit.Trim()` (`:680`) throws on a missing unit. Column `nvarchar(50)` required | **neither** (blank unit saves); **DB-only** (over 50 → 500); null unit → 500. WFP checks unit (`WfpExpenditureService.cs:456`), AIP does not |
| Unit price | `MoneyInput` (no minus) | ≥ 0 (`:602`) | — |
| Quantity | `type=number min=0 step=1` — `min` does not stop typing `-5`; `Number(v) \|\| 0` keeps it (`:422`) | ≥ 0 (`:602`) | **API-only** (readable) |
| Number of days | `type=number min=1`; blank becomes 1 | > 0 (`:605`) | **API-only** |
| Quarter | Chosen from tabs | 1–4 (`:608`) | — |
| Line total (qty × price × days) | Shown | Not capped | **neither** (suspected 500 on overflow) |
| At least one costed item | "Cost at least one item." (`AipExpenditureTable.tsx:1107`) | Not required | **page-only** |

- Save, leave warning and error handling are the expenditure line's (A3).
- **Duplicates:** a duplicate item gets an amber warning only (`:360`) — by design.

## A5. AIP review comments (activity threads)

Page: `components/aip/entry/AipComments.tsx` (`maxLength={2000}` at `:599`, `:714`). API: `AipReviewCommentService.cs:194` (not blank, ≤ 2000).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Comment body | `maxLength=2000` | Not blank, ≤ 2000 | — |
| Reply to a reply / to a resolved thread | Not offered | Refused (`:366`–`:372`) | — |

- Not inspected further: whether Post is disabled while sending. Add to the spec.

## A6. AIP detail page — legacy inline editors (FY 2027 format)

Page: `components/aip/AipActivityRow.tsx`, `AipAddActivityRow.tsx`. API: `AipService.UpdateActivityAsync` (`:1375`), `AddActivityAsync` (`:1163`).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Name | Required (`AipAddActivityRow.tsx:57`) | Required | — |
| PS / MOOE / CO | `MoneyInput` (no minus) | **No negative check** (`AipService.cs:1442`, `:1228`) | **page-only** |
| Dates, CC, typology | Same as A1 | Same as A1 | Same as A1 |

- **Double submit:** Save disabled while saving (`AipAddActivityRow.tsx:161`). **Errors:** `aipErrorMessage`, readable. **Leave warning:** none.

## A7. New AIP record (upload / manual)

Page: `app/(portal)/budget-planning/aip/new/page.tsx`. Fiscal year is a select of four years (`:4`); the upload accepts `.xlsm` up to 20 MB (`:6`). Button disabled while uploading / creating (`:217`, `:318`). Errors use the AIP error reader. Not audited field by field — the import has its own parser checks (`IAipXlsmParser`). **Leave warning:** none.

---

# Part B — Budget Planning forms

## B1. Investment Proposals

Page: `components/proposals/editor/ProposalEditor.tsx`, `sections.tsx`, `fields.tsx`. API: `Validators/InvestmentProposal/UpdateProposalValidator.cs` — the one real validator. Errors come back keyed by field path and show under the field (`FieldErrors`).

| Field (section) | Page rule | API rule | Gap |
|---|---|---|---|
| Project location (A) | No `maxLength` | ≤ 500 | **API-only** (readable, under the field) |
| HGDG checklist (A) | Select | Must be a known checklist | — |
| HGDG score (A) | `type=number min=0 max=20 step=0.1` (`sections.tsx:132`) | 0–20, one decimal | — |
| Beneficiary indicator / male / female (A) | Counts `min=0 step=1` (`fields.tsx:110`) | Indicator required ≤ 1000 if row used; counts 0–1,000,000 | **API-only** |
| Narrative sections (B, C, …) | Rich text | ≤ 100,000 after sanitising | **API-only** |
| D benefits / costs, E logframe cells | No max | ≤ 4,000 | **API-only** |
| F target beneficiaries | Kind select, name, counts | Kind Direct/Indirect, name required ≤ 1000 | **API-only** |
| G groups / work plan | Group label, step name, timeline, OPR | Label required ≤ 300; step name required ≤ 500; timeline ≤ 200; OPR ≤ 300 | **API-only** |
| I supervisor, manager, team | No max | ≤ 200; member name required; sex M/F | **API-only** |
| K monitoring, L risks | No max | Phase from list; activity / risk required ≤ 1000 | **API-only** |
| Signatories | No max | Slots 1–4; label ≤ 100, name / position ≤ 200 | **API-only** |

- **Double submit:** Save all disabled while saving (`ProposalEditor.tsx:413`). Finalize disabled while there are unsaved edits (`:420`).
- **Leave warning:** yes (shared `UnsavedChanges`, with Save all).
- **Errors:** field-keyed 400 under each field; 409 on Finalize with open comments handled (`:294`). Reload / export failures show a fixed toast with no server text (`:313`, `:324`).
- This is the model for the other forms: every "API-only" row here still gives a readable message under the field.

## B2. LDIP entry

Page: `app/(portal)/budget-planning/ldip/LdipForm.tsx`. API: `LdipService.cs` (create `:85`, `ValidateGroups` `:365`, program edit `:205`, finalize `:270`). Columns: `LdipRecordConfiguration.cs`, `LdipOfficeConfiguration.cs`, `LdipProgramConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Office | Required (`:545`) | Required (`:353`) | — |
| Fiscal year start | `type=number min=2020 max=2055` (`:724`) | No range check | **page-only** (`min`/`max` do not stop a typed value either) |
| Fiscal year end | `min=2020 max=2060` (`:738`) | No range check | **page-only** |
| Start ≤ end | Warning text only (`:746`); Save still works | Checked only at **Finalize** (`:281`) | **neither** on draft save |
| Sector | Fixed list | Must be General / Social / Economic / Others (`:372`) | — |
| Office / sub-office (group) name | Required on add (`:496`) | Required; no duplicate group per sector (`:374`–`:377`). Column `nvarchar(500)`, length not checked | **DB-only** (over 500) |
| Program name | Required, upper-cased, `maxLength=500` (`:191`) | Required (`:214`); length not checked | — (page caps at the column) |
| Program budget (add) | Must be > 0 (`:496`) | **Not checked** | **page-only** |
| Program budget (edit) | `MoneyInput`, blank becomes 0 (`:197`) — 0 allowed | Not checked | **disagree** (add needs > 0, edit allows 0); **page-only** (negative) |
| PS / MOOE / CO, CC amounts | `MoneyInput` | Not checked | **page-only** (negative); **neither** (large) |

- **Double submit:** Save disabled while saving (`:671`, `:681`).
- **Leave warning:** none.
- **Errors:** `ldipErrorMessage` reads `{ error }` — readable.
- **Duplicates:** duplicate group under a sector refused (`:377`). Duplicate program names inside a group: not checked.

## B3. Office ceilings (and Bulk ceilings from last year)

Page: `app/(portal)/budget-planning/office-ceilings/page.tsx` (save `:145`), `BulkCeilingModal.tsx` (`:90`). API: `AllocationService.UpsertCeilingAsync` (`:110`). Column: `BudgetCeilingConfiguration.cs` (unique office × FY × fund).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Amount | `MoneyInput`; a cleared field is not saved (`isDirty`, `:69`) | **No check** | **page-only** (negative); **neither** (large) |
| Ceiling below what the office already encoded | Allowed, office shown as over | Allowed | By design (A5-b, page header `:17`) — not a gap |
| Ceiling below the division allocations | Not checked | Not checked | **neither** — confirm whether A5-b covers this too |

- **Double submit:** card shows saving state; saves one request per changed fund, not atomic (by design, comment `:142`). Bulk: Apply disabled while saving.
- **Leave warning:** none.
- **Errors:** per line, `allocationErrorMessage`, readable. Bulk shows one fixed message for any failure (`BulkCeilingModal.tsx:107`) and may leave a half-applied batch (it says so).
- **Duplicates:** upsert on the unique key — no duplicate possible.

## B4. Division allocations and program assignment

Page: `app/(portal)/budget-planning/allocation/page.tsx` (save `:895`, program assign `:1024`). API: `AllocationService.UpsertAllocationsAsync` (`:191`), `UpsertProgramAssignmentAsync` (`:355`).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Division amount | `MoneyInput` | **No per-row negative check** | **page-only** |
| Sum of divisions ≤ ceiling | Blocked when over (`:906`) | Refused (`:207`) | — |
| A ceiling must exist | — | Refused readably (`:202`) | **API-only** (readable) |
| Empty division list | Blocked (`:913`) | Refused (`:196`) | — |
| Division not in the office | — | **Silently skipped**, still 200 (noted in page comment `:930`) | **neither** — page now warns "Partly saved" |

- **Double submit:** Save disabled for that fund while saving (`savingAllocationsFundId`).
- **Leave warning:** none.
- **Errors:** toast with `allocationErrorMessage`, readable.

## B5. WFP entry

Page: `app/(portal)/budget-planning/wfp/entry/page.tsx` (save `:480`), `components/wfp/WfpFrequencyGrid.tsx`, `WfpProcurementItemTable.tsx`. API: `WfpExpenditureService.ValidateDto` (`:423`).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Account | Required: toast "Pick an account before saving." (`:488`) | Not checked | **page-only** |
| Nature | Select | Procurement / Non-Procurement / Combined (`:425`) | — |
| Frequency | Select | M / Q / B / A (`:428`) | — |
| Reserve amount | `MoneyInput` | ≥ 0 and ≤ the reserve cap (`:432`, `:218`) | — |
| Annual quarter choice | Select | 1–4 (`:435`) | — |
| Period amounts | `MoneyInput` grid | In range, no duplicates, ≥ 0 (`:440`–`:447`) | — |
| Procurement item name / unit | No `maxLength` | Required (`:454`, `:456`); length not checked. Columns 300 / 50 | **DB-only** (over-length → 500) |
| Item price / qty / days | Inputs | ≥ 0, ≥ 0, > 0 (`:458`–`:462`) | — |
| Fund ceiling | Shown | Checked (`:239`) | — |
| Large amounts | No max | No max | **neither** |

- **Double submit:** Save disabled until valid, and shows loading (`:556`).
- **Leave warning:** none.
- **Errors:** `wfpErrorMessage`, readable.

---

# Part C — Inventory forms

Inventory endpoints send **plain-text** errors. The 500 handler sends JSON. Several pages read the body as a string, so a 500 shows wrong. See each form's "Errors" line.

## C1. Items Master

Page: `app/(portal)/inventory/items-master/page.tsx` (checks `:344`, save `:370`). API: `ItemService.cs` (create `:113`, update `:179`). Column: `ItemMasterConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Stock No. | Required (`:344`), no `maxLength` | Required (`:113`); unique → 409 (`:126`). Column 50, length not checked | **DB-only** (over 50) |
| Description | Required (`:345`), no `maxLength` | Required; ≤ 300 (`:120`) | **API-only**; **disagree** (API 300, column 500) |
| Unit | Required (`:346`) | **Not checked.** Column 50 required | **page-only**; **DB-only** (over 50) |
| Category, Item type | Free text | Trimmed, blank → null. Columns 100, not checked | **DB-only** |
| Unit cost | `type=number` (`:531`) | **No check** (`:135`) | **neither** (negative saves) |
| Remarks | Free text | `nvarchar(max)` | — |

- **Double submit:** Save disabled while saving (`:610`, `:620`).
- **Leave warning:** none.
- **Errors:** ⚠️ the page reads `response.data.message` (`:386`), but this API sends plain text. So a **409 duplicate Stock No.** and a **400 description too long** both show the generic "Failed to save. Please try again." The real message is lost.
- **Duplicates:** Stock No. unique, refused (409).

## C2. Create PR

Page: `app/(portal)/inventory/create-pr/page.tsx` (header checks `:428`, items `:443`, submit `:770`). API: `PurchaseRequestService.CreateAsync` (`:265`), lengths `:825`–`:870`. Column: `PurchaseRequestConfiguration.cs`, `PRItemConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| PR No. (typed) | Free text | Unique → 409 (`:305`). Column 30, **length not checked** | **DB-only** (over 30 → 500) |
| PR date | Required (`:428`) | Not checked | **page-only** |
| Division | Required (`:429`) | Must exist (`:271`); Staff = own division (`:277`) | — |
| Department | Optional | Blank → "PPDO" (`:321`); ≤ 100 | — |
| Fund | Required (`:430`) | ≤ 100; **required not checked** | **page-only** |
| Requested by | Required (`:431`) | ≤ 100; required not checked | **page-only** |
| Position | Required (`:432`) | ≤ 100; required not checked | **page-only** |
| Approved by / position, AIP code, account no./title, SAI, ALOBS | Optional | ≤ 100 / 50 / 200 / 50 (`:830`) | **API-only** (readable) |
| Program / project / activity | Textarea | Unbounded by design (RAL-225) | — |
| At least one item | Required (`:445`) — **PPDO-271** | Required (`:286`) | — |
| Item description | Required (`:448`) | ≤ 300 (`:869`) | **API-only** (length) |
| Item unit | Required (`:449`) | Not checked. Column 50 | **page-only**; **DB-only** |
| Item quantity | > 0 (`:451`) | **Not checked** | **page-only** |
| Item unit cost | Number | **Not checked** | **neither** (negative) |

- **Double submit:** Submit disabled while submitting (`:937`).
- **Leave warning:** none (a long PR form with many rows).
- **Errors:** 400 / 409 plain text shows readably. ⚠️ A **500** is JSON, and the page does `String(response.data)` (`:787`–`:790`), so the toast reads **"[object Object]"**.
- **Duplicates:** PR No. refused (409).

## C3. Receive Delivery

Page: `app/(portal)/inventory/receive-delivery/page.tsx` (`buildPayload` `:330`, submit `:350`). API: `DeliveryService.CreateAsync` (`:185`). Column: `DeliveryConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Purchase request | Required (`:331`) | Must exist; not Completed; Staff = own division (`:190`–`:202`) | — |
| Delivery date | Required (`:332`) | **Not checked** (`:255`) | **page-only** |
| Received by | Required (`:333`) | **Not checked**; `dto.ReceivedBy.Trim()` (`:256`) throws when missing. Column 100 | **page-only**; missing → 500; **DB-only** (over 100) |
| Supplier | Optional | Column 200, not checked | **DB-only** |
| Qty this delivery | `type=number min=0`; rows with 0 skipped; at least one (`:340`) | > 0 (`:219`) | — |
| Qty more than remaining | Amber warning only (`:567`, `:604`); not blocked | **Not checked** | **neither** — over-delivery saves |

- **Double submit:** Submit disabled while submitting (`:459`, `:630`).
- **Leave warning:** none.
- **Errors:** plain-text 400 readable. ⚠️ 500 → `String(object)` → **"[object Object]"** (`:366`–`:368`).
- **Dates:** delivery date before the PR date, or in the future — not checked anywhere.

## C4. Distribution

Page: `app/(portal)/inventory/distribution/page.tsx` (form `:160`–`:235`, submit `:350`). API: `DistributionService.cs` (`:218`–`:305`). Column: `DistributionConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Qty issued | `type=number min=0.01`; over-stock warning (`:223`), Submit disabled (`:231`) | > 0 (`:227`); total ≤ available (`:300`) | — |
| Issued by | Required (via `canSubmit`) | Required (`:230`). Column 100, length not checked | **DB-only** |
| Division | Select | Required; must be an active division (`:247`) | — |
| Date issued | `type=date` | Not checked | **page-only** if the page requires it; confirm in the spec |
| Remarks | Free text | `nvarchar(max)` | — |

- **Double submit:** Submit disabled while submitting (`:231`).
- **Leave warning:** none.
- **Errors:** ⚠️ `toast.error("Failed", msg)` where `msg` is the response body (`:366`–`:368`). On a **500** that body is a JSON **object**, and the toast renders it as a React child. Suspected **blank screen / React error**, not just a bad message.

---

# Part D — Admin, Config and personal forms

## D1. Users (admin)

Page: `app/(portal)/admin/users/page.tsx` (add `:742`, edit `:822`). API: `UserService.CreateAsync` (`:70`), `UpdateAsync`. Column: `UserConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Full name | Required (`:742`) | Required (`:133`). Column 100, length not checked | **DB-only** |
| Username | Required | Required; unique, case-insensitive → 409 (`:140`). Column 50, not checked | **DB-only** |
| Email | `type="email"` (`:329`) — but saved with a button `onClick`, not a form submit, so the browser never checks it | Unique → 409 (`:147`); **format not checked**. Column 256 | **neither** (format); **DB-only** (length) |
| Role | Select | Must be SuperAdmin / Admin / Staff; office users Staff only (`:76`, `:98`) | — |
| Division | Required for Staff (disabled otherwise, `:360`) | Required for Staff (`:115`) | — |
| Position, contact no. | Free text | Columns 100 / 50, not checked | **DB-only** |

- **Double submit:** Save disabled and loading (`:1047`, `:1078`).
- **Leave warning:** none.
- **Errors:** plain text read correctly (`:760`). A 500 (JSON `{ error }`) reads `.message`, misses it, and shows the generic text — acceptable.
- **Duplicates:** username and email refused (409).

## D2. Divisions (Config)

Page: `app/(portal)/config/divisions/page.tsx` (`:316`). API: `DivisionService.cs` (create `:87`, update `:153`). Column: `DivisionConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Office | Required (`:316`) | Required | — |
| Name | Required, trimmed (`:317`) | Required; unique per office → 409 (`:102`). Column 200, length not checked | **DB-only** |
| Code | Optional, trimmed | Unique per office → 409 (`:112`). Column 20, not checked | **DB-only** |
| Landing page | Select | Must be a valid name (`:94`) | — |

- **Double submit:** buttons disabled while saving (`:586`). **Leave warning:** none. **Errors:** `configErrorMessage`, readable.

## D3. Funding Sources (Config)

Page: `app/(portal)/config/funding-sources/page.tsx` (`:281`). API: `FundingSourceService.cs` (create `:123`, update `:174`). Column: `FundingSourceConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Code | Required (`:284`) — **PPDO-271** | Required; unique → 409 (`:133`) — **PPDO-271**. Column 20, length not checked | **DB-only** |
| Name | Required — **PPDO-271** | Required. **Not unique** — **PPDO-271** `test.fail()` with **PPDO-282**. Column 100, not checked | **neither** (duplicate name, PPDO-282); **DB-only** (length) |
| Description, aliases | Trimmed | `nvarchar(max)` | — |
| Colour | Colour picker or "no colour" | Blank → null; **format not checked**. Column 7 | **page-only**; **DB-only** |
| Office (own fund) | Select | Must exist (`:137`) | — |
| Enforce AIP ceiling | Toggle | General Fund always on (`:513`) | — |

- **Double submit:** buttons disabled while saving (`:683`). **Leave warning:** none. **Errors:** `configErrorMessage`, readable.

## D4. Calendar events (create / edit / reject)

Page: `components/dashboard/CreateEventModal.tsx`, `CalendarApprovalPanel.tsx`. API: `DashboardService.cs` (create `:88`, update `:225`, reject `:186`). Column: `CalendarEventConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Title | Required (`:46`), `maxLength=200` (`:135`) | Required (`:93`); length not checked. Column 200 | — through the page; **DB-only** by direct call |
| Description | Textarea, no `maxLength` | Trimmed; column 1000, not checked | **DB-only** (over 1000 → 500) |
| Event type | Office / Personal | Must be Office / Personal (`:96`) | — |
| Start date | `type=date` | Required by the column | — |
| End date ≥ start | Checked (`:50`), `min={startDate}` (`:175`) | **Not checked** (`:108`) | **page-only** |
| Rejection reason | Required when rejecting | Required (`:186`); column 500, not checked | **DB-only** |

- **Double submit:** Save shows loading, which disables it (`Modal.PrimaryButton`).
- **Leave warning:** none.
- **Errors:** ⚠️ `catch { toast.error("Failed to save event.") }` (`:86`) — the server's message (e.g. "You can only edit your own events.") is dropped.

## D5. Announcements

Page: `components/announcements/AnnouncementEditorModal.tsx` (draft `:66`, publish `:90`). API: `AnnouncementService.cs` (`:67`, `:102`). Column: `AnnouncementConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Title | Required (`:66`, `:90`), **no `maxLength`** | Required; length not checked. Column 200 | **DB-only** — a 201-character title is a 500 |
| Content | Rich text | `nvarchar(max)` | — |
| Delete a published one | — | Refused, 409 (`:199`) | — |

- **Double submit:** all three buttons disabled while saving (`:125`–`:136`).
- **Leave warning:** none (rich-text editor in a modal; closing loses the text).
- **Errors:** `announcementErrorMessage` handles both string and JSON — readable.

## D6. Resource Links

Page: `app/(portal)/resource-links/page.tsx` (form `:95`–`:165`, add `:276`). API: `ResourceLinkService.cs` (`:67`, `:119`). Column: `ResourceLinkConfiguration.cs`.

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Title | Free text, no `maxLength` | Required; column 200, length not checked | **API-only** (blank); **DB-only** (length) |
| URL | Free text, no `type=url` | Required (`:70`); **format and scheme not checked**. Column 2048 | **neither** — `not a url` or `javascript:…` saves, and is rendered as `href={link.url}` (`page.tsx:400`, `ResourceLinksWidget.tsx:90`) |
| Category | Select / text | Required by column (100), not checked | **DB-only** |
| Link order | `type=number min=1` (`:136`) | Not checked | **page-only** |

- **Double submit:** Save disabled while saving (`:161`).
- **Leave warning:** none.
- **Errors:** plain text shown (`:283`). A 500 (JSON) falls back to "Failed to add link." — acceptable.

## D7. Account — profile

Page: `app/(portal)/account/page.tsx` (`:115`). (`/profile` is a 19-line redirect.) API: `UserService.UpdateOwnProfileAsync` (`:560`).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Full name | Required (`:115`) | Required (`:569`); column 100 not checked | **DB-only** |
| Username | Required (`:116`) | Required; unique → 409 (`:579`); column 50 not checked | **DB-only** |
| Email | `type=email` (`:169`) | Unique → 409 (`:590`); format not checked | **neither** (format) — confirm whether this is a real form submit |
| Position, contact no. | Free text | Columns 100 / 50, not checked | **DB-only** |

- **Double submit:** Save disabled while saving (`:227`).
- **Leave warning:** none.
- **Errors:** ⚠️ `setError(response.data ?? …)` (`:128`–`:131`). On a 500 the body is a JSON object, and `{error}` is rendered as a React child → suspected React error.

## D8. Account — change password

Page: `components/account/ChangePasswordForm.tsx`. API: `UserService.ChangePasswordAsync` (`:627`).

| Field | Page rule | API rule | Gap |
|---|---|---|---|
| Current password | Required (`:28`) | Must match (`:637`) | **API-only** (readable) |
| New password | ≥ 8, an upper-case letter, a digit (`:32`–`:34`) | Same three (`:642`–`:646`) | — |
| Confirm | Must match (`:35`) | Must match | — |
| Very long password | No max | No max; BCrypt uses only the first 72 bytes | **neither** — low risk, note only |

- **Double submit:** disabled while saving (`:114`). **Leave warning:** none.
- **Errors:** ⚠️ same as D7 — `setError(response.data)` (`:52`–`:55`) renders an object on a 500.

## D9. Account — recovery answer *(not in the ticket's list; found while auditing)*

Page: `components/account/RecoveryAnswerForm.tsx`. Question required, answer and confirm required and must match (`:81`–`:86`), Save disabled while saving (`:159`). API not audited in depth. Add to the spec list.

---

# Part E — Other saving forms found, not audited field by field

These save data but are not in PPDO-283's list. Each needs a row set before its spec is written.

- **Config → Offices** (`config/offices`) — `OfficeService.cs:73`: code and name required, code unique (409), landing page checked. Code 20 / name 200 lengths not checked.
- **Config → Accounts** (`config/accounts`), **CC typologies**, **eSRE codes**, **Investment planning settings**, **API access (partner keys)**, **Office users**.
- **Config → Price index** and **Procurement presets** — both refuse negative prices / quantities (`PriceIndexService.cs:560`, `ProcurementPresetService.cs:195`).
- **Inventory → Stock balances (warehouse count)** — `StockBalanceService.cs:136`, counted qty ≥ 0 (`:605`), duplicate → 409.
- **Uploads:** AIP `.xlsm`, LDIP `.xlsm`, GSO PR import, every Config CSV import, stock-balance import.
- **Comments:** Investment Proposal comments; calendar approval (reject reason, D4).

---

# Suspected bugs

From reading the code only. Each needs a test to confirm. Ralph decides which become tickets.

## AIP (fix first)

1. **"+ Add project / activity" shows nothing when the API refuses.** `try … finally`, no `catch`. — `components/aip/entry/AipEntryPanelParts.tsx:254` (callers `AipProgramPanel.tsx:80`, `AipProjectPanel.tsx:136`)
2. **A procurement item with no unit crashes the save (500); a blank unit saves.** `i.Unit.Trim()` with no check; WFP has the check, AIP does not. — `backend/PPDO.Application/Services/AipExpenditureService.cs:680`, `:595`–`:613`
3. **Procurement item name over 300 or unit over 50 characters is a 500.** No `maxLength` on the page, no length check in the API. — `AipProcurementItemTable.tsx:365`, `:374`; `AipProcurementItemConfiguration.cs:39`, `:44`
4. **The API saves an expenditure line with no account.** The page blocks it; the API does not, so the gap shows only at the submit gate. — `AipExpenditureService.cs:111` vs `AipExpenditureTable.tsx:1102`
5. **Activity start / end months: any text saves through the API, and the order is never checked.** "December → January" saves; text over 50 characters is a 500. — `AipService.cs:1509`–`:1510`; `AipActivityFields.tsx:452`–`:465`
6. **CC adaptation / mitigation: negative values accepted by the API, and CC larger than the activity total is not checked.** — `AipService.cs:1512`–`:1513`
7. **CC typology code is not checked against Config → CC Typologies by the API.** — `AipService.cs:1514`
8. **Legacy (FY 2027) activity write accepts negative PS / MOOE / CO.** — `AipService.cs:1442`, `:1228`
9. **No upper limit on any AIP amount.** Past `decimal(18,2)` or 2^53 the save is expected to fail with a 500 or lose precision. — `components/ui/MoneyInput.tsx` (no max); `AipExpenditureConfiguration.cs:51`
10. **`aipConflict` treats a 409 with no body as "not a conflict"**, against its own comment. Latent today. — `frontend/src/lib/aip.ts:87`–`:90`

## Other features

11. **Distribution: a 500 renders a JSON object inside a toast** → suspected React error / blank screen. — `app/(portal)/inventory/distribution/page.tsx:366`–`:368`
12. **Account profile and Change password: a 500 renders a JSON object as the error text** → suspected React error. — `app/(portal)/account/page.tsx:128`–`:131`; `components/account/ChangePasswordForm.tsx:52`–`:55`
13. **Create PR and Receive Delivery: a 500 shows "[object Object]".** — `create-pr/page.tsx:787`–`:790`; `receive-delivery/page.tsx:366`–`:368`
14. **Items Master hides every API message** (reads `.message` from a plain-text body). A duplicate Stock No. shows "Failed to save." — `items-master/page.tsx:386`
15. **Resource Links accept any URL, including `javascript:`, and render it as a link.** — `ResourceLinkService.cs:70`; `resource-links/page.tsx:400`; `ResourceLinksWidget.tsx:90`
16. **Receive Delivery saves more than was ordered.** The page shows an amber warning only; the API has no check. — `receive-delivery/page.tsx:567`; `DeliveryService.cs:216`–`:229`
17. **Receive Delivery with no "Received by" is a 500 from the API.** — `DeliveryService.cs:256`
18. **Announcement title over 200 characters is a 500.** No `maxLength`, no API check. — `AnnouncementEditorModal.tsx`; `AnnouncementConfiguration.cs:19`
19. **User email format is checked nowhere** (`type=email` outside a form submit). — `admin/users/page.tsx:329`; `UserService.cs:143`
20. **Create PR: the API accepts zero / negative quantity, negative unit cost, missing Fund / Requested by / Position, and a typed PR No. over 30 characters (500).** — `PurchaseRequestService.cs:265`–`:310`, `:1007`
21. **Items Master: negative unit cost saves; missing unit is a 500.** — `ItemService.cs:113`–`:135`
22. **Ceilings and division allocations: negative amounts accepted by the API.** — `AllocationService.cs:110`, `:191`
23. **LDIP: start year after end year saves as a draft** (warning only; API checks at Finalize). — `LdipForm.tsx:746`; `LdipService.cs:281`
24. **LDIP program budget: add needs > 0, edit allows 0 or negative; the API checks neither.** — `LdipForm.tsx:197`, `:496`; `LdipService.cs:230`
25. **Calendar: the API saves an end date before the start date**, and the modal drops the server's error text. — `DashboardService.cs:108`; `CreateEventModal.tsx:86`
26. **WFP: the API saves an expenditure with no account** (page-only check). — `wfp/entry/page.tsx:488`; `WfpExpenditureService.cs:423`
27. **Over-length text is a 500 across Users, Divisions, Funding Sources, Items Master, Calendar description, Resource Links** (only the column limit stops it). See each form's DB-only rows.

---

# Proposed spec order

AIP first, as decided. Inside each group, the forms with the most suspects go first.

1. **AIP procurement items** (A4) — suspects 2, 3, 9
2. **AIP "+ Add" rows and project details** (A2) — suspect 1
3. **AIP activity details** (A1) — suspects 5, 6, 7, 10
4. **AIP expenditure line** (A3) — suspect 4; extends the PPDO-271 spec
5. **AIP legacy detail editors** (A6) — suspect 8
6. **AIP comments** (A5) and **reviewer reason** (A3)
7. **Investment Proposals** (B1) — API-only rows; confirm messages land under the field
8. **LDIP entry** (B2) — suspects 23, 24
9. **Office ceilings + division allocations** (B3, B4) — suspect 22
10. **WFP entry** (B5) — suspect 26
11. **Distribution** (C4) — suspect 11 (possible blank screen, so first in inventory)
12. **Create PR** (C2) — suspects 13, 20
13. **Receive Delivery** (C3) — suspects 13, 16, 17
14. **Items Master** (C1) — suspects 14, 21
15. **Account profile + change password** (D7, D8) — suspect 12
16. **Resource Links** (D6) — suspect 15
17. **Users** (D1) — suspect 19
18. **Announcements** (D5) — suspect 18
19. **Calendar events** (D4) — suspect 25
20. **Funding Sources** (D3) — extends PPDO-271
21. **Divisions** (D2), **Recovery answer** (D9), then Part E

**Cross-cutting tests worth writing once, not per form:** a 500 on each inventory / account page (forced with an over-length field) checks suspects 11–13 together; a "very large amount" test on one `MoneyInput` checks finding 3.
