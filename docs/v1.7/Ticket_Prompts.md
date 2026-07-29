# v1.7 — Draft Ticket Implementation Prompts

> Drafted 2026-07-29 against `release/1.6.0`. Follows `docs/TICKET_PROMPT_STANDARD.md`
> (RAL-81 is the canonical shape). Authoritative spec for all of these is
> [`Mobile_And_Inventory_Findings.md`](./Mobile_And_Inventory_Findings.md).
>
> **Ticket IDs are placeholders** (RAL-A … RAL-F) — replace with the real Linear IDs on
> creation, including in the commit-message line at the end of each prompt.
>
> **Branch targets follow §7 of the findings doc:** RAL-A/B (and optionally D) target
> `release/1.6.0`; the rest target `release/1.7.0`, which does not exist yet — cut it from
> `main` after v1.6.0 merges, and correct the branch lines below if that plan changes.
>
> Not drafted: **RAL-G (warehouse stock input)** — blocked on findings §5.3 Q1/Q2; the schema
> depends on the answers. Distribution and Create-PR-template tickets are likewise blocked on
> §4.2 and §6.

---

## RAL-A — Responsive portal shell (sidebar drawer + hamburger)

**Milestone:** v1.6.0 · **Blocks:** RAL-B, RAL-C · **No backend changes**

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.7/Mobile_And_Inventory_Findings.md §2 FULLY — it is the authoritative spec
for this ticket.

Read these files before writing code:
- frontend/src/app/(portal)/layout.tsx (the shell: `flex h-screen ... overflow-hidden` at
  :206, plus the auth guard and the print: variants that must survive unchanged)
- frontend/src/components/layout/Sidebar.tsx (the `w-56 shrink-0` aside at :121; the
  collapsible Inventory/Config/Budget-Planning groups and their auto-expand effects; the
  user popup menu and its click-outside handler at :64-72)
- frontend/src/components/layout/Topbar.tsx (`h-14 ... px-6` header — this is where the
  hamburger trigger goes; note the user name is already `hidden sm:block`)
- frontend/src/components/ui/Modal.tsx (existing overlay/backdrop and focus conventions to
  match — do not introduce a new dialog primitive)
- frontend/tailwind.config.ts (PPDO design tokens — never hardcode colours)

Working branch: release/1.6.0.
Create feature/v1.6.0-ral-A-responsive-portal-shell off release/1.6.0 and open the PR
against release/1.6.0 (NOT main).

No service-layer logic, so no TDD step. Verify manually per the test plan below.

1. Sidebar: keep the existing markup, but make the `<aside>` a static column at `lg:` and a
   fixed off-canvas drawer below `lg:`. Below `lg:` it renders translated off-screen with a
   backdrop, above `lg:` it renders exactly as today. Preserve `print:hidden`.
2. Sidebar: accept `open` / `onClose` props. Close the drawer on route change (`usePathname`),
   on backdrop click, and on Escape. Do NOT auto-close on `lg:` — the drawer is not mounted
   as a drawer at that width.
3. Topbar: add a hamburger button visible only below `lg:`, left of the breadcrumb, wired to
   the layout's drawer state. Use an emoji glyph (☰) — the sidebar uses emoji icons, not an
   icon library (see TICKET_PROMPT_STANDARD.md "Frontend reuse").
4. layout.tsx: hold the `sidebarOpen` state and pass it down. The shell must stay
   `overflow-hidden` at `lg:` and above; below `lg:` the main content area takes the full
   width. Keep every `print:` variant on the shell intact — the WFP/PPMP reports depend on
   them (RAL-147, RAL-149).
5. Ensure touch targets in the drawer are at least 44px tall — the current
   `px-3 py-2.5` nav links are ~40px.

Manual test plan (include in the PR body):
- [ ] 375px: sidebar hidden, hamburger opens the drawer over a backdrop
- [ ] 375px: tapping a nav link navigates AND closes the drawer
- [ ] 375px: backdrop click and Escape both close the drawer
- [ ] 1280px: layout pixel-identical to before this change
- [ ] Collapsible Inventory / Config / Budget Planning groups still auto-expand on route
- [ ] User popup menu (Profile / Logout) still works in the drawer
- [ ] Print preview of a WFP report is unchanged

Do NOT change any permission gating or the `isOfficeUser` visibility rules in Sidebar.tsx.
Do NOT change the auth guard, the refresh-retry logic, or the route prefetch effect in
layout.tsx. Do NOT restyle the sidebar — this is a layout-mechanics change only.

