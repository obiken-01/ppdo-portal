# v1.7 — Mobile UI & Inventory Rework: Findings

> Audit date: **2026-07-29**. Branch: `release/1.6.0`.
> Scope requested by Ralph: (a) make the portal mobile-friendly, focusing on the Main
> Dashboard and Inventory; (b) an Inventory pass covering optimization, a new
> warehouse-stock input page (per-item + Excel bulk), Distribution page improvements,
> and the Create PR Excel template.
>
> Some items here may be pulled forward into **v1.6.0** — see §6 for the sequencing
> recommendation. Nothing in this doc is committed to a milestone yet.

---

## 1. Headline findings

| # | Finding | Severity | Where |
|---|---|---|---|
| 1 | Portal shell is unconditionally desktop — fixed 224px sidebar, no drawer, no breakpoint. Blocks every other mobile fix. | **Blocker** | `(portal)/layout.tsx:206`, `layout/Sidebar.tsx:121` |
| 2 | Main Dashboard is a non-wrapping flex row with a hard-coded `w-60` side panel. | High | `(portal)/dashboard/page.tsx:161-177` |
| 3 | Inventory tables squash instead of scrolling — `overflow-x-auto` is present but the table is `w-full` with 8–12 columns. | High | all 7 inventory pages |
| 4 | `GeneratePRNoAsync` full-scans `purchase_requests` **on every PR creation**. | **High (write path)** | `PurchaseRequestService.cs:443-465` |
| 5 | Retired v1.2 division list still hard-coded in 3 Inventory pages — **broke PR creation and Excel import outright**. ✅ fixed in v1.6.0 | **Blocker** | see §4.1, §4.1.1 |
| 6 | `InventoryService` loads the entire item catalog + all PRs and filters in memory. | Medium | `InventoryService.cs:63, 78, 147` |
| 7 | No opening-balance / warehouse-stock concept exists anywhere in the domain. | n/a — new feature | §5 |

---

## 2. Mobile audit

Measured in-browser at **375×812** (mobile preset) against the local dev server.

### 2.1 The shell — the blocker

`frontend/src/app/(portal)/layout.tsx:206`:

```tsx
<div className="flex h-screen bg-slate-100 font-sans overflow-hidden ...">
  <Sidebar me={me} />
  ...
```

and `frontend/src/components/layout/Sidebar.tsx:121`:

```tsx
<aside className="w-56 shrink-0 bg-green-700 flex flex-col h-full print:hidden">
```

The sidebar is always rendered at a fixed `w-56` (224px) with `shrink-0`. On a 375px
viewport that leaves ~151px of usable content width — roughly 40% of the screen. There is
no hamburger, no drawer, no `hidden md:flex`, and no breakpoint anywhere in either file.

**Nothing else on this list is worth fixing until this is.** Every page-level fix below is
evaluated *assuming* the shell has been fixed and the page gets the full viewport width.

Related, in `Topbar.tsx:123`: `h-14 ... px-6` with the user name already correctly hidden
below `sm:`. The topbar is the one part of the shell that is close to mobile-ready; it just
needs to host the hamburger trigger.

**Note:** the viewport meta tag is *not* a problem — Next.js App Router injects
`width=device-width, initial-scale=1` by default, and `app/layout.tsx` does not override it.
The public login page already renders correctly at 375px with no horizontal overflow
(measured: `scrollWidth === clientWidth === 375`).

### 2.2 Main Dashboard

`frontend/src/app/(portal)/dashboard/page.tsx:161`:

```tsx
<div className="flex gap-4 flex-1 min-h-0">
  <div className="flex-1 min-w-0">      {/* calendar */}
  <div className="w-60 shrink-0">       {/* ResourceLinksWidget */}
```

No `flex-wrap`, and the Resource Links panel is `w-60 shrink-0` (240px). The calendar and
the panel cannot both fit on a phone. Needs to become a column stack below `lg:`, with
Resource Links either below the calendar or collapsed into a disclosure.

The calendar component itself is **fine** — `DashboardCalendar.tsx:168` uses `height="auto"`
and `dayMaxEvents={2}` (tuned in RAL-175), so it reflows rather than fixing a pixel height.
The event-detail modal at `dashboard/page.tsx:183` is already `max-w-sm w-full p-4`, which
works on mobile.

The dashboard page contains **zero** responsive utilities today.

### 2.3 Inventory pages

Better than expected. Every data table is already wrapped:

```tsx
<div className="overflow-x-auto overflow-y-hidden">
  <table className="w-full text-sm">
```

