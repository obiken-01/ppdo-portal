---
type: audit
version: v1.9.2
status: current
tickets: [PPDO-252, PPDO-253, PPDO-254, PPDO-255, PPDO-256]
updated: 2026-10-26
status_note: "suspects from reading the code; each one needs a live check on UAT before it becomes a ticket"
date: "2026-10-26"
---

# UAT pre-sweep — suspects from reading the code

Up: [v1.9 hub](README.md) · Plan: [UAT Polish Plan](UAT_Polish_Plan.md)

> Code-read pass over every `page.tsx` on `release/1.9.2` and every HTTP-triggered Function, done
> before the manual sweep (A1–A3) so the sweep **confirms suspects instead of hunting for them**.
> Nothing here was run against UAT, so **every item is a suspect until checked live.** Each one says
> how to check it in the sweep. When one is confirmed, file it under PPDO-252 per plan §3.4 and put
> the ticket ID in the tracker. When one isn't, strike it through here with a one-line reason.

## Summary

| # | Suspect | Where | Likely severity | Check as |
|---|---|---|---|---|
| S1 | A failed load on the Inventory dashboard shows **zeros** instead of an error | `inventory/page.tsx` | 🟡 | any persona |
| S2 | Same toast-then-empty pattern on PR Report and Receive Delivery | `inventory/pr-report`, `inventory/receive-delivery` | 🟡 | any persona |
| S3 | A transient `/auth/me` failure sends you to `/login`, not `/reconnecting`, on 21 pages | Config, Inventory, Admin, Home, Announcements | 🟡 | `SA` |
| S4 | Inventory **reads** skip `CanAccessInventory` (division scope only); the PR report and all writes check it | 5 services, see below | 🟡 policy gap | `ST` in a division with inventory **off** |
| S5 | 14 pages gate on a full-screen spinner before the shell renders (CLS rule) | Config, Inventory, Admin | 🟢 | any, on Fast 4G |
| S6 | Pages with wide tables and no visible `overflow-x-auto` | WFP Entry, Report, Allocation, AIP Detail, Procurement Presets (+3 components) | 🟡 if it scrolls the page | any, at 390 px |
| S7 | One lint warning: unnecessary `STATUS_ORDER` dependency | `inventory/page.tsx:294` | 🟢 | n/a (code only) |

**Checked and clean:** every non-public endpoint authenticates (272 HTTP functions scanned). The
newer files use `ConfigHttp.AuthorizeAsync` / `AuthorizeWriteAsync` / `AuthorizeReportAsync`, and the
cleanup endpoints require `X-Dev-Cleanup-Key`. No direct `fetch(`, no native `alert`/`confirm`, no
`any`, no `console.log` in page files. `/auth/me` is always read through the shared `fetchMe()`
cache (the O13 fix held). The Budget Planning dashboard has an error state for each of its five loads.

---

## S1 — Inventory dashboard shows zeros when its load fails

`frontend/src/app/(portal)/inventory/page.tsx:240-259`. Each of the three loads (`/inventory/stats`,
`/purchase-requests`, `/inventory/ledger`) only toasts on failure. Then the stat cards render
`pr?.total ?? 0` (lines 382–437), so after the toast disappears the page shows **0 PRs, 0 items,
₱0.00**. That reads as real data, and the plan's checklist item 4 says a failed load must not look empty.

**Check:** DevTools → Network → *Block request URL* `inventory/stats` → reload. Expect an error
state with Retry. Suspect: zeros.

**Fix shape (if confirmed):** an `error` flag per load. Cards show `—` and a one-line "Couldn't load,
Retry" under the band, the same pattern `items-master` and `pr-register` already use (`fetchError`).

## S2 — Same pattern on PR Report and Receive Delivery

- `inventory/pr-report/page.tsx:255, 266`. Toast only. The PR picker and report body stay empty.
- `inventory/receive-delivery/page.tsx:248, 281`. Toast only. The PR picker is empty, which reads as "no open PRs".

**Check:** block `purchase-requests` and reload each page. **Fix shape:** as S1. `items-master` and
`pr-register` are the siblings to copy. ✅ A manual-coding candidate: small, with a sibling to copy, and reversible.

## S3 — A transient `/auth/me` failure sends the user to `/login`

The portal layout separates "server unreachable" (→ `/reconnecting`, which retries) from "session
invalid" (→ `/login`) (`app/(portal)/layout.tsx:146-165`, RAL-199). But **21 pages** also call
`fetchMe()` themselves and handle *any* failure with `router.replace("/login")`:

admin/users · config (index, accounts, audit-log, cc-typologies, divisions, esre-codes,
funding-sources, office-users, offices, price-index, procurement-presets) · inventory (index,
create-pr, distribution, item-ledger, items-master, pr-register, receive-delivery, stock-balances) ·
home · announcements.

A Functions restart or timeout *after* the refresh succeeded therefore bounces the user to the
login page. PPDO-238 then restores the session, but the user lands on their default page and loses
where they were. This is the same symptom class as the v1.0.1 idle-redirect bug.

**Check:** as `SA`, open `/config/offices`. In DevTools block `auth/me` (not `auth/refresh`) and
do a hard reload. Expect `/reconnecting` (or an inline error). Suspect: `/login`.

**Fix shape:** treat only a 401/403 from `fetchMe()` as "go to login", and send network errors to
`reconnectingUrl(pathname)`, as the layout does. Better still, move the gate into one shared helper
or `useMe()`, so 21 copies become one.

## S4 — Inventory reads don't check `CanAccessInventory`

