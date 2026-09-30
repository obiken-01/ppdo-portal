"use client";

/**
 * In-app notifications — the sidebar's pending count and the "returned" notice (V18-58 / PPDO-75,
 * `docs/v1.8/AIP_Review_Spec.md` §6.5).
 *
 * ⚠️ **One module-level store, read everywhere.** The sidebar and AIP Entry both need this, and the
 * sidebar is on every portal page — a fetch per component is the `/auth/me`-four-times-a-load
 * mistake the WFP page once made. Same shape as `me-cache.ts`: fetched once, deduplicated, shared.
 *
 * ⚠️ **Keyed by user id.** Signing in as someone else in the same tab must never show the previous
 * person's count, and the logout paths are several; a stale key simply refetches.
 *
 * ⚠️ **No polling.** Fetched once per portal load and again, via `refreshAipNotifications`, after the
 * reader's own submit / return / accept / re-open.
 *
 * ↩️ **Plus on tab focus and on page change, at most every 30 s (PPDO-168).** A hand-off by someone
 * ELSE — a department head returning a division, an encoder submitting one — used to appear only on
 * a full reload, because this store outlives client-side navigation. The reader coming back to the
 * tab, or moving to another page, is exactly when a stale badge would mislead them, and it costs one
 * small request at a moment they are looking. Still no background timer.
 */

import { useEffect, useState } from "react";
import { usePathname } from "next/navigation";
import api from "./api";
import type { ApiResponse, AipReviewNotifications, MeResponse } from "@/types";

type Listener = (value: AipReviewNotifications | null) => void;

/** PPDO-168 — a focus or page change refetches only when the last fetch is older than this. */
const STALE_AFTER_MS = 30_000;

let _userId: string | null = null;
let _value: AipReviewNotifications | null = null;
let _loaded = false;
let _inflight: Promise<void> | null = null;
let _fetchedAt = 0;
const _listeners = new Set<Listener>();

function publish() {
  _listeners.forEach((listener) => listener(_value));
}

function load(userId: string): Promise<void> {
  _fetchedAt = Date.now();
  const run = api
    .get<ApiResponse<AipReviewNotifications>>("/budget-planning/aip/review/notifications")
    .then(({ data }) => {
      if (_userId === userId) _value = data.data ?? null;
    })
    // A failed read shows no badge. The sidebar is not the place for an error, and every page the
    // badge points at still works without it.
    .catch(() => {
      if (_userId === userId) _value = null;
    })
    .finally(() => {
      if (_userId === userId) {
        _loaded = true;
        publish();
      }
    });
  // ↩️ PPDO-168 — compared against `tracked`, the promise actually stored. It used to compare against
  // `run`, which `_inflight` never holds, so the marker was never cleared after the first fetch — and
  // every later "is one in flight?" check (the focus / page-change refresh) answered yes forever.
  const tracked: Promise<void> = run.finally(() => {
    if (_inflight === tracked) _inflight = null;
  });
  _inflight = tracked;
  return tracked;
}

function ensure(userId: string) {
  if (_userId !== userId) {
    _userId = userId;
    _value = null;
    _loaded = false;
    _inflight = null;
    publish();
  }
  if (_loaded || _inflight) return;
  void load(userId);
}

/** Refetch after the reader's own review action — the badge should move without a reload. */
export function refreshAipNotifications(): Promise<void> {
  if (!_userId) return Promise.resolve();
  return load(_userId);
}

/**
 * PPDO-168 — refetch if the last read is older than {@link STALE_AFTER_MS} and none is in flight.
 * Several components use the hook, so several listeners may call this at once; the age and the
 * in-flight check make that one request.
 */
function refreshIfStale() {
  if (!_userId || !_loaded || _inflight) return;
  if (Date.now() - _fetchedAt < STALE_AFTER_MS) return;
  void load(_userId);
}

/**
 * The caller's notifications, or null while loading, on failure, or for a user who cannot open
 * Budget Planning (who is never asked — the endpoint would 403).
 */
export function useAipNotifications(me: MeResponse | null): AipReviewNotifications | null {
  const userId = me?.canAccessBudgetPlanning === true ? me.userId : null;
  const [value, setValue] = useState<AipReviewNotifications | null>(() =>
    userId != null && _userId === userId ? _value : null
  );

  useEffect(() => {
    if (userId == null) {
      setValue(null);
      return;
    }
    const listener: Listener = (next) => setValue(next);
    _listeners.add(listener);
    ensure(userId);
    setValue(_userId === userId ? _value : null);
    return () => {
      _listeners.delete(listener);
    };
  }, [userId]);

  // PPDO-168 — back to the tab: pick up hand-offs made elsewhere while it was in the background.
  useEffect(() => {
    if (userId == null) return;
    const onVisible = () => {
      if (document.visibilityState === "visible") refreshIfStale();
    };
    window.addEventListener("focus", refreshIfStale);
    document.addEventListener("visibilitychange", onVisible);
    return () => {
      window.removeEventListener("focus", refreshIfStale);
      document.removeEventListener("visibilitychange", onVisible);
    };
  }, [userId]);

  // PPDO-168 — and on every page change, which this store otherwise outlives.
  const pathname = usePathname();
  useEffect(() => {
    if (userId != null) refreshIfStale();
  }, [pathname, userId]);

  return userId != null ? value : null;
}

/**
 * The sidebar count and where clicking it goes, or null at zero — nothing is shown then, never a 0.
 *
 * A PPDO reviewer (or a holder of both flags) goes to the search with "waiting on me" applied; a
 * department head alone goes to their own office's AIP Entry, where the submit panel is.
 */
export function pendingLink(
  n: AipReviewNotifications,
): { count: number; href: string; title: string } | null {
  // PPDO-152 — a division that has handed its work up is waiting on the department head too.
  const divisions = n.divisionsSubmitted ?? 0;
  const offices = n.pendingForPpdo + n.pendingForDepartmentHead;
  const count = offices + divisions;
  if (count === 0) return null;
  // ⚠️ Names what is waiting. The count now mixes offices and divisions, and "3 offices are
  // waiting" over one office and two divisions would send the reader looking for offices.
  const parts: string[] = [];
  if (offices > 0) parts.push(`${offices} ${offices === 1 ? "office" : "offices"}`);
  if (divisions > 0) parts.push(`${divisions} ${divisions === 1 ? "division" : "divisions"}`);
  const title = `${parts.join(" and ")} waiting on you`;
  if (n.pendingForPpdo > 0) {
    return {
      count,
      title,
      href: `/budget-planning/aip/review/search?mine=true&fiscalYear=${n.ppdoFiscalYear}`,
    };
  }
  return { count, title, href: `/budget-planning/aip/entry?fiscalYear=${n.departmentHeadFiscalYear}` };
}