Because the table is `w-full` with no `min-w`, it compresses columns to illegibility rather
than triggering the horizontal scroll the wrapper is there to provide. The fix is mechanical:
add a `min-w-[…]` sized to the column count so `overflow-x-auto` actually engages.

Affected: `inventory/page.tsx:465,561`, `item-ledger:682`, `create-pr:926`, plus the
equivalent blocks in `pr-register`, `pr-report`, `receive-delivery`, `distribution`.

Secondary issues:
- Stat cards: `inventory/page.tsx:371` is `flex gap-4 flex-wrap` (wraps fine), but each
  `StatGroup`'s children at `:149` are `flex gap-3` with no wrap — the group itself overflows.
- Filter panels use `flex gap-4 flex-wrap` and mostly survive.
- Quick-action buttons at `:342` are `flex gap-2 flex-wrap` — fine.

### 2.4 Coverage

Only **45** responsive utilities (`sm:`/`md:`/`lg:`/`xl:`) exist across 14 files in the whole
`(portal)` tree. The best-covered are the Budget Planning import-preview and LDIP form pages.
The Main Dashboard has none; `item-ledger` has one.

---

## 3. Inventory optimization

`docs/Performance_Audit_2026-07-16.md` §"Tier 3 — Inventory" already scoped this and
deliberately deferred it for "a dedicated future pass". That pass is this milestone. Verified
still-present as of 2026-07-29:

### 3.1 `GeneratePRNoAsync` — the worst finding in the application

`backend/PPDO.Application/Services/PurchaseRequestService.cs:443`:

```csharp
IReadOnlyList<PurchaseRequest> allPRs = await _prs.GetAllAsync(cancellationToken);
if (allPRs.Count > 0)
{
    int maxSeq = allPRs.Select(pr => ParseSequence(pr.PRNo)) ... .Max();
    nextSeq = maxSeq + 1;
}
```

Every single PR creation loads the entire `purchase_requests` table into memory and
string-parses every `PRNo`. It is the only **write-path** finding in the whole audit. It
degrades linearly and forever, since the sequence is global (not reset per day).

Replace with a scoped repository method — a `MAX` computed in SQL, or a dedicated sequence
table. Note the current semantics carefully before changing them: the sequence is **global
and monotonic**, while the date segment changes daily, so `101-1041-GF-2026-07-29-004` can
follow `101-1041-GF-2026-07-28-003`. Preserve that unless Ralph confirms otherwise —
`CLAUDE.md` documents the format but not the reset rule.

### 3.2 `InventoryService` in-memory filtering

`InventoryService.cs:78` and `:147` both do:

```csharp
IReadOnlyList<ItemMaster> catalog = await _items.GetAllAsync(cancellationToken);
Dictionary<string, ItemMaster> catalogMap = catalog.ToDictionary(i => i.StockNo, i => i);
```

The full item catalog is loaded to resolve `Description` and `ReorderQty` for the stock
levels already returned by `_inventory.GetItemStockLevelsAsync`. Only the StockNos present in
`stockLevels` are ever read from the map. `:63` similarly falls back to `_prs.GetAllAsync()`
for admins with no division scope, then does five in-memory `Count`/`Sum` passes.

Fixes: add `IItemMasterRepository.GetByStockNosAsync(IEnumerable<string>)` for the catalog
lookup, and push the PR counts to `CountAsync`/`SumAsync` per `docs/PERFORMANCE_GUIDELINES.md`.

### 3.3 Also outstanding from the audit

- `DeliveryService.GetAllAsync` — full scan + N+1.
- No pagination on the PR, Delivery, or Items Master lists.
- Full-page CLS spinners on all 8 inventory pages (`LoadingState` / skeletons exist now —
  `components/ui/LoadingState.tsx`).
- Duplicate `/auth/me` fetch at `inventory/pr-report/page.tsx:236` — should use `useMe()`.
- Client-side N+1 at `inventory/receive-delivery/page.tsx:269-282`.

---

## 4. Distribution page

### 4.1 Retired division list — a real correctness bug ✅ FIXED in v1.6.0

> **Shipped 2026-07-29 on `release/1.6.0`.** This turned out to be the cause of "I can't
> upload a PR to inventory" — not merely a latent inconsistency. See §4.1.1 below for what
> the live data showed and what was changed.

The `Division` enum was retired in **v1.2 / RAL-97** and replaced by the configurable
`divisions` table (`backend/PPDO.Domain/Entities/Division.cs`). Three Inventory pages never
got the memo:

| File | Line |
|---|---|
| `frontend/src/app/(portal)/inventory/distribution/page.tsx` | 36 |
| `frontend/src/app/(portal)/inventory/pr-register/page.tsx` | 69 |
| `frontend/src/app/(portal)/inventory/create-pr/page.tsx` | 54 |

