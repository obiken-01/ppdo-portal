/**
 * PPDO Portal — Token storage & auth helpers.
 *
 * Token storage strategy (RAL-58):
 *   Access token  → in-memory only (_accessToken). Cleared on page reload —
 *                   intentional; silent refresh restores it from the cookie.
 *   Refresh token → httpOnly, Secure, SameSite=Strict cookie set by the backend on
 *                   /auth/login and /auth/refresh (scoped to /api/auth/refresh). It is
 *                   NOT accessible to JavaScript by design — there is nothing to read,
 *                   store, or clear here. The browser sends it automatically on refresh
 *                   (axios is configured with withCredentials: true).
 *
 * Never store tokens in localStorage or sessionStorage where injected third-party
 * scripts could read them. (The "ppdo_had_session" flag in lib/session-restore.ts is the one
 * localStorage key — it holds "1", not a token. PPDO-238.)
 */

import type { LoginResponse } from "@/types/auth";
import { clearSessionFlag, setSessionFlag } from "./session-restore";

// ---------------------------------------------------------------------------
// In-memory access token
// ---------------------------------------------------------------------------

let _accessToken: string | null = null;

// ---------------------------------------------------------------------------
// Public auth object
// ---------------------------------------------------------------------------

export const auth = {
  getToken: (): string | null => _accessToken,

  setToken: (token: string): void => {
    _accessToken = token;
  },

  clearToken: (): void => {
    _accessToken = null;
  },

  isAuthenticated: (): boolean => _accessToken !== null,

  /**
   * Stores the access token after a successful login or refresh.
   * Call with the raw server response body.
   * Also marks this browser as "has signed in before", so /login tries a quiet restore (PPDO-238).
   */
  login: (data: LoginResponse): void => {
    _accessToken = data.accessToken;
    setSessionFlag();
  },

  /**
   * Clears the in-memory access token. The refresh cookie is cleared server-side
   * by POST /auth/logout.
   * Also clears the "has signed in before" flag — called on sign-out and on every definitive
   * refresh 401, never on a network error (PPDO-238).
   */
  logout: (): void => {
    _accessToken = null;
    clearSessionFlag();
  },
};
