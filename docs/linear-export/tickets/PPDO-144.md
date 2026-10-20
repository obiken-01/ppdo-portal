---
id: PPDO-144
title: 'Demo 2.14 T2 — Config page: Investment Planning Settings (default fiscal year)'
status: Done
project: PPDO Portal
labels: 
milestone: Demo 2 — change requests & fixes
created: 2026-09-27
completed: 2026-09-30
parent: PPDO-136
blocked_by: PPDO-143
related: (none)
url: https://linear.app/ralphoksiprojects/issue/PPDO-144
uuid: f58720fd-36c9-482b-8a97-78d2496e8eeb
---

# PPDO-144 — Demo 2.14 T2 — Config page: Investment Planning Settings (default fiscal year)

**Spec:** `docs/v1.8/Default_Fiscal_Year_Spec.md` — §3.1, §6.1, §10. Blocked by [PPDO-143](https://linear.app/ralphoksiprojects/issue/PPDO-143/demo-214-t1-backend-default-fiscal-year-setting-host-office-gate) (endpoints).

## Scope

1. New page `/config/investment-planning` ("Investment Planning Settings") with one section, **Default fiscal year**:
   * help text
   * FY number input + **Save**
   * **Clear default** link-button, shown only when a year is set, with a `ConfirmDialog`
   * a "Last changed {Manila date/time} by {name}" line
2. New tile on `config/page.tsx`. **Hidden** unless the user is SuperAdmin, or a config manager whose `/auth/me` has `isHostOffice: true`. This is a courtesy only; the server enforces the rule.
3. `lib/config.ts` client functions: `getDefaultFiscalYear` / `updateDefaultFiscalYear`.

## UI states (spec §6.1)

* **Loading:** a skeleton of the input row, same height as the loaded row.
* **Unset:** "Not set — each page uses its own default."
* **Error:** a banner with **Retry**.
* **Save / clear:** toasts with the copy from §3.1.
* **Validation:** "Fiscal year must be between 2020 and {max}.", shown under the input, both client-side and from the server's 400.
* **Forbidden:** direct navigation shows the existing config forbidden state.

Follow `docs/DESIGN_SYSTEM.md`: flat design (no `rounded-*`), PPDO tokens, no `text-slate-700`. Reuse `ConfirmDialog` and `useToast`; this ticket adds no new shared component.

## Acceptance (from spec §10)

* A SuperAdmin sees the tile. Saving 2028 shows the toast and the last-changed line.
* Saving 2019 shows the validation message, and nothing is saved.
* A guest-office Admin and a Staff user without `CanManageConfig` see no tile and get the forbidden state on the direct URL.
* A SuperAdmin tied to a guest office can still save.
* **Clear default** asks for confirmation before clearing.

**Manual-implementation candidate: borderline.** It's a plain single-field form, but it's a new page.

Commit: `feat(config): add default fiscal year setting page (PPDO-136)`
