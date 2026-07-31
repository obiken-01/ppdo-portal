# GSO PR export → Create PR import — findings

Answers the open question left in `Mobile_And_Inventory_Findings.md` §6 ("what is failing for
users in practice"). Ralph supplied two real files exported from the external GSO system for the
same already-approved PR (`101-1041-GF-2026-04-28-757`): the printed/signed PDF and an `.xlsx`
export. Both inspected 2026-07-31.

## The PDF

The official signed hard copy — dept/section/fund/PR No./SAI/ALOBS header, the item table with
Program/Project/Activity/Account rows stacked above the line items, Purpose, and the three
signature blocks (Requested By / Cash Availability / Approved By), plus a QR code and a
"NOT A TRUE COPY" watermark. It contains the requester's and approver's names, which the xlsx
does not. **Not a parse target** — it's a rendered layout, not structured data, and Ralph confirmed
it's reference-only; no attachment/upload feature needed for it.

## The xlsx — a different export than our own template

Nothing like `ExcelService.GeneratePRTemplate`'s output. Single sheet named `PR` (ours: `PR-001` +
`Instructions`), flat label/value pairs at fixed rows, item rows immediately below with no fixed
gap. Confirmed by direct `openpyxl` read (not guessed from the PDF):

| Row | Col A | Col B |
|---|---|---|
| 1 | `PR Number` | `101-1041-GF-2026-04-28-757` |
| 2 | `Office (Short)` | `PPDO` |
| 3 | `Office (Full)` | `Provincial Planning and Development Office` |
| 4 | `Fund` | `General Fund` |
| 5 | `Fiscal Year` | `2026` |
| 6 | `Quarter` | `Q2` |
| 7 | `Submitted Date` | `Apr 28, 2026` |
| 8 | `Purpose` | *(blank in this sample — don't assume it's ever populated)* |
| 10 | `Item No.` \| `Stock No.` \| `Description` \| `Unit` \| `Qty` \| `Unit Cost` \| `Total Cost` | *(item table header)* |
| 11 | | `1000-000-1-01-010-001 - PLANNING MONITORING AND EVALUATION PROGRAM` *(Program, col C only)* |
| 12 | | `1000-000-1-01-010-001-002 - Administrative Support Services` *(Project, col C only)* |
| 13 | | `1000-000-1-01-010-001-002-008 - Safety measures for contagious/ communicable diseases` *(Activity, col C only)* |
| 14 | | `5 02 03 990` *(Account No., col C only, no code prefix)* |
| 15+ | `1`, `2`, … | real item rows: Stock No./Description/Unit/Qty/Unit Cost/Total Cost all populated |
| last | `TOTAL` | grand total |

No bold/italic/fill distinguishes the hierarchy rows (11–14) from item rows — confirmed by reading
`cell.font` on every populated cell, all identical. **The only reliable signal**: item rows have a
positive integer in column A (Item No.); hierarchy/account rows have column A blank and only
column C populated. Among the hierarchy rows, the ones matching `"<code> - <name>"` are
Program/Project/Activity in that top-to-bottom order; a line with no `" - "` (just digits and
spaces) is the Account No. AIP Code = the leading code segment of the Activity line.

## Field coverage

| CreatePRDto field | In the xlsx? | Source |
|---|---|---|
| PRNo | ✅ exact | B1 |
| Fund | ✅ exact | B4 |
| PRDate | ✅ | B7 "Submitted Date" (2 days off the PDF's printed `Date.` field in this sample — use B7, it matches the PR No.'s embedded date) |
| Program / Project / Activity | ✅ | rows 11/12/13, split on first `" - "` |
| AIPCode | ✅ derived | Activity line's code prefix |
| AccountNo | ✅ | row 14, raw |
| AccountTitle | ❌ | not in the file anywhere — resolve via `GET /api/config/accounts?search=<AccountNo>`, exact match on `AccountNumber`. That endpoint (`AccountService.GetAllAsync`) already does `Contains` on both `AccountNumber` and `AccountTitle`, open to any authenticated user — currently unused by Inventory, only Budget Planning uses it today. |
| Items (StockNo/Description/Unit/Qty/UnitCost) | ✅ | rows 15+ |
| Purpose | ⚠️ sometimes | B8, empty in this sample |
| Division | ❌ | the file's "Office (Full)" is PPDO itself, not a PPDO-internal division — there is no way to derive this, ever. Stays a required manual field. |
| RequestedBy, Position, ApprovedBy, ApprovingPosition | ❌ | only in the PDF's signature block, not the xlsx at all |
| SAINo, ALOBSNo | ❌ | blank in both files (filled in later in the PR lifecycle) |

## Decisions (confirmed with Ralph 2026-07-31)

1. **Prefill, not bulk direct-create.** The existing `POST /api/purchase-requests/import` path
   hard-validates Division and RequestedBy up front (`ExcelService.ParsePRImport`) — this format
   can never supply either, so it can't go through that path. Build a separate **preview/prefill**
   endpoint that parses and returns data with no DB writes (beyond the read-only account/item
   lookups); the frontend drops the result into the existing Create PR form state, same as if
   typed by hand, and normal required-field validation on Submit is unchanged.
2. **One PR per file, always.** Confirmed — the GSO site exports a single `PR` sheet per file. No
   multi-sheet looping needed (unlike our own template, which supports several PRs per workbook).
3. **No PDF upload/attachment feature.** Reference-only; out of scope entirely.
4. **Account No ↔ Account Title lookup goes into manual Create PR entry too**, not just this import
   path — today they're two disconnected free-text inputs on the form; wire both to
   `GET /api/config/accounts?search=` (same bidirectional pattern as the existing Stock No lookup).
