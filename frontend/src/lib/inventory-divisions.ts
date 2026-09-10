"use client";

/**
 * Shared source of the division list used by the Inventory pages.
 *
 * Problem: Create PR, PR List, and Distribution each hard-coded
 *   const DIVISIONS = ["Admin", "Planning", "RM", "MIS", "SPD"];
 * — the fixed Division enum retired in v1.2 (RAL-97). Divisions are a configurable
 * table now, and none of those five labels match the seeded division names, so the
 * backend's name → division resolution rejected every submission. Creating and
 * importing Purchase Requests was broken outright.
 *
 * Solution: read the real list from GET /api/config/divisions, scoped to PPDO's own
 * divisions (Inventory is a PPDO-internal feature; other offices' divisions must never
 * appear). Cached module-level for the browser tab session so the eight Inventory pages
 * share one request rather than each firing their own — see CLAUDE.md, "Fetch shared
 * state once", and docs/PERFORMANCE_GUIDELINES.md §8 on caching config data.
 *
 * Call clearInventoryDivisionsCache() after editing divisions in Config so the next
 * Inventory page load picks up the change.
 */

import { useEffect, useState } from "react";
import { findHostOffice, listDivisions, listOffices } from "@/lib/config";
import type { DivisionResponse } from "@/types";

let _cache: DivisionResponse[] | null = null;
let _inflight: Promise<DivisionResponse[]> | null = null;

/** Clears the cache — call after a division is added, renamed, or deactivated in Config. */
export function clearInventoryDivisionsCache(): void {
  _cache = null;
  _inflight = null;
}

/**
 * Fetches the active divisions of the host office — inventory is a host-office feature, so a
 * Purchase Request always belongs to one of its divisions (DECISION F, RAL-258).
 * - Returns a resolved promise immediately when the cache is warm.
 * - Deduplicates concurrent calls.
 *
 * Falls back to all active divisions when no office is flagged as host, so a misconfigured
 * office row degrades to "too many options" rather than an empty dropdown that blocks every
 * submission. Same fallback the backend's GetSelectableDivisionsAsync uses.
 *
 * The offices request is what identifies the host office. It runs once per cache fill, alongside
 * the divisions request rather than after it, so this costs no extra round trip.
 */
export function fetchInventoryDivisions(): Promise<DivisionResponse[]> {
  if (_cache) return Promise.resolve(_cache);
  if (_inflight) return _inflight;

  _inflight = Promise.all([listDivisions({ active: "true" }), listOffices({ active: "true" })])
    .then(([all, offices]) => {
      const hostCode = findHostOffice(offices)?.officeCode;
      const scoped = hostCode ? all.filter((d) => d.officeCode === hostCode) : [];
      _cache = scoped.length > 0 ? scoped : all;
      return _cache;
    })
    .finally(() => { _inflight = null; });

  return _inflight;
}

/**
 * Hook: the division names an Inventory page should offer.
 *
 * Staff are clamped to their own division — the backend rejects anything else
 * (PurchaseRequestService.CreateAsync), so offering more is a dead end. Admin and
 * SuperAdmin get every PPDO division.
 *
 * @param ownDivision  The current user's division name (me.division), or null.
 * @param clampToOwn   True for Staff — restrict the list to ownDivision.
 */
export function useInventoryDivisions(
  ownDivision: string | null = null,
  clampToOwn: boolean = false
): { divisions: string[]; loading: boolean } {
  const [divisions, setDivisions] = useState<string[]>(
    () => (_cache ? _cache.map((d) => d.name) : [])
  );
  const [loading, setLoading] = useState(() => _cache == null);

  useEffect(() => {
    let cancelled = false;
    fetchInventoryDivisions()
      .then((list) => {
        if (cancelled) return;
        setDivisions(list.map((d) => d.name));
      })
      .catch(() => { if (!cancelled) setDivisions([]); })
      .finally(() => { if (!cancelled) setLoading(false); });
    return () => { cancelled = true; };
  }, []);

  if (clampToOwn) {
    // Keep the user's own division even if the list has not arrived yet, so the
    // form is usable immediately and the <select> value always matches an option.
    return { divisions: ownDivision ? [ownDivision] : [], loading };
  }

  return { divisions, loading };
}
