/**
 * Landing-page resolution for the client (RAL-263).
 *
 * The backend resolves the landing route server-side and returns it on `/auth/me` as
 * `landingPath` — walking user preference → division default → office default → first page the
 * user can actually reach → /account. Doing it there rather than here is the point: the login
 * page, the portal layout and the sidebar all used to hardcode their own version of "where should
 * this user go", and they drifted.
 *
 * This helper exists so those call sites share one expression of that, including the fallback
 * for when `me` has not loaded yet.
 */

import type { MeResponse } from "@/types";

/** Route every authenticated user can always reach. Mirrors LandingPageRoutes.Fallback. */
export const LANDING_FALLBACK = "/account";

/**
 * The route this user should land on.
 *
 * Falls back to {@link LANDING_FALLBACK} when `me` is missing or the field is empty — an older
 * backend that predates RAL-261 would omit it. /account rather than /dashboard because it is the
 * one page reachable regardless of role, office or permissions; office users cannot open
 * /dashboard at all.
 */
export function resolveLandingPath(me: Pick<MeResponse, "landingPath"> | null | undefined): string {
  const path = me?.landingPath?.trim();
  return path && path.startsWith("/") ? path : LANDING_FALLBACK;
}