```tsx
const DIVISIONS = ["Admin", "Planning", "RM", "MIS", "SPD"];
```

**Consequence:** any division added, renamed, or deactivated through the Config page is
invisible to Inventory, and any of these five that no longer exists is still offered.
Distribution can therefore be recorded against a division that isn't in the table.

The replacement already exists: `listDivisions()` in `frontend/src/lib/config.ts:214`
(`GET /api/config/divisions`), which is what the user form already uses.

### 4.1.1 Why PR creation and import were completely broken

Querying the dev database settled it. The seeded PPDO divisions (`office_id = 7`) are:

| code | name |
|---|---|
| ADMIN | Administrative Division |
| PLANNING | Sectoral Planning Division |
| SMED | Statistics Monitoring and Evaluation Division |
| MIS | Information and Communications Technology Division |
| FPIP | Fiscal Planning and Investment Programming Division |
| OG-CSO | Open Governance and Civil Society Organization Engagement Division |

**Not one of the five hard-coded names — `Admin`, `Planning`, `RM`, `MIS`, `SPD` — matches
any of these.** `PurchaseRequestService.ResolveDivisionByNameAsync` matches on `Name` only,
so every submission from the Create PR form resolved to `null` and came back
`Division 'Admin' was not found.` The codes `ADMIN`/`PLANNING`/`MIS` do exist, but resolution
never looked at `Code`.

Two further defects surfaced while fixing it:

1. **Cross-office ambiguity.** `ResolveDivisionByNameAsync` ran `GetAllAsync()` then
   `FirstOrDefault` on name across **every office**. `"Administrative"` exists under both
   office 3 (PTO) and office 12 (PHO); whichever came back first won. A PPDO purchase request
   could silently be attached to another office's division.
2. **The Excel import's Staff pre-check** compared `row.DivisionName` as a raw string against
   `requester.Division.Name`, so a sheet saying `ADMIN` was rejected as "another division"
   before any resolution happened.

**What changed (all on `release/1.6.0`):**

- `frontend/src/lib/inventory-divisions.ts` — new shared, module-level-cached source of the
  active PPDO divisions, matching the `me-cache` idiom so the Inventory pages share one
  `GET /api/config/divisions` request rather than each firing their own.
- The three hard-coded arrays are gone. Create PR clamps Staff to their own division (the
  backend rejects anything else anyway); PR List and Distribution offer all PPDO divisions.
- `ResolveDivisionByNameAsync` now scopes to PPDO's own divisions via
  `IOfficeRepository.GetByCodeAsync("PPDO")` and accepts **Name or Code** (case-insensitive,
  trimmed; Name wins). Falls back to all active divisions if the PPDO office row is missing,
  so a misconfiguration degrades rather than blocking every create.
- The import's Staff pre-check resolves before comparing, and compares division **ids**.
- The "not found" error now lists the valid division names instead of a generic message.
- 6 new tests in `PurchaseRequestServiceTests` cover code-matching, case/whitespace,
  cross-office rejection, inactive divisions, and the error message contents.

**Not verified in a live browser session** — the Inventory pages sit behind login. Backend
behaviour is covered by the test suite (874 passing); the UI change needs a manual check.

### 4.2 Other Distribution improvements

FIFO batch allocation is currently done **on the frontend** (documented as a deliberate v1.1
decision in `CLAUDE.md`). It works, but it means the allocation rule is untestable in
`PPDO.Tests` and can drift from any future server-side rule. Worth revisiting in this pass —
flagged, not yet scoped, because moving it is a behavioural change that needs Ralph's call.

**Open — needs Ralph's input:** beyond the hard-coded divisions, what specifically is painful
about the Distribution page today? The flow (search item → summary → single Distribute button
→ FIFO across batches) reads cleanly in the code; I can't tell from source what's failing in
practice.

---

## 5. Warehouse stock input — new feature

### 5.1 There is no opening balance today

`ItemMaster` (`backend/PPDO.Domain/Entities/ItemMaster.cs`) is a **catalog only** — StockNo,
Description, Category, Unit, UnitCost, ItemType, ReorderQty, Remarks, IsNewItem. There is no
quantity field of any kind.

On-hand is derived entirely from movements:

```
onHand = ItemStockLevel.QtyDelivered - ItemStockLevel.QtyDistributed
```

(`InventoryService.cs:87` and `:155`, sourced from
`IInventoryRepository.GetItemStockLevelsAsync`.)

