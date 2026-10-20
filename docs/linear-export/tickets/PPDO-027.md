---
id: PPDO-27
title: 'Config dashboard tiles + sidebar links for the new config pages'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.0 — Office Users, AIP Redesign & Reviewer Flow
created: 2026-08-20
completed: 2026-09-02
parent: PPDO-28
blocked_by: PPDO-23, PPDO-25
related: RAL-232
url: https://linear.app/ralphoksiprojects/issue/PPDO-27
uuid: 452abe68-fb46-4089-b645-c60947a79559
---

# PPDO-27 — Config dashboard tiles + sidebar links for the new config pages

**V18-14** — plan: `docs/v1.8/Phase_Plan.md` §3.2. Depends on V18-09 ([RAL-247](https://linear.app/ralphoksiprojects/issue/RAL-247/config-climate-change-typology-table-management-page)) and V18-10 ([RAL-248](https://linear.app/ralphoksiprojects/issue/RAL-248/config-esre-code-table-management-page)).

Add tiles to `config/page.tsx` for the climate-change typology and eSRE config pages, matching the existing tiles.

## ➕ Widened 2026-09-01 — sidebar links too

**Both new pages are currently unreachable except by typing the URL.** The tiles were the ticketed half; the sidebar is the other half of the same outcome, and shipping tiles alone is a half-fix. `Sidebar.tsx` has a collapsible **Configuration** group with one `<Link>` per config page (accounts · offices · funding-sources · price-index · divisions · procurement-presets · audit-log). Add two more, using the same `childLinkCls(isActive(...))` shape.

The existing order is not alphabetical — it runs roughly by how often each page is touched — so slot the two next to funding-sources rather than appending them.

## ⚠️ The count endpoints are the actual work

Worth stating plainly, because the tiles look like a two-line change and are not. Honouring the rule below means, per page: a `CountAsync` on the repository, a service method, a `GET /api/config/…/count` endpoint, and a `countX` client function in `lib/config.ts`. `RAL-232` did exactly this for the price index — copy that shape rather than inventing one.

The two lists are short enough that `.length` would work today. That is not the point: the tile you add is the tile the next config page copies.

⚠️ **Serve each tile's count from a count endpoint, not by fetching the list and taking **`.length`**.** That exact mistake cost 1.57 MB per Config dashboard load for the price index ([RAL-232](https://linear.app/ralphoksiprojects/issue/RAL-232/price-index-slim-the-list-payload-157-mb-and-stop-the-config-dashboard), which cut it to 41 bytes). These two lists are short, so the cost is small — but the pattern is what gets copied next time, and `docs/PERFORMANCE_GUIDELINES.md` is explicit: counts go through `CountAsync`.

**Done when:** both tiles render with live counts, and no tile on the page downloads a list to count it.
