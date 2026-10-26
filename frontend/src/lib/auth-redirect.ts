/**
 * PPDO Portal — failed-refresh classification & redirect helpers (RAL-198, RAL-199).
 *
 * A failed POST /auth/refresh means one of two very different things:
 *
 *   - The server answered "no" (a definitive HTTP response — see RefreshErrorDto.cs
 *     for the token_superseded / token_expired reasons). Retrying is pointless; the
 *     refresh cookie has been proven invalid. → "unauthorized", explain and send to
 *     /login (RAL-198).
 *
 *   - The server never answered at all (network error / timeout — Azure Functions
 *     cold start, Azure SQL Free auto-pause resume). The refresh cookie was never
 *     proven invalid, so retrying the *same* call once the backend wakes up can
 *     still succeed with no re-entered credentials. → "unreachable", send to
 *     /reconnecting instead of forcing a logout (RAL-199).
 */

import axios from "axios";
import type { RefreshErrorReason } from "@/types/auth";

/**
 * Request timeout for POST /auth/refresh calls (api.ts interceptor, portal
 * layout.tsx guard, /reconnecting auto-retry). Without this, a slow-but-alive
 * cold start (Azure Functions ~5-20s, Azure SQL Free auto-pause resume up to
 * ~1 min) never rejects the axios promise — it just hangs, so isNetworkError()
 * below never fires and the /reconnecting redirect never triggers. Set below
 * the /health check's 30s timeout so a genuinely stuck backend gets classified
 * as unreachable (→ /reconnecting, which retries with its own backoff) instead
 * of leaving the UI frozen with no explanation.
 */
export const REFRESH_TIMEOUT_MS = 20_000;

const VALID_REASONS: readonly RefreshErrorReason[] = ["token_superseded", "token_expired"];

/** Extracts a known RefreshErrorReason from a failed /auth/refresh call, if present. */
export function getRefreshErrorReason(error: unknown): RefreshErrorReason | null {
  if (!axios.isAxiosError(error)) return null;
  const reason: unknown = error.response?.data?.reason;
  return VALID_REASONS.includes(reason as RefreshErrorReason) ? (reason as RefreshErrorReason) : null;
}

/** True when the refresh call never got an HTTP response at all (cold start / timeout). */
export function isNetworkError(error: unknown): boolean {
  return axios.isAxiosError(error) && !error.response;
}

export type RefreshFailure =
  | { kind: "unreachable" }
  | { kind: "unauthorized"; reason: RefreshErrorReason | null };

/** Classifies a failed refresh call as "unreachable" (retry-worthy) or "unauthorized" (terminal). */
export function classifyRefreshFailure(error: unknown): RefreshFailure {
  if (isNetworkError(error)) return { kind: "unreachable" };
  return { kind: "unauthorized", reason: getRefreshErrorReason(error) };
}

/** Builds the /login URL, carrying the reason as a query param when present. */
export function loginUrlWithReason(reason: RefreshErrorReason | null): string {
  return reason ? `/login?reason=${reason}` : "/login";
}

/** Builds the /reconnecting URL, carrying the page the user was trying to reach. */
export function reconnectingUrl(next: string): string {
  return `/reconnecting?next=${encodeURIComponent(next)}`;
}

/**
 * Where a portal page sends the user when its fetchMe() fails (PPDO-260).
 *
 * Same split as a failed refresh: no HTTP response at all (cold start, restart, timeout)
 * goes to /reconnecting, which retries and comes back to `currentPath`; any answer from
 * the server goes to /login as before. A 401 never gets this far in practice, because
 * the api.ts interceptor handles it first.
 *
 * Loop guard: if /auth/refresh works but /auth/me keeps failing (a CORS misconfiguration
 * on GET looks exactly like a network error), /reconnecting succeeds and sends the user
 * straight back, the page fails again, and so on, hundreds of times a second. So a second
 * network failure within ME_RECONNECT_COOLDOWN_MS of the last trip to /reconnecting
 * (`lastReconnectAt`) falls back to /login.
 */
export const ME_RECONNECT_COOLDOWN_MS = 30_000;

export function meFailureRedirect(
  error: unknown,
  currentPath: string,
  lastReconnectAt: number | null = null,
  now: number = Date.now(),
): string {
  if (!isNetworkError(error)) return "/login";
  if (lastReconnectAt !== null && now - lastReconnectAt < ME_RECONNECT_COOLDOWN_MS) return "/login";
  return reconnectingUrl(currentPath);
}