So stock that exists in the warehouse but never passed through a PR + Delivery in this system
is **invisible**. That is exactly the gap Ralph described.

### 5.2 What it needs

A new entity — snake_case per `docs/NAMING_CONVENTIONS.md`, since this is a new table:

- new table (working name `stock_balances`) keyed on StockNo, with quantity, effective date,
  optional division scope, source/reason, and audit columns
- `IStockBalanceRepository` + EF configuration + migration
- Application service + DTOs + validator
- `StockBalanceFunctions` — protected endpoints, `CanAccessInventory` gate, `ApiResponse<T>`
- `InventoryService` on-hand formula updated to `opening + delivered - distributed`
- frontend: per-item entry page + Excel bulk upload

The Excel upload path is a **straight reuse** of the existing PR import: see
`PurchaseRequestFunctions.cs:98-126` for the `CopyToAsync` → `MemoryStream` → `Position = 0`
pattern (the non-seekable-stream fix documented in `CLAUDE.md`), and
`ExcelService.ParsePRImport` at `:546` for the parse shape. `CsvUploadButton.tsx` /
`CsvDownloadButton.tsx` exist in `components/ui/` if a CSV path is ever preferred.

### 5.3 Open questions — these block ticket-writing

1. **One-time opening balance, or recurring physical count?** A single opening balance per
   item is one row and one migration. A recurring physical-count adjustment with history is a
   ledger with effective dates, variance against system on-hand, and a reconciliation view.
   These are materially different schemas — this is the single biggest unknown.
2. **Division-scoped or PPDO-wide?** Is warehouse stock held centrally, or per division?
   This decides whether `division_id` is on the table and whether `DivisionScope` applies.
3. **Retroactive or forward-only?** If an opening balance is entered today, does it apply to
   historical Stock Overview figures, or only from its effective date forward?
4. **Who can enter it?** `CanAccessInventory`, or Admin/SuperAdmin only? It directly moves
   on-hand numbers, so it may warrant a tighter gate than ordinary inventory access.
5. **Does entering a balance need an approval step**, like the calendar-event workflow?
6. **What is the bulk template's key** — StockNo only, or StockNo + division? And on
   re-upload: upsert, or reject duplicates?

---

## 6. Create PR Excel template

`ExcelService.GeneratePRTemplate` (`backend/PPDO.Infrastructure/Services/ExcelService.cs:78`)
builds a two-sheet workbook: a `PR-001` sheet from `BuildPRSheet(ws, prefilled: false, pr: null)`
plus an `Instructions` sheet. Round-trips through `ParsePRImport` at `:546`.

**Open — needs Ralph's input.** The structure is legible from source but nothing in it is
self-evidently wrong. What is failing for users in practice — column set, validation,
instructions, multi-PR support, dropdowns for divisions/units? Ticket deferred until this is
answered.

---

## 7. Sequencing recommendation

The mobile shell fix (§2.1) is **cross-cutting, not Inventory-owned**. It unblocks Budget
Planning on mobile just as much as Inventory. Under the standing priority order (Budget
Planning first, other features second, Inventory last), bundling it into an Inventory
milestone would park a Budget-Planning-affecting fix behind Inventory work.

**Recommendation:** pull the shell + dashboard tickets (RAL-A, RAL-B below) forward into
**v1.6.0**, and keep the Inventory-specific work (RAL-C through RAL-G) in v1.7.0.

Proposed split:

| Ticket | Title | Milestone |
|---|---|---|
| RAL-A | Responsive portal shell — sidebar drawer + hamburger | **v1.6.0** |
| RAL-B | Main Dashboard responsive stacking | **v1.6.0** |
| RAL-C | Inventory tables — mobile scroll + card fallback | v1.7.0 |
| RAL-D | Replace hard-coded division list in 3 Inventory pages | ✅ **DONE — shipped in v1.6.0** (see §4.1.1) |
| RAL-E | `GeneratePRNoAsync` — push sequence lookup to SQL | v1.7.0 |
| RAL-F | `InventoryService` scoped queries + list pagination | v1.7.0 |
| RAL-G | Warehouse stock input — entity, API, page, Excel bulk upload | v1.7.0 — **blocked on §5.3** |
| — | Distribution page improvements | blocked on §4.2 |
| — | Create PR template improvements | blocked on §6 |

Draft implementation prompts for RAL-A through RAL-F are in
[`Ticket_Prompts.md`](./Ticket_Prompts.md). RAL-G is not drafted — §5.3 Q1 and Q2 must be
answered first, or the schema will be wrong.

Linear IDs are placeholders (RAL-A…RAL-G) pending ticket creation.