When done, commit with:
feat(ui): responsive portal shell — off-canvas sidebar drawer below lg (RAL-A)
```

---

## RAL-B — Main Dashboard responsive stacking

**Milestone:** v1.6.0 · **Blocked by:** RAL-A · **No backend changes**

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.7/Mobile_And_Inventory_Findings.md §2.2 FULLY — it is the authoritative spec
for this ticket.

Read these files before writing code:
- frontend/src/app/(portal)/dashboard/page.tsx (the `flex gap-4` row at :161 with the
  `w-60 shrink-0` panel at :175; also the event-detail modal at :183, already mobile-safe)
- frontend/src/components/dashboard/DashboardCalendar.tsx (`height="auto"` and
  `dayMaxEvents={2}` at :168-174 — tuned in RAL-175, do not change these values)
- frontend/src/components/dashboard/ResourceLinksWidget.tsx (the widget being stacked)
- frontend/src/components/dashboard/CalendarApprovalPanel.tsx (admin panel — check it is
  usable at 375px as part of this ticket)
- frontend/src/components/dashboard/CreateEventModal.tsx (date-click modal — same check)

Working branch: release/1.6.0.
Create feature/v1.6.0-ral-B-dashboard-responsive off release/1.6.0 and open the PR against
release/1.6.0 (NOT main).

No service-layer logic, so no TDD step.

1. dashboard/page.tsx: change the `flex gap-4 flex-1 min-h-0` row to stack vertically below
   `lg:` and keep the current side-by-side layout at `lg:` and above.
2. Resource Links: drop the fixed `w-60 shrink-0` below `lg:` so it spans full width under
   the calendar. Keep `w-60` at `lg:` and above.
3. The page root is `p-5 h-full flex flex-col` — below `lg:` the column must be allowed to
   scroll rather than being clipped by `h-full`. Reduce padding to `p-3` below `sm:`.
4. Verify CreateEventModal and CalendarApprovalPanel at 375px; fix any fixed widths found.
   Per PERFORMANCE_GUIDELINES.md, loading states must not shift layout — check the
   `eventsLoading` path still reserves the calendar's height.

Manual test plan (include in the PR body):
- [ ] 375px: calendar full width, Resource Links stacked below, no horizontal scroll
- [ ] 375px: tapping a date opens CreateEventModal, fully usable
- [ ] 375px: admin pending-events chip opens CalendarApprovalPanel, fully usable
- [ ] 1280px: layout pixel-identical to before this change
- [ ] Multi-day events still render correctly (regression guard for the exclusive-end fix)

Do NOT change the events cache, the month-change fetch logic, the pending-count logic, or
the owner-only edit/delete rules (RAL-168). Do NOT change dayMaxEvents or the calendar
height mode.

When done, commit with:
feat(ui): stack Main Dashboard calendar and Resource Links below lg (RAL-B)
```

---

## RAL-C — Inventory tables: mobile scroll + readable columns

**Milestone:** v1.7.0 · **Blocked by:** RAL-A · **No backend changes**

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.7/Mobile_And_Inventory_Findings.md §2.3 FULLY — it is the authoritative spec
for this ticket.

Read these files before writing code:
- frontend/src/app/(portal)/inventory/page.tsx (two tables at :465 and :561; the StatGroup
  wrapper at :145-149 whose `flex gap-3` children do NOT wrap; stat row at :371)
- frontend/src/app/(portal)/inventory/item-ledger/page.tsx (table at :682; the one inventory
  page that already has a `md:` grid, at :585 — match its idiom)
- frontend/src/app/(portal)/inventory/pr-register/page.tsx (filter panel + table)
- frontend/src/app/(portal)/inventory/pr-report/page.tsx (report table)
- frontend/src/app/(portal)/inventory/receive-delivery/page.tsx (table + entry form)
- frontend/src/app/(portal)/inventory/distribution/page.tsx (item summary + batch table)
- frontend/src/app/(portal)/inventory/items-master/page.tsx (catalog table)
- frontend/src/app/(portal)/inventory/create-pr/page.tsx (line-items table at :926; the
  `md:grid-cols-2` form at :747 is already responsive — leave it)
- frontend/src/components/ui/DataTable.tsx (shared table primitive — prefer extending this
  over per-page fixes if the pages already use it)

