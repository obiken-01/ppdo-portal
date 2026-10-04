---
status: findings
version: v1.8.x
date: 2026-10-04
page: frontend/src/app/(portal)/budget-planning/page.tsx (Investment Planning dashboard)
wireframes: wireframes/dashboard/
---

# Investment Planning dashboard — design pass

A review of the Investment Planning dashboard (`/budget-planning`) after v1.8.0 reached UAT. It covers how
the page reads for each role and how fast it loads. **No code was changed.** The fixes are proposals, drawn
in [wireframes/dashboard/](wireframes/dashboard/README.md).

Checked on local dev (`release/1.8.0` @ `7eb2f29b`) as `admin` (cross-office reviewer), `opa.user` (guest
encoder) and `opa.head.user` (guest department head), at 1440 px and 375 px.

## 1. Summary

- **The page still describes the AIP before submission existed.** Every role sees "AIP submission ·
  Opens in a later release", and the department head gets a disabled **Submit AIP** button. Submission
  has worked in AIP Entry since Phase 4. This is the one finding that is wrong, not just awkward.
- **The top of the page does not match the reader.** The PPDO reviewer is told to "Keep costing your AIP
  activities". Their real work, the Offices board, starts on the third screen.
- **Speed is fine; request order is the only cost.** The endpoints answer in 0.03–0.22 s with 2–7 KB, and
  the page barely moves while loading (CLS 0.0035). But the requests run three deep, one after another.
- **Proposed shape:** one status band at the top that says where the AIP is and who has it, with the one
  next step. Below it, each role's main work comes first and empty bands go.

## 2. Findings

Priority: **P1** wrong or misleading, **P2** clarity and layout, **P3** polish.

| # | P | Finding | Who sees it | Fix needs |
|---|---|---|---|---|
| F1 | P1 | Pipeline stage "AIP submission" is a constant: always "Todo · Opens in a later release" (`page.tsx` `submissionStage`) | Everyone | Backend field: the office's workflow state (B1) |
| F2 | P1 | Department head's action card: disabled "Submit AIP", "Submission opens in a later release" | Department heads | Frontend now (link to AIP Entry); full text needs B1 |
| F3 | P1 | Action card ignores the reviewer role: the PPDO reviewer is told to keep costing activities | `CanReviewAllOffices`, SuperAdmin | Frontend; the waiting count is already loaded for the sidebar badge |
| F4 | P2 | The reviewer's Offices board starts on screen 3, below PPDO's own tiles, fund bar and division table | Reviewer | Frontend: order bands by role |
| F5 | P2 | Recent activity prints database names: "retag_div on aip_activities #18501", "update on budget_ceilings #13", and times to the second | Everyone | Backend or client phrase map (B4) |
| F6 | P2 | Guest offices without divisions get an empty "Divisions" band ("No divisions configured…") whose description says "Click a row" | Most guest offices | Frontend: leave the band out |
| F7 | P2 | Division rows with nothing in them repeat `Todo · 0/0 · ₱0.00 · ₱0.00 · ₱0.00`; the "Allocation →" link wraps onto two lines | Host office | Frontend |
| F8 | P2 | The header line "FY 2028 · PPDO — …" repeats the context bar right under it; the locked Office and Division fields take a full row | Everyone | Frontend |
| F9 | P2 | With one fund, the fund card fills a third of the row and leaves two thirds empty | Host office | Frontend |
| F10 | P2 | The board's Not started column lists 15 chips, with the red-dot legend ("No reviewer — cannot submit") at the bottom, far from the dots | Reviewer | Frontend |
| F11 | P2 | Investment proposals, new in v1.8.0, are not on the dashboard at all, though AIP submit now warns about them | Everyone, FY 2028+ | Backend counts (B3) |
| F12 | P2 | On a phone, the action card and the stacked stage cards fill the first screen; the money is below the fold | Everyone on a phone | Frontend |
| F13 | P3 | The sidebar truncates "Investment Planni…" | Everyone | Shell, not this page |

Things that are already right and should stay: per-band loading and error states, skeletons that match the
loaded layout, no horizontal scroll at 375 px, the remembered Board/Table switch, and the ceiling tile always
shown even when read-only.

## 3. Performance

Measured on local dev, warm server, three runs each:

| Request | Avg | Size |
|---|---|---|
| `GET /budget-planning/dashboard` | 0.13–0.22 s | 3.1 KB |
| `GET /budget-planning/dashboard/office` | 0.21 s | 2.3 KB |
| `GET /budget-planning/dashboard/offices` | 0.20 s | 7.3 KB |
| `GET /budget-planning/activity` | 0.08 s | 1.7 KB |
| `GET /budget-planning/fiscal-years` | 0.03 s | 0.1 KB |

