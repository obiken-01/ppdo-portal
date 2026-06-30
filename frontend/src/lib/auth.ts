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
 * scripts could read them.
 */

import type { LoginResponse } from "@/types/auth";

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
   */
  login: (data: LoginResponse): void => {
    _accessToken = data.accessToken;
  },

  /**
   * Clears the in-memory access token. The refresh cookie is cleared server-side
   * by POST /auth/logout.
   */
  logout: (): void => {
    _accessToken = null;
  },
};