Working branch: release/1.7.0.
Create feature/v1.7.0-ral-C-inventory-tables-mobile off release/1.7.0 and open the PR
against release/1.7.0 (NOT main).

No service-layer logic, so no TDD step.

1. Every inventory table is already wrapped in `overflow-x-auto overflow-y-hidden` but the
   table itself is `w-full`, so it compresses instead of scrolling. Add a `min-w-[…]` to each
   table sized to its column count, so the wrapper's horizontal scroll actually engages.
   Keep `w-full` so wide screens are unchanged.
2. Make the horizontal scroll discoverable — the WFP report's bottom-pinned scrollbar
   treatment (RAL-147) is the in-repo precedent; reuse that approach rather than inventing one.
3. inventory/page.tsx: give StatGroup's inner `flex gap-3` a `flex-wrap` (or a responsive
   grid) so stat cards reflow instead of overflowing their group.
4. Reduce page padding below `sm:` (`p-5` → `p-3`) consistently across the 8 inventory pages.
5. Filter panels: confirm each `flex gap-4 flex-wrap` panel's inputs have a sane min-width and
   do not force overflow at 375px.

Manual test plan (include in the PR body):
- [ ] each of the 8 inventory pages at 375px: page body does NOT scroll horizontally
- [ ] each table scrolls horizontally within its own container, headers legible
- [ ] 1280px: all 8 pages pixel-identical to before this change
- [ ] stat cards on the Inventory Dashboard wrap rather than overflow

Do NOT change any table's column set, sorting, filtering, or data fetching. Do NOT convert
tables to card layouts in this ticket — scroll-and-legible first; a card fallback can be a
follow-up if Ralph wants one after seeing this.

When done, commit with:
fix(ui): make inventory tables scroll and stat cards wrap on mobile (RAL-C)
```

---

## RAL-D — Replace the retired hard-coded division list in Inventory

**Milestone:** v1.7.0 (candidate for v1.6.0 — small, and it is a correctness bug)
**No backend changes** — the endpoint already exists

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.7/Mobile_And_Inventory_Findings.md §4.1 FULLY — it is the authoritative spec
for this ticket.
Read docs/v1.2/ (the RAL-97 divisions/permissions rework) for why the enum was retired.

Read these files before writing code:
- frontend/src/app/(portal)/inventory/distribution/page.tsx:36 (hard-coded DIVISIONS)
- frontend/src/app/(portal)/inventory/pr-register/page.tsx:69 (hard-coded DIVISIONS)
- frontend/src/app/(portal)/inventory/create-pr/page.tsx:54 (hard-coded DIVISIONS)
- frontend/src/lib/config.ts:214 (listDivisions() — GET /api/config/divisions, the
  replacement source; note it returns DivisionResponse[], and takes a query filter)
- frontend/src/types/config.ts (DivisionResponse shape — id, officeId, code, name, isActive)
- backend/PPDO.Domain/Entities/Division.cs (int Id, office-scoped, IsActive soft delete —
  the pages currently send a division *name string*, so check what the API expects)
- frontend/src/components/ui/OfficeSelect.tsx and Lookup.tsx (existing shared pickers —
  reuse rather than writing a new dropdown)
- frontend/src/lib/me-cache.ts (useMe — division scope for Staff users)

Working branch: release/1.7.0.
Create feature/v1.7.0-ral-D-inventory-divisions-from-config off release/1.7.0 and open the
PR against release/1.7.0 (NOT main).

1. FIRST, establish the contract: the three pages currently submit a division NAME string
   from the retired enum. Check what the Distribution / PR create / PR filter endpoints
   actually bind — a name string or a division id. Do not guess; read the DTOs and the
   Functions handlers. If they take a name, decide with Ralph whether this ticket also
   migrates them to division_id (that would make it a backend ticket too).
2. Replace all three hard-coded arrays with `listDivisions()`, filtered to active divisions.
3. Per PERFORMANCE_GUIDELINES.md "fetch shared state once": do NOT fetch the division list
   separately in each component. Load it once and share it, the way /auth/me is shared via
   me-cache. Config data is a caching candidate — see docs/PERFORMANCE_GUIDELINES.md §8.
4. Staff/Observer division scoping must still apply — a Staff user must not be offered
   divisions outside their scope. Verify against DivisionScope semantics on the backend.
5. Handle the empty/loading state so the dropdown does not cause layout shift.

Manual test plan (include in the PR body):
- [ ] a division added in Config appears in all three inventory dropdowns
- [ ] a division deactivated in Config disappears from all three
- [ ] existing records referencing an old division name still display correctly
- [ ] Staff user sees only their own division; Admin sees all
- [ ] no duplicate /config/divisions requests on any page load (check Network tab)

Do NOT reintroduce the Division enum or PermissionGroup (retired in v1.2 / RAL-97 —
explicitly forbidden in CLAUDE.md). Do NOT change division permission-flag resolution.

When done, commit with:
fix(inventory): source division lists from the divisions config table (RAL-D)
```