- **Layout shift: 0.0035** (Google's "good" is under 0.1). The skeletons do their job.
- **The first call after a restart took 1.2 s** (cold start). Production already pre-warms the host from the
  login page.
- **The `dashboard` and `activity` calls show twice in dev.** That is React strict mode, which runs effects twice in
  development only. Not a production cost.
- **The real cost is the chain:** `/auth/me` → `/dashboard` → `/dashboard/office` + `/dashboard/offices`.
  The last two wait for `/dashboard` only to learn the fiscal year and office id. At production's ~0.4 s per
  request, the lower half of the page appears roughly 0.4–0.6 s later than it needs to.

**Rules for the redesign, so it adds no slowness:**

1. **No new requests.** New figures (workflow state, proposal counts, waiting time) ride the existing
   `/dashboard/office` and `/dashboard/offices` responses.
2. **Two round trips at most after `/auth/me`.** Fire `/dashboard`, `/dashboard/office`, `/dashboard/offices`
   and `/activity` together: the office id comes from `/auth/me`, and the office endpoints default to the
   configured fiscal year when none is sent (B5).
3. **Set-based queries only.** Proposal counts are one grouped `COUNT` per fiscal year, never a loop per office or
   project (`docs/PERFORMANCE_GUIDELINES.md`).
4. **Slim payloads.** The whole page stays under 20 KB of JSON.
5. **Keep CLS under 0.1.** Every new band gets a skeleton of its loaded size, the status band included
   (wireframe board 5).

## 4. Proposed design

Drawn in [wireframes/dashboard/](wireframes/dashboard/README.md), within `docs/DESIGN_SYSTEM.md`:
flat, PPDO tokens, Segoe UI, no new colours.

- **One status band replaces the action card and the 3–5 pipeline cards.**
  - A plain sentence says where the AIP is and who has it ("Your encoders are done. The AIP is ready for
    you to send to PPDO.").
  - One line says why, then the one next step as a button, or none when there is nothing to do.
  - A thin step track runs underneath: Ceiling · Encoding · Department review · PPDO review · Accepted. The
    host office adds Division allocation and Program assignment.
  - Every state and reader has its own sentence (board 5).
- **The fiscal year moves into the page header**; the locked Office/Division fields go (the office is in the
  header line).
- **Bands are ordered by role:**
  - **Reviewer:** the province bar (offices per stage) and the Offices board come right after the status band. Then
    PPDO's own office shrinks to one summary card, beside an **Investment proposals, all offices** card (final /
    draft / none across offices, and the offices in PPDO review with the fewest final proposals). Reviewers can
    read and export every office's proposals, so the card links straight into that list.
  - **Cross-office review is a grant, not a role** (Ralph, 2026-10-04). Anyone given `CanReviewAllOffices`
    gets the reviewer top, a PPDO finance user included. A finance user with the grant sees the reviewer top
    first, then the allocation, fund and division bands below it.
  - **Department head / encoder:** the ceiling meter, then Investment proposals and Recent activity side by side.
  - **PPDO finance:** the fund card with the division split, then the division table.
- **Ceiling meter:** one wide tile, "₱38.4M of ₱50M", with a bar and "₱11.6M left". It states that it counts
  MOOE + CO rounded up, as the submit check does.
- **Investment proposals band (new):** a bar of final / draft / none, and the few projects that need attention,
  each with Create or Open.
- **Tables:**
  - **Divisions band:** left out when the office has no divisions.
  - **Division table:** rows with nothing in them fold into one line, zero amounts print as a dash, and the
    Allocation link stays on one line.
- **Offices board:** Not started becomes one wrapped list with "13 have no reviewer · Assign reviewers" at the
  top. The PPDO review column is tinted, since that is the reviewer's column.
- **Recent activity:** sentences grouped by day, times without seconds, "Full history" link.
- **Phone:** the step track becomes a short list, so the first screen holds the status, the button and the
  progress.

## 5. Backend fields the design needs

| ID | Field | Endpoint | For |
|---|---|---|---|
| B1 | Office workflow state (`Draft`, `DepartmentReview`, `SubmittedToPpdo`, `Returned`, `Consolidated`), the date it entered it, divisions submitted n of m, and the count of activities with no cost | `/dashboard/office` | F1, F2, status band |
| B2 | `submittedAt` per office | `/dashboard/offices` | "Waiting 3 days" on the board |
| B3 | Proposal counts per office for the year: final, draft, none | `/dashboard/office` (and `/offices` for the reviewer) | F11 |
| B4 | A readable description per activity entry, or the fields to build one (office code, record label, amount) | `/activity` | F5 |
| B5 | Default the fiscal year server-side when none is sent | `/dashboard/office`, `/dashboard/offices` | Parallel loading |

## 6. Suggested tickets

| Order | Ticket | Scope | Size |
|---|---|---|---|
| 1 · **PPDO-175** | Fix the stale submission stage and the department head's disabled Submit (F1, F2) | B1 + frontend | S |
| 2 · **PPDO-176** | Role-aware top: reviewer action card and band order (F3, F4) | Frontend | S |
| 3 | Load the bands in parallel (B5) | Backend + frontend | S |
| 4 | Status band replaces the action card and the rail (incl. phone, F12) | Frontend | M |
| 5 | Cleanups: header, empty Divisions band, zero rows, one-fund width, Not started column (F6–F10) | Frontend | S |
| 6 | Investment proposals band (F11, B3), including the all-offices card for cross-office reviewers | Backend + frontend | M |
| 7 | Readable recent activity (F5, B4) | Backend + frontend | M |

Tickets 1 and 2 fix wrong information and could ship as a v1.8.0 UAT fix. The rest fits v1.8.1.

**Manual-coding candidates (CLAUDE.md):** ticket 5 is a good one for you to code by hand. It is small,
frontend-only and reversible. `DivisionTable.tsx` and `page.tsx`'s header are its siblings, and the result
is easy to check in the browser. Ticket 1 is not a candidate: it touches the workflow state that the
permission and review rules depend on.

## 7. Open questions

1. Should the status band name the person who holds the AIP ("with Juan dela Cruz") or only the role ("with your
   department head")? Default: the role. Names change, and the role answers "who do I ask".
2. Should the reviewer's dashboard still show PPDO's own money tiles? Default: no, one summary card with a link.
   The finance view keeps them.
3. Should "Waiting N days" on the board count calendar days or working days? Default: calendar days. It is simpler,
   and the deadline is a calendar date.
