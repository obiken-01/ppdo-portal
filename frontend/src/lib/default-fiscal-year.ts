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
 * own fallback (§3.2 "Failure"). A failure is NOT cached, so the next page tries again. The hook
 * also reports `failed`, so a page whose picker depends on it can show an error with Retry
 * (PPDO-277, AIP Review search).
 *
 * ⚠️ **The raw setting, not the resolved year.** `FiscalYears.fiscalYear` folds in the newest AIP
 * year and the calendar; the client-resolved pages each keep their own fallback (decision 4), so
 * they need the setting alone. Null means "unset" — the page behaves exactly as before.
 */

import { useCallback, useEffect, useState } from "react";
import { getFiscalYears } from "./budget-planning";

let _cache: { value: number | null } | null = null;
let _inflight: Promise<DefaultFiscalYearLoad> | null = null;

/** The read's outcome: the setting (null when unset), and whether the call itself failed. */
export interface DefaultFiscalYearLoad {
  value: number | null;
  failed: boolean;
}

/**
 * Reads the setting and says whether the call failed (PPDO-277). Never rejects. A failure is not
 * cached, so calling again — a Retry — makes a new request.
 */
export function loadDefaultFiscalYear(): Promise<DefaultFiscalYearLoad> {
  if (_cache) return Promise.resolve({ value: _cache.value, failed: false });
  if (_inflight) return _inflight;
  _inflight = getFiscalYears()
    .then((data): DefaultFiscalYearLoad => {
      _cache = { value: data.defaultFiscalYear ?? null };
      return { value: _cache.value, failed: false };
    })
    .catch((): DefaultFiscalYearLoad => ({ value: null, failed: true }))
    .finally(() => { _inflight = null; });
  return _inflight;
}

/** Resolves the setting (null when unset or unreachable). Never rejects. */
export function fetchDefaultFiscalYear(): Promise<number | null> {
  return loadDefaultFiscalYear().then((r) => r.value);
}

/**
 * Replaces the cached setting after the config page saves it (PPDO-163), so the pages this tab opens
 * next use the new year. Other tabs and other users still pick it up on their next full page load.
 */
export function setCachedDefaultFiscalYear(value: number | null): void {
  _cache = { value };
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
 * `{ ready, defaultFiscalYear, failed, retry }`. `ready` is false only until the first read settles —
 * on a warm cache it is true on the first render, so a page never waits twice.
 *
 * `failed` is true when the read settled because the call failed (PPDO-277); `defaultFiscalYear` is
 * then null, as before, so pages that fail open keep working. A page that should say so shows an
 * error with `retry`, which asks again and keeps `ready` true meanwhile (the page keeps its layout).
 *
 * ⚠️ A page whose URL already names a year must **not** wait on `ready` (spec §6.2): the link wins
 * regardless, and holding it would add a round trip to every deep link.
 */
export function useDefaultFiscalYear(): {
  ready: boolean;
  defaultFiscalYear: number | null;
  failed: boolean;
  retry: () => void;
} {
  const [state, setState] = useState(() =>
    _cache
      ? { ready: true, defaultFiscalYear: _cache.value, failed: false }
      : { ready: false, defaultFiscalYear: null, failed: false }
  );
  // Bumped by retry() to run the read again.
  const [attempt, setAttempt] = useState(0);

  useEffect(() => {
    if (state.ready && !state.failed) return;
    let live = true;
    void loadDefaultFiscalYear().then(({ value, failed }) => {
      if (live) setState({ ready: true, defaultFiscalYear: value, failed });
    });
    return () => { live = false; };
    // eslint-disable-next-line react-hooks/exhaustive-deps -- re-runs on a retry, not on each state change
  }, [attempt]);

  const retry = useCallback(() => setAttempt((n) => n + 1), []);

  return { ...state, retry };
}