---

## RAL-E — `GeneratePRNoAsync`: push the sequence lookup to SQL

**Milestone:** v1.7.0 · Highest-value backend fix in the milestone

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.7/Mobile_And_Inventory_Findings.md §3.1 FULLY — it is the authoritative spec
for this ticket.
Read docs/PERFORMANCE_GUIDELINES.md FULLY — this ticket is a direct application of its
"query at the database, not in memory" rule.
Read docs/Performance_Audit_2026-07-16.md §"Tier 3 — Inventory" for the original finding.

Read these files before writing code:
- backend/PPDO.Application/Services/PurchaseRequestService.cs:443-475
  (GeneratePRNoAsync + ParseSequence — the full-scan being replaced; note the call site
  at :153 and that a caller-supplied PRNo bypasses generation)
- backend/PPDO.Domain/Interfaces/IPurchaseRequestRepository.cs (where the new scoped
  method goes — GetByDivisionAsync at :37 is the template for a scoped query)
- backend/PPDO.Infrastructure/Repositories/PurchaseRequestRepository.cs (implementation)
- backend/PPDO.Tests/Application/PurchaseRequestServiceTests.cs (extend this)
- CLAUDE.md "Key Business Logic" — PR No. format 101-1041-GF-YYYY-MM-DD-XXX, 3-digit
  zero-padded sequence

Working branch: release/1.7.0.
Create feature/v1.7.0-ral-E-pr-number-sequence-sql off release/1.7.0 and open the PR
against release/1.7.0 (NOT main).

TDD: extend backend/PPDO.Tests/Application/PurchaseRequestServiceTests.cs with failing
tests first, then implement.

1. PRESERVE THE EXISTING SEMANTICS EXACTLY. Today the sequence is GLOBAL and MONOTONIC —
   it is the max sequence across ALL PRs ever, not per-day, so 101-1041-GF-2026-07-29-004
   legitimately follows 101-1041-GF-2026-07-28-003. CLAUDE.md documents the format but not
   the reset rule. Do NOT "fix" this to a per-day reset without Ralph confirming — write a
   test that pins the current behaviour first.
2. Add IPurchaseRequestRepository.GetMaxPrSequenceAsync(CancellationToken) returning int.
   Implement it as a single SQL aggregate — do not materialise rows. The sequence is the
   last '-'-delimited segment of PRNo; a computed MAX over a SQL-side substring is
   acceptable, or add a persisted sequence column if that proves cleaner. Note the current
   ParseSequence tolerates malformed PRNos by skipping them (returns null) — preserve that
   tolerance so legacy/imported rows cannot break creation.
3. Replace the GetAllAsync call in GeneratePRNoAsync with the new method. Delete
   ParseSequence only if nothing else uses it.
4. Consider the concurrency hole this leaves: two simultaneous creates can read the same
   max and generate the same PRNo. Check whether purchase_requests.PRNo has a unique index;
   if not, note it in the PR body as a follow-up rather than expanding this ticket.

Test cases to cover: empty table → 001; existing max 007 → 008; malformed PRNo rows are
skipped, not fatal; a caller-supplied PRNo still bypasses generation entirely.

Do NOT change the PR No. format. Do NOT change the Manila-timezone date segment handling
(DateTime.UtcNow converted to UTC+8 — never DateTime.Now). Do NOT touch the delivery or
issue ref generators in this ticket.

When done, commit with:
perf(inventory): compute next PR sequence in SQL instead of scanning all PRs (RAL-E)
```

---

## RAL-F — `InventoryService` scoped queries + list pagination

**Milestone:** v1.7.0

```
Read CLAUDE.md, PROJECT_DOCUMENTATION_NET_AZURE.md, and PPDO_PROJECT_CONTEXT.md.
Read docs/v1.7/Mobile_And_Inventory_Findings.md §3.2 and §3.3 FULLY — authoritative spec.
Read docs/PERFORMANCE_GUIDELINES.md FULLY.
Read docs/Performance_Audit_2026-07-16.md §"Tier 3 — Inventory".

