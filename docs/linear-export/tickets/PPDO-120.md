---
id: PPDO-120
title: 'V18-71.4 — Conflict panel in AIP Entry (Deploy 2)'
status: Done
project: PPDO Portal
labels: 
milestone: v1.8.2 — Dashboard Redesign, Caching & Remaining Optimizations
created: 2026-09-22
completed: 2026-09-23
parent: PPDO-116
blocked_by: PPDO-119
related: PPDO-64, PPDO-75, PPDO-191
url: https://linear.app/ralphoksiprojects/issue/PPDO-120
uuid: 993f4b55-ee92-4f74-ae86-bd7b58c2ea8b
---

# PPDO-120 — V18-71.4 — Conflict panel in AIP Entry (Deploy 2)

Child 4 of [PPDO-116](<https://linear.app/ralphoksiprojects/issue/PPDO-116>). **Deploy 2.** Blocked by [PPDO-119](<https://linear.app/ralphoksiprojects/issue/PPDO-119>).

This is the ticket the encoder actually sees. The backend can be perfect and this still decides whether the feature helps or annoys.

## 1. Context docs to read

Read `CLAUDE.md`, `PROJECT_DOCUMENTATION_NET_AZURE.md`, and `PPDO_PROJECT_CONTEXT.md`.

Read `docs/v1.8/AIP_Concurrent_Edit_Spec.md` **§3 and §6 FULLY** — authoritative. **Read** `docs/DESIGN_SYSTEM.md` **before writing any markup** (`CLAUDE.md` requires it).

## 2. Files to read before writing code

* `frontend/src/lib/aip.ts` — the AIP API client. `updateActivity` (\~line 180), `updateActivityDetails` (\~line 281), the expenditure calls (\~line 322+). Where the version is held and sent.
* `frontend/src/app/(portal)/budget-planning/aip/entry/page.tsx` — the entry screen.
* `frontend/src/app/(portal)/budget-planning/aip/detail/page.tsx` — the inline editors. **412 lines** since [PPDO-64](https://linear.app/ralphoksiprojects/issue/PPDO-64/extract-aipdetailpagetsx-into-components-and-lib-pure-move-no) extracted it, so it is workable; check the extracted components too.
* `frontend/src/types/` — where `AipConflict` goes. ⚠️ `CLAUDE.md`: **no** `any`.
* `frontend/src/lib/api.ts` — the Axios instance. Do **not** handle 401s here; the interceptor owns that.
* `docs/DESIGN_SYSTEM.md` §1 (colour) and the flat-design rule — portal pages carry **no** `rounded-*` on panels.

## 3. Current → target

**Current:** a failed AIP save surfaces as a generic error. There is no version in any request, and a 409 would be indistinguishable from any other failure.

**Target:** the client holds each row's `rowVersion`, sends it on every write, and on 409 renders an inline conflict panel offering Overwrite / Discard.

> State the current behaviour back in one sentence as output before you start.

## 4. Working branch + PR target

Integration branch `release/1.8.0`. Create `feature/v1.8.0-ppdo-<id>-aip-conflict-panel` off it; PR against `release/1.8.0` **(NOT** `main`**, NOT** `uat`**)**.

## 5. Implementation steps

1. **Types + client.** `AipConflict` in `types/`, matching spec §4's payload. Hold the `rowVersion` returned with each activity/expenditure and send it on every write.
   *Verify:* `npx tsc --noEmit` clean; Network tab shows `rowVersion` on a save.
2. **Conflict panel.** Expands **in place on the conflicting row**, pushing content down.
   * `Ben Reyes changed this activity at 9:14 AM, while you were editing it.`
   * Two-column compare, **differing fields only**: *Your value* / *Their value*.
   * **Overwrite with mine** (danger) · **Discard mine and reload** (secondary).
   * ⚠️ **The user's typed input stays in the form, editable, throughout.** This is the point of the whole ticket — see §7.
   * ⚠️ **Time renders in Manila (UTC+8)**; the API returns UTC (`CLAUDE.md`).
     *Verify:* two browsers, same activity, save in both. Screenshot the panel.
3. **Overwrite.** Resubmits the user's values with the `rowVersion` **from the conflict payload**, so it is one round trip rather than reload-then-save.
   *Verify:* Overwrite in browser B, reload browser A, confirm B's values.
4. **Discard.** Reloads the row and drops local edits. No confirm dialog — the button already says what it does.
   *Verify:* Discard shows the other encoder's values.
5. **Expenditure + details parity.** The same panel on the expenditure editor and the activity **details** form, not only the amount grid.
   *Verify:* force a conflict on each of the three surfaces.
6. **Verify the non-conflict path did not regress.** Two encoders on **different** activities, same office, simultaneously → both save, no panel.
   *Verify:* two browsers. ⚠️ **This is the regression that would make the feature hated** — a warning that fires on unrelated edits trains people to click through it.

## 6. Risks, rollback, sign-off

🛑 **Sign-off before merge** — step 2 touches `components/ui/` if you extract the panel, and `CLAUDE.md` lists the shared design system as a sign-off trigger (blast radius is every page). Keeping it local to AIP avoids that; extract only if a second caller appears.

| Risk | Rollback |
| -- | -- |
| Panel implemented as a **toast** | Toasts auto-dismiss, and this one carries the only copy of a decision the user must make. Dismissing by waiting would leave rejected money edits on screen with no indication — strictly worse than the bug being fixed. Spec §6 is explicit. |
| Panel implemented as a **modal** | Covers the row being compared against. Also spec §6. |
| Local edits cleared on 409 | Defeats the entire feature. The user loses exactly the work this exists to protect. |
| Version not refreshed after a successful save | Every second save by the same user conflicts with themselves. Add a test or check it by hand — save twice in a row. |

## 7. Out of scope — do NOT

* **Do NOT** auto-merge field by field. These are money fields; spec §2 🅒.
* **Do NOT** poll for changes or show presence ("Ben is editing this"). Spec §7.
* **Do NOT** notify the other editor. Open follow-up, needs [PPDO-75](https://linear.app/ralphoksiprojects/issue/PPDO-75/v18-58-in-app-notifications-pending-count-review-queue)'s plumbing.
* **Do NOT** touch the backend. Child 3 shipped it; child 5 finishes it.
* **Do NOT** add `rounded-*` to the panel, and **do NOT** use `text-slate-700` (`CLAUDE.md`).

## 8. Commit message

```
feat(aip): conflict panel for concurrent AIP edits (V18-71)
```