| Service | Checks the permission | Division scope only (no permission check) |
|---|---|---|
| `PurchaseRequestService` | Create, MarkCompleted, Unmark, Template, Import, GSO preview | **GetAll, Search, GetById** (Update is Admin-only, fine) |
| `DeliveryService` | Create | **GetAll, GetByPRId, GetDeliveredTotalsByPR, GetById** |
| `InventoryService` | — | **GetStats, GetItemLedger** |
| `ItemService` | Create, Update | GetAll, Search, GetById, Lookup (no scope; it's a catalogue) |
| `PRReportService` | GetReport, Export (via `LoadAndAuthorizeAsync`) | — |
| `DistributionService` | both | — |

So a Staff user whose division has `CanAccessInventory = false` (e.g. Planning) can't *see*
Inventory in the sidebar, but can call `GET /api/purchase-requests`, `/deliveries`,
`/inventory/stats` and `/inventory/ledger` and get **their own division's** inventory data.
Division scope still holds (`DivisionScope.Resolve`, and office users get `SeeNothing`), so this is
**not a cross-division leak**. It is an inconsistency: the PR report for the same PR *does* refuse that user.

**Check:** the UI hides these pages, so test the API directly with an `ST` account whose division
has inventory **off**. `POST <uat-api>/api/auth/login` with that account (curl or Postman), take
`accessToken` from the response, then `GET <uat-api>/api/purchase-requests` with
`Authorization: Bearer <token>`. Expect 403. Suspect: 200 with rows. Repeat for `/deliveries`,
`/inventory/stats` and `/inventory/ledger`. Don't paste the token anywhere public.

**Decision needed (Ralph):** is "reads are division-scoped, the feature flag only gates the UI"
intended? If yes, write that into `docs/v1.8/Permission_Matrix.md` and pin it with a
`PermissionMatrixTests` row. If no, add `CanAccessInventoryAsync` to those reads (backend, TDD).
⚠️ Not a manual-coding candidate: a missing check is not caught by the compiler.

## S5 — Full-screen spinner before the shell (CLS)

14 pages return a full-screen centred spinner while `authChecked` is false: admin/users,
config (index, audit-log, funding-sources, offices, price-index, procurement-presets), inventory
(index, create-pr, distribution, item-ledger, items-master, pr-register, receive-delivery). The
`fetchMe()` cache makes this short on return visits. On a first visit or a slow network it is a
visible jump, which breaks the CLAUDE.md rule *render the shell immediately, skeleton matching the
loaded layout*.

**Check:** C1's throttled pass (Fast 4G + 4× CPU). Note how long the spinner shows and whether the
layout jumps. **Fix shape:** render the page header plus `TableSkeleton` instead of the spinner, the
same gate for all 14. It pairs naturally with S3 if both move into one helper.

## S6 — Wide tables with no visible horizontal scroll container

Files that render a `<table>` with no `overflow-x-auto` / `overflow-auto` in the same file:
`budget-planning/wfp/entry`, `budget-planning/report`, `budget-planning/allocation`,
`budget-planning/aip/detail`, `config/procurement-presets`, and the components `AipConflictPanel`,
`AipExpenditureTable`, `AipDivisionsPanel`. A parent may already scroll, so this is only a suspect.

**Check:** checklist item 10 at 390 px. Look for horizontal **page** scroll on each.

**Result (2026-10-26, local, 375 px, `admin`, PPDO-256):** every portal route without an ID was loaded,
plus AIP Detail. The portal scrolls inside `<main>`, so the test is `main.scrollWidth > main.clientWidth`
(the document itself never overflows).

- **Fixed:** Allocation (division table, 405 px), AIP Entry (`AipDivisionsPanel` table, 453 px) and the
  WFP page (the Division picker grew to fit its longest option, 543 px). The tables now scroll in their
  own box; the picker shrinks to the row.
- **By design, left for Ralph's call:** AIP Detail (`min-w-[1800px]`) and the WFP Report (`min-w-[1600px]`).
  Both let `<main>` scroll sideways on purpose, because their `sticky top-0` header needs `<main>` as its
  scroll container. A wrapper with `overflow-x-auto` would break the sticky header on desktop too. On AIP
  Detail the sector tab row (552 px) also overflows, but the table already makes the page that wide.
- ~~WFP Entry, Procurement Presets, `AipConflictPanel`, `AipExpenditureTable`~~: no page overflow in the
  states reached. Their tables are `w-full` and only appear after a choice, so a wide one is still possible
  with long data. Re-check in the A1 sweep.
- Clean: every Config, Inventory, Admin and shell page, and the rest of Budget Planning.

## S7 — Lint warning

`inventory/page.tsx:294`: `useMemo` lists `STATUS_ORDER` (module constant) as a dependency. Harmless.
Fold it into whichever PR next touches that file (it will be S1's).

---

## Method (so this can be repeated next release)

- **Page scan:** for each `page.tsx`, counts of API calls, `catch`, error-state setters, empty-state
  text, skeletons, spinners, `403`, `fetch(`, `/auth/me`, `alert`/`confirm`, `any`, `console.log`.
  Outliers were then read by hand.
- **Endpoint scan:** every `[Function]` with an `HttpTrigger`, checked for an auth call
  (`ValidateAsync`, `Authorize*Async`, `Check*KeyAsync`). Where a handler only validates the JWT,
  the service method it calls was checked for `CanAccessInventoryAsync` and `DivisionScope`.
- **Not covered by reading code:** real data shapes, actual layout at 390 px, timing, copy quality, and
  anything that needs a second persona. That is what A1–A3 are for.

💡 This scan is a good candidate for a reusable Skill (`uat-presweep`) to run at the start of each
release's UAT pass.
