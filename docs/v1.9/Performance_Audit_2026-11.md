---
type: audit
version: v1.9.2
status: current
tickets: [PPDO-257]
updated: 2026-10-26
status_note: "draft — §2 bundle baseline done; §3–§6 need UAT access and are Ralph's"
supersedes_note: "none (follows ../v1.8/Performance_Audit_2026-10-04.md)"
date: "2026-10-26"
---

# Performance audit — 2026-11 (v1.9.2 re-measure)

Up: [v1.9 hub](README.md) · Plan: [UAT Polish Plan §5](UAT_Polish_Plan.md#5-workstream-c--performance-ppdo-257)

> **PPDO-257.** The [2026-10-04 audit](../v1.8/Performance_Audit_2026-10-04.md) tickets
> (PPDO-182…189) have all shipped. This audit **re-measures**: it confirms those gains held and finds
> what the v1.9.0 features added. **No code was changed.**
>
> **Status:** §2 (bundles) was measured on 2026-10-26 from a production build of `release/1.9.2` @
> `211ef9f5`. §3–§6 need UAT or App Insights access and are left for Ralph to fill in with the tables
> already laid out below.

## 1. Summary

- **The lazy-loading fix (O14) worked.** The rich-text pages that were the heaviest on 2026-10-04
  (265–283 kB first load) are now 129 kB (Announcements) and 164 kB (Proposal editor). The heaviest
  page is now **AIP Entry at 183 kB**.
- **Shared JS is flat:** 87.8 kB (87 kB on 2026-10-04).
- **One new P3:** the login page is the heaviest *public* page (157 kB, 30.6 kB of it page-specific).
  `zod` and `react-hook-form` are imported **only** by `login/page.tsx`. That page is also the one a
  cold visitor loads first.
- *(§3–§6 pending: API sizes and times, the throttled pass, Lighthouse, App Insights.)*

## 2. Bundles (measured 2026-10-26)

`NEXT_PUBLIC_API_BASE_URL=/api npm run build` on `release/1.9.2` @ `211ef9f5`. "First load" is
Next.js's gzipped first-load JS for the route, including the shared 87.8 kB.

### 2.1 Before → after for the pages the last audit named

| Page | 2026-10-04 | 2026-10-26 | Note |
|---|---|---|---|
| Shared by all | 87 kB | **87.8 kB** | Flat |
| `/announcements` (TipTap) | 265 kB | **129 kB** | O14, editor lazy-loaded |
| `/budget-planning/proposals/edit` (TipTap) | 283 kB | **164 kB** | O14 |
| `/dashboard` (FullCalendar) | 198 kB | **132 kB** | O14, calendar lazy-loaded |

### 2.2 Heaviest routes now

| Route | Page JS | First load |
|---|---|---|
| `/budget-planning/aip/entry` | 13.1 kB | **183 kB** |
| `/budget-planning/aip/review` | 6.68 kB | 174 kB |
| `/budget-planning/report` | 13.4 kB | 173 kB |
| `/budget-planning/aip/review/search` | 5.75 kB | 165 kB |
| `/budget-planning/proposals/edit` | 26.3 kB | 164 kB |
| `/login` | **30.6 kB** | 157 kB |
| `/inventory/pr-register` | 8.8 kB | 145 kB |
| `/budget-planning/aip/detail` | 12.6 kB | 144 kB |
| `/budget-planning` | 13.3 kB | 143 kB |
| `/inventory/items-master` | 6.95 kB | 141 kB |

Every other route is 88–138 kB. The full table is reproducible with the command above.

### 2.3 Finding

| # | P | Finding | Where | Suggested fix |
|---|---|---|---|---|
| N1 | P3 | **`zod` + `react-hook-form` ship only for the login form.** They are the largest page-specific code on the first page a cold visitor loads (chunk with `zod` is ~87 KB raw) | `app/(public)/login/page.tsx:23-25`; no other file imports `zod` | Two fields don't need a schema library: plain controlled inputs plus a small validator. Re-measure after the change. Low priority: 157 kB is still well under the portal pages |

## 3. API responses on UAT *(pending: Ralph)*

Re-run the [2026-10-04 §2 table](../v1.8/Performance_Audit_2026-10-04.md#2-measurements) on UAT, logged in
as `SA`, with DevTools → Network ("Size" shows transferred/resource). Three warm runs; record the median.

| Request | 10-04 (local, uncompressed) | UAT transferred | UAT resource | Time | `Content-Encoding` |
|---|---|---|---|---|---|
| `GET /budget-planning/aip/{id}` (largest FY record) | 1,520 KB | | | | |
| `GET /budget-planning/aip/{id}/summary` | 724 KB | | | | |
| `GET /config/price-index/picker?active=true` | 713 KB | | | | |
| `GET /config/price-index/picker?search=paper` | 14 KB | | | | |
| `GET /config/cc-typologies` | 78 KB | | | | |
| `GET /config/accounts` | 70 KB | | | | |

**New in v1.9.0** (no baseline):

| Request | Transferred | Resource | Time | Note |
|---|---|---|---|---|
| `GET /notifications/unread-count` | | | | Fires on tab focus |
| `GET /notifications` | | | | |
| `GET /budget-planning/aip/{aipId}/offices/{officeId}/comments` | | | | |
| `GET /budget-planning/proposals/{id}/comments` | | | | |
| `GET /budget-planning/wfp/report/preview` (initial WFP) | | | | |
| `POST /budget-planning/wfp/report/bulk-exports` → job → download | | | | Time to ready |
| `GET /budget-planning/aip/{aipId}/ceiling` | | | | |

## 4. Throttled pass *(pending: Ralph)*

Chrome DevTools: Network **Fast 4G** and CPU **4× slowdown**. This is the pass the
[loading-guard audit](Loading_Guard_Audit.md#not-checked) left out. For each page: time to first
meaningful content, any layout jump (CLS), and any button usable before its data loads. Also note
pre-sweep [S5](UAT_PreSweep_Findings.md) (full-screen spinner gate) where it shows.

| Page | First content | Layout jump? | Early-clickable control? | Notes |
|---|---|---|---|---|
| AIP Entry | | | | |
| AIP Review | | | | |
| Proposals editor | | | | |
| WFP Entry | | | | |
| Allocation | | | | |
| Office Ceilings | | | | |
| Report | | | | |
| Receive Delivery | | | | |
| Distribution | | | | |
| Create PR | | | | |
| Config: Offices / Price Index (spinner gate) | | | | |

## 5. Lighthouse *(pending: Ralph)*

Chrome → Lighthouse → Mobile, Performance only, on UAT.

| Page | Score | LCP | CLS | TBT |
|---|---|---|---|---|
| `/` (landing) | | | | |
| `/login` | | | | |
| `/dashboard` (signed in) | | | | |

## 6. App Insights, `ppdo-portal-api-uat` *(pending: Ralph)*

Portal → App Insights → Performance → Operations, over the UAT window (since 2026-10-20). Top 10 by
p95, plus Failures.

| Operation | Count | p50 | p95 | Failures |
|---|---|---|---|---|
| | | | | |

Re-check **PPDO-205** (AIP review cross-office search, p95 2.4 s on the last check).

## 7. Tickets

Per plan §5: one ticket per P1/P2. Fix P1s in v1.9.2; P2/P3 become tickets unless they are S-sized.

| Finding | P | Ticket |
|---|---|---|
| N1 login `zod` | P3 | *(file if you agree; it's small, and a ✅ manual-coding candidate: one file, with the shape of the field validation as the guide)* |

## Related

- [Performance audit 2026-10-04](../v1.8/Performance_Audit_2026-10-04.md): the baseline
- [Loading guard audit](Loading_Guard_Audit.md): the throttled pass it left undone
- [UAT pre-sweep findings](UAT_PreSweep_Findings.md): S5 (spinner gate) overlaps §4
- [PERFORMANCE_GUIDELINES.md](../PERFORMANCE_GUIDELINES.md)
