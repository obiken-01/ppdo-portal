"use client";

/**
 * Module-level /auth/me cache.
 *
 * Problem: every portal page independently calls api.get("/auth/me") and gates
 * its data fetch behind the result, creating a 300ms waterfall on every page
 * visit (auth/me → data load → table appears).
 *
 * Solution:
 *   fetchMe() — deduplicates in-flight requests and caches the result for the
 *               browser tab session. Return visits get the response synchronously
 *               (zero network cost). First visits get it in parallel with data loads.
 *
 *   useMe()   — React hook that initialises synchronously from cache on return
 *               visits (no loading state) and redirects if the user lacks the
 *               required permission.
 *
 * Call clearMeCache() on logout so the next login starts fresh.
 */

import { useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import api from "./api";
import type { MeResponse } from "@/types";

let _cache: MeResponse | null = null;
let _inflight: Promise<MeResponse> | null = null;

/** Clears the cache — call on logout or when the user's profile changes. */
export function clearMeCache(): void {
  _cache = null;
  _inflight = null;
}

/**
 * Fetches /auth/me.
 * - Returns a resolved promise immediately when the cache is warm.
 * - Deduplicates concurrent calls (only one in-flight request at a time).
 */
export function fetchMe(): Promise<MeResponse> {
  if (_cache) return Promise.resolve(_cache);
  if (_inflight) return _inflight;
  _inflight = api
    .get<MeResponse>("/auth/me")
    .then(({ data }) => { _cache = data; return data; })
    .finally(() => { _inflight = null; });
  return _inflight;
}

/**
 * Hook: resolves /auth/me (cached), redirects if the permission check fails.
 *
 * Returns MeResponse | null.
 * - On return visits (cache warm): returns MeResponse synchronously — no loading state.
 * - On first visit: returns null until auth/me resolves (~300ms), during which
 *   the page's own data loads should already be in flight.
 *
 * @param requirePermission  Return true when the user may access this page.
 * @param redirectTo         Path to replace() when permission fails (fn or string).
 */
export function useMe(
  requirePermission: (me: MeResponse) => boolean,
  redirectTo: string | ((me: MeResponse) => string) = "/dashboard"
): MeResponse | null {
  const router = useRouter();

  const [me, setMe] = useState<MeResponse | null>(() => {
    if (_cache && requirePermission(_cache)) return _cache;
    return null;
  });

  useEffect(() => {
    let cancelled = false;
    fetchMe()
      .then((data) => {
        if (cancelled) return;
        if (!requirePermission(data)) {
          const url = typeof redirectTo === "function" ? redirectTo(data) : redirectTo;
          router.replace(url);
          return;
        }
        setMe(data);
      })
      .catch(() => { if (!cancelled) router.replace("/login"); });
    return () => { cancelled = true; };
  }, []); // eslint-disable-line react-hooks/exhaustive-deps

  return me;
}
