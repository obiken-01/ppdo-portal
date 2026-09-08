/**
 * Who may open which Budget Planning page (PPDO-81).
 *
 * ⚠️ **These exist so the sidebar and the route guard cannot disagree.** Before this, `canAccessBudgetPlanning`
 * opened every page under `/budget-planning` and each page re-stated its own rule inline — which is
 * how the AIP record list came to be offered to encoders whose only button on it (`+ New AIP`) was
 * already gated away from them. A nav item that leads to a redirect is worse than no nav item: the
 * reader concludes the feature is broken rather than that it is not theirs.
 *
 * ⚠️ **Courtesy, not a guard.** Every rule here is enforced independently by the server — the AIP
 * write paths run `AipWriteGuard`, and reads run `AipReadScope`. Hiding a link only spares somebody
 * a page they cannot use; it is never the reason anything is safe.
 */

import type { MeResponse } from "@/types";

/** Admin or SuperAdmin. The two roles that own province-wide setup. */
function isAdminRole(me: MeResponse): boolean {
  return me.role === "Admin" || me.role === "SuperAdmin";
}

/**
 * The AIP **record list and detail** — `/budget-planning/aip` and its `new` / `detail` /
 * `import-preview` children. Admin and SuperAdmin only.
 *
 * ⚠️ **Not the same page as AIP Entry, and the distinction is the point.** Since PPDO-61/62 the base
 * AIP record is created once per fiscal year by an Admin, who thereby populates every office's
 * programs from its LDIP. An office encoder never creates anything here — `aip/entry` deliberately
 * offers no create action, precisely so there is one create path rather than two.
 *
 * ↩️ It used to be `canAccessBudgetPlanning`, so an encoder saw a table of every fiscal year's
 * records whose only action was already denied them. Their surface is `aip/entry`.
 *
 * ⚠️ **Known consequence, accepted deliberately:** `aip/detail` is also the read view for the
 * FY≤2027 **uploaded** AIPs, so a guest office now has no route to last year's approved document.
 * That data is PPDO-held today. If an office ever needs it, the answer is a read-only view — not
 * re-opening this list, whose create and finalize actions are what had to be contained.
 */
export function canOpenAipRecords(me: MeResponse): boolean {
  return me.canAccessBudgetPlanning && isAdminRole(me);
}

/**
 * The LDIP pages. Host office (PPDO) only — **any role**, not just Admin.
 *
 * ⚠️ **Gated on the office, not on the role, and the two would behave very differently here.** PPDO
 * planning staff work in the LDIP as a matter of course; it is their page. What does not belong is
 * a *guest* office seeing it: from FY2028 the LDIP is the **closed list** an AIP's programs must come
 * from (`aipProgramsAreLdipOnly`), so an office missing a program will come here to add it — and
 * every write control on the page is already Admin-only. They would find a page that shows them the
 * problem and refuses the fix.
 *
 * The write gating inside the page is unchanged and still Admin/SuperAdmin. This decides only who
 * sees the page at all.
 */
export function canOpenLdip(me: MeResponse): boolean {
  return me.canAccessBudgetPlanning && me.isHostOffice;
}

/**
 * Where to send somebody who reached one of the above without the grant.
 *
 * The Budget Planning hub, which anyone holding `canAccessBudgetPlanning` can open — it names the
 * stage they are actually on, so the redirect lands somewhere that answers "then what should I be
 * doing?" rather than dumping them at a generic dashboard.
 *
 * ⚠️ A guest-office user has no `/dashboard` — the sidebar gives them Budget Planning and nothing
 * else (`Sidebar.tsx`) — so the no-access fallback splits on office, matching `ldip/new`'s existing
 * redirect. Sending them to `/dashboard` would be a redirect to another redirect.
 */
export function budgetPlanningFallback(me: MeResponse): string {
  if (me.canAccessBudgetPlanning) return "/budget-planning";
  return me.isHostOffice ? "/dashboard" : "/account";
}
