---
id: PPDO-182
title: 'Perf: compress API responses (spike, then switch on)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.1 — Optimization & Dashboard Fixes
created: 2026-10-04
completed: 2026-10-04
parent: (none)
blocked_by: (none)
related: PPDO-195
url: https://linear.app/ralphoksiprojects/issue/PPDO-182
uuid: b4a72e70-84de-4bb7-9a04-e3ecbe7e3cad
---

# PPDO-182 — Perf: compress API responses (spike, then switch on)

From the performance audit, 2026-10-04 (`docs/v1.8/Performance_Audit_2026-10-04.md`, finding **O1**). Optimization ticket 1 of 8. Backend, size S. **Do first:** it shrinks every other payload.

## Current behaviour

* **No compression:** the API sends JSON uncompressed. Production `GET /api/announcements` with `Accept-Encoding: gzip, deflate, br` returned no `Content-Encoding`. (That body is only 5 KB, so confirm on a large response first.)
* **Large responses** (local, uncompressed):
  * `GET /budget-planning/aip/13`: 1,520 KB. Gzipped: 155 KB.
  * `/aip/13/summary`: 724 KB.
  * `/config/price-index/picker`: 713 KB.

## Target behaviour

* **Spike first:** `Program.cs` already uses `ConfigureFunctionsWebApplication` ([ASP.NET](<http://ASP.NET>) Core integration). Find where compression can be applied so it survives the Functions host proxy: [ASP.NET](<http://ASP.NET>) response compression, or gzip in `ConfigHttp` / the JSON writers. Write the answer on this ticket.
* **Then switch on:** gzip (Brotli if cheap) for JSON responses over \~1 KB when the client sends `Accept-Encoding`.
* **Unchanged:** CORS, the auth cookie, file downloads (`.xlsx` / `.docx` / CSV are already compressed or small) and error shapes.

## Acceptance

- [ ] Local: `curl -H "Accept-Encoding: gzip" …/aip/13 -D -` shows `Content-Encoding: gzip`, and the body is \~150 KB.
- [ ] The browser renders the AIP detail, AIP Entry, WFP Entry and price index pages unchanged.
- [ ] Excel/Word exports still download and open.
- [ ] **UAT first:** the same curl against UAT shows `Content-Encoding`; then production after merge.
- [ ] A response without `Accept-Encoding` is still plain JSON.

## Risks

* Compression costs CPU on the Consumption plan. Gzip at the default level on ≤2 MB of JSON is small; check App Insights duration before/after on UAT.
