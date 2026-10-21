/**
 * PPDO Portal — "this browser has signed in before" flag (PPDO-238).
 *
 * /login uses it to decide whether a quiet POST /auth/refresh is worth trying: a browser that
 * signed in recently may still hold a valid 7-day refresh cookie, which JavaScript cannot see
 * (it is httpOnly). First-time visitors have no flag, so they make no wasted call.
 *
 * This is NOT a token and holds nothing secret — just "1". lib/auth.ts's rule ("never store
 * tokens in localStorage") still holds: the refresh token stays in its httpOnly cookie and the
 * access token stays in memory. Reading the flag proves nothing; the server still decides.
 *
 * Set in auth.login() (every login and refresh success), cleared in auth.logout() (sign-out and
 * every definitive refresh 401). Network errors never call auth.logout(), so a cold start keeps it.
 *
 * Every storage access is wrapped: private windows and blocked site data throw, and a throw
 * reads as "no flag".
 */

export const SESSION_FLAG_KEY = "ppdo_had_session";

export function hasSessionFlag(): boolean {
  try {
    return localStorage.getItem(SESSION_FLAG_KEY) !== null;
  } catch {
    return false;
  }
}

export function setSessionFlag(): void {
  try {
    localStorage.setItem(SESSION_FLAG_KEY, "1");
  } catch {
    // Storage unavailable — /login just shows the form next time.
  }
}

export function clearSessionFlag(): void {
  try {
    localStorage.removeItem(SESSION_FLAG_KEY);
  } catch {
    // Storage unavailable — nothing was stored.
  }
}

/**
 * Try the quiet restore only when this browser signed in before AND /login was not opened
 * with a ?reason= (signed in elsewhere / expired) — that banner must still show.
 */
export function shouldTryRestore({ hasFlag, reasonParam }: { hasFlag: boolean; reasonParam: string | null }): boolean {
  return hasFlag && reasonParam === null;
}
