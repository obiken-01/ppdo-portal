"use client";

/**
 * LandingPageSelect — picks the page someone lands on after signing in (RAL-262).
 *
 * Used in four places: the User form, Division config, Office config and /account.
 *
 * The options are filtered by what the target can actually reach. That is not cosmetic:
 * saving a page the user cannot open does not fail at redirect time, it *loops* — the page
 * ejects them and the redirect sends them back. The backend rejects unreachable choices too;
 * this filter exists so the option is never offered in the first place.
 */

import type { LandingPageKey, MeResponse } from "@/types";

/** Every target, in the order they should be offered. */
const ALL: { key: LandingPageKey; label: string }[] = [
  { key: "MainDashboard",           label: "Main Dashboard" },
  { key: "InventoryDashboard",      label: "Inventory Dashboard" },
  { key: "BudgetPlanningDashboard", label: "Budget Planning Dashboard" },
  { key: "Profile",                 label: "My Account" },
];

/** What a page needs before it can be offered. Mirrors LandingPageResolver.IsReachableAsync. */
export interface LandingReachability {
  /** Office users are bounced off the main dashboard by the portal gate. */
  isOfficeUser: boolean;
  canAccessInventory: boolean;
  canAccessBudgetPlanning: boolean;
}

/** The subset of targets reachable under `r`. Profile is always included. */
export function reachableLandingPages(r: LandingReachability): LandingPageKey[] {
  return ALL.filter(({ key }) => {
    switch (key) {
      case "MainDashboard":           return !r.isOfficeUser;
      case "InventoryDashboard":      return r.canAccessInventory;
      case "BudgetPlanningDashboard": return r.canAccessBudgetPlanning;
      case "Profile":                 return true;
    }
  }).map(({ key }) => key);
}

/** Reachability for the signed-in user, from /auth/me. */
export function reachabilityFromMe(me: MeResponse): LandingReachability {
  return {
    isOfficeUser: me.officeId != null,
    canAccessInventory: me.canAccessInventory,
    canAccessBudgetPlanning: me.canAccessBudgetPlanning,
  };
}

export interface LandingPageSelectProps {
  value: LandingPageKey | null;
  onChange: (value: LandingPageKey | null) => void;
  /** Which options to offer. Omit to offer all of them. */
  reachability?: LandingReachability;
  label?: string;
  /** Explains what "no preference" falls back to in this context. */
  hint?: string;
  disabled?: boolean;
  id?: string;
}

export default function LandingPageSelect({
  value,
  onChange,
  reachability,
  label = "Landing page",
  hint,
  disabled,
  id = "landing-page",
}: LandingPageSelectProps) {
  const allowed = reachability ? reachableLandingPages(reachability) : ALL.map((o) => o.key);

  // A previously-saved value that is no longer reachable still has to be shown, or the
  // select would silently render as "no preference" and saving would quietly wipe it.
  const options = ALL.filter((o) => allowed.includes(o.key) || o.key === value);

  return (
    <div>
      <label htmlFor={id} className="block text-xs font-medium text-slate-600 mb-1">
        {label}
      </label>
      <select
        id={id}
        value={value ?? ""}
        disabled={disabled}
        onChange={(e) => onChange(e.target.value === "" ? null : (e.target.value as LandingPageKey))}
        className="w-full px-3 py-2 text-sm border border-slate-200 focus:outline-none focus:ring-2 focus:ring-green-600 disabled:bg-slate-100 disabled:text-slate-400"
      >
        <option value="">— No preference —</option>
        {options.map(({ key, label: optionLabel }) => (
          <option key={key} value={key}>
            {optionLabel}
            {!allowed.includes(key) ? " (no longer available)" : ""}
          </option>
        ))}
      </select>
      {hint && <p className="mt-1 text-xs text-slate-600">{hint}</p>}
    </div>
  );
}
