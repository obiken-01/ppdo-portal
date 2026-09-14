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
 * reader's own submit / return / accept / re-open. A hand-off by someone else appears on the next load.
 */

import { useEffect, useState } from "react";
import api from "./api";
import type { ApiResponse, AipReviewNotifications, MeResponse } from "@/types";

type Listener = (value: AipReviewNotifications | null) => void;

let _userId: string | null = null;
let _value: AipReviewNotifications | null = null;
let _loaded = false;
let _inflight: Promise<void> | null = null;
const _listeners = new Set<Listener>();

function publish() {
  _listeners.forEach((listener) => listener(_value));
}

function load(userId: string): Promise<void> {
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
  _inflight = run.finally(() => {
    if (_inflight === run) _inflight = null;
  });
  return _inflight;
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

  return userId != null ? value : null;
}

/**
 * The sidebar count and where clicking it goes, or null at zero — nothing is shown then, never a 0.
 *
 * A PPDO reviewer (or a holder of both flags) goes to the search with "waiting on me" applied; a
 * department head alone goes to their own office's AIP Entry, where the submit panel is.
 */
export function pendingLink(n: AipReviewNotifications): { count: number; href: string } | null {
  const count = n.pendingForPpdo + n.pendingForDepartmentHead;
  if (count === 0) return null;
  if (n.pendingForPpdo > 0) {
    return {
      count,
      href: `/budget-planning/aip/review/search?mine=true&fiscalYear=${n.ppdoFiscalYear}`,
    };
  }
  return { count, href: `/budget-planning/aip/entry?fiscalYear=${n.departmentHeadFiscalYear}` };
}
