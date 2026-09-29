"use client";

/**
 * The admin-set default fiscal year, read once and shared (v1.8.0 — PPDO-145,
 * `docs/v1.8/Default_Fiscal_Year_Spec.md` §3.2, decision 9).
 *
 * The same module-level cache as `lib/me-cache`: the first page that asks makes the one
 * `GET /budget-planning/fiscal-years` call, and every later page in the tab reads the cached value
 * synchronously. A change made on the config page reaches other users on their next full page load
 * — there is no live push (spec §7).
 *
 * Not cleared on logout: the value is province-wide, the same for whoever signs in next.
 *
 * ⚠️ **Fails open.** If the call fails, `defaultFiscalYear` settles as null and each page uses its
 * own fallback, silently (§3.2 "Failure"). A failure is NOT cached, so the next page tries again.
 *
 * ⚠️ **The raw setting, not the resolved year.** `FiscalYears.fiscalYear` folds in the newest AIP
 * year and the calendar; the client-resolved pages each keep their own fallback (decision 4), so
 * they need the setting alone. Null means "unset" — the page behaves exactly as before.
 */

import { useEffect, useState } from "react";
import { getFiscalYears } from "./budget-planning";

let _cache: { value: number | null } | null = null;
let _inflight: Promise<number | null> | null = null;

/** Resolves the setting (null when unset or unreachable). Never rejects. */
export function fetchDefaultFiscalYear(): Promise<number | null> {
  if (_cache) return Promise.resolve(_cache.value);
  if (_inflight) return _inflight;
  _inflight = getFiscalYears()
    .then((data) => {
      _cache = { value: data.defaultFiscalYear ?? null };
      return _cache.value;
    })
    .catch(() => null)
    .finally(() => { _inflight = null; });
  return _inflight;
}

/**
 * The AIP record the WFP pages preselect for the default year (spec §3.3), or null — no default, or
 * no record for it, which leaves each page's own behaviour in place.
 *
 * A non-archived record is preferred: there is one base record per fiscal year, but an archived
 * predecessor can share the year, and archived records cannot take WFP entry.
 */
export function aipForFiscalYear<T extends { id: number; fiscalYear: number; status: string }>(
  records: readonly T[],
  fiscalYear: number | null,
): T | null {
  if (fiscalYear == null) return null;
  const forYear = records.filter((r) => r.fiscalYear === fiscalYear);
  return forYear.find((r) => r.status !== "Archived") ?? forYear[0] ?? null;
}

/**
 * `{ ready, defaultFiscalYear }`. `ready` is false only until the first read settles — on a warm
 * cache it is true on the first render, so a page never waits twice.
 *
 * ⚠️ A page whose URL already names a year must **not** wait on `ready` (spec §6.2): the link wins
 * regardless, and holding it would add a round trip to every deep link.
 */
export function useDefaultFiscalYear(): { ready: boolean; defaultFiscalYear: number | null } {
  const [state, setState] = useState(() =>
    _cache ? { ready: true, defaultFiscalYear: _cache.value } : { ready: false, defaultFiscalYear: null }
  );

  useEffect(() => {
    if (state.ready) return;
    let live = true;
    void fetchDefaultFiscalYear().then((value) => {
      if (live) setState({ ready: true, defaultFiscalYear: value });
    });
    return () => { live = false; };
  }, [state.ready]);

  return state;
}