Read these files before writing code:
- backend/PPDO.Application/Services/InventoryService.cs (GetStatsAsync at :47 — the
  GetAllAsync fallback at :63 and the full-catalog load at :78; GetItemLedgerAsync at :114
  — the same catalog load at :147)
- backend/PPDO.Domain/Interfaces/IInventoryRepository.cs (GetItemStockLevelsAsync,
  GetStockNosDeliveredInRangeAsync — the aggregate queries that already do this correctly;
  match their shape)
- backend/PPDO.Domain/Interfaces/IItemMasterRepository.cs (add the batched lookup here;
  GetByStockNoAsync at :18 is the single-key template)
- backend/PPDO.Domain/Interfaces/IPurchaseRequestRepository.cs (GetByDivisionAsync at :37)
- backend/PPDO.Infrastructure/Repositories/ (the matching implementations)
- backend/PPDO.Application/Common/DivisionScope.cs (SeeNothing / DivisionId semantics —
  the null-division "office user sees nothing" guard must survive untouched)
- backend/PPDO.Tests/Application/InventoryServiceTests.cs (extend this)

Working branch: release/1.7.0.
Create feature/v1.7.0-ral-F-inventory-scoped-queries off release/1.7.0 and open the PR
against release/1.7.0 (NOT main).

TDD: extend backend/PPDO.Tests/Application/InventoryServiceTests.cs with failing tests
first, then implement.

1. Add IItemMasterRepository.GetByStockNosAsync(IReadOnlyCollection<string> stockNos, ct)
   — a single IN-list query. Replace both `_items.GetAllAsync()` + `ToDictionary` blocks
   (:78 and :147) with it, keyed on the StockNos already present in `stockLevels`. Only
   those keys are ever read from the map today, so this is behaviour-preserving.
   RAL-158 (fix/v1.4.3-ral-158-wfp-report-nplus1-queries) is the in-repo precedent for
   batching N+1 lookups into IN-list queries — follow that shape.
2. GetStatsAsync :61-71: the admin path calls `_prs.GetAllAsync()` then does four in-memory
   Count passes and a Sum. Replace with scoped repository aggregates (CountAsync per status,
   SumAsync for TotalAmount). Await them SEQUENTIALLY — never Task.WhenAll over one
   DbContext (this caused a prod 500 in GetStatsAsync; see CLAUDE.md).
3. Add server-side pagination (Skip/Take + total count) to the PR list, Delivery list, and
   Items Master list endpoints, per PERFORMANCE_GUIDELINES.md. Return slim DTOs — only the
   columns each grid renders. Update the corresponding frontend pages to page through.
4. Frontend cleanups in the same pass:
   - inventory/pr-report/page.tsx:236 — replace the separate /auth/me fetch with useMe()
   - inventory/receive-delivery/page.tsx:269-282 — batch the client-side N+1
   - replace the full-page spinners on the 8 inventory pages with layout-preserving
     skeletons (components/ui/LoadingState.tsx exists)

If this proves too large for one PR, split at step 3 — steps 1-2 are the backend query fix,
steps 3-4 are pagination + frontend. Say so in the PR rather than half-doing both.

Test cases to cover: admin (null division) vs Staff (scoped) stats are unchanged from
before the refactor; office user with no division still gets EmptyStats (SeeNothing);
item-ledger date-range filter still restricts correctly; stock levels for a StockNo absent
from the catalog still fall back to StockNo-as-name and ReorderQty 0.

Do NOT change the on-hand formula (QtyDelivered - QtyDistributed) in this ticket — RAL-G
changes it to include opening balances and would conflict. Do NOT change the low-stock
LogWarning business events. Do NOT change DivisionScope semantics.

When done, commit with:
perf(inventory): scope InventoryService queries and paginate inventory lists (RAL-F)
```

---

## Not yet draftable

| Ticket | Blocked on |
|---|---|
| **RAL-G** — warehouse stock input (entity, API, per-item page, Excel bulk upload) | Findings §5.3 — especially Q1 (one-time opening balance vs. recurring physical count) and Q2 (division-scoped vs. PPDO-wide). The schema and migration depend on both. |
| Distribution page improvements | Findings §4.2 — what is actually painful about the current flow, and whether FIFO allocation moves server-side. |
| Create PR Excel template | Findings §6 — what is failing for users in the current template. |
