/**
 * PPDO Portal — Token storage & auth helpers.
 *
 * Token storage strategy (Section 7 of PROJECT_DOCUMENTATION_NET_AZURE.md):
 *   Access token  → in-memory only (_accessToken).
 *                   Cleared on page reload — intentional; use refresh to restore.
 *   Refresh token → localStorage (key: "ppdo_rt").
 *                   Persisted across reloads so silent refresh can restore
 *                   the access token without sending the user to /login.
 *                   Note: move to an httpOnly cookie set by the server when
 *                   the backend is updated to set Set-Cookie on /auth/login.
 *
 * Never store tokens in sessionStorage or anywhere the token could be read
 * by injected third-party scripts.
 */

import type { LoginResponse } from "@/types/auth";

const REFRESH_TOKEN_KEY = "ppdo_rt";

// ---------------------------------------------------------------------------
// In-memory access token
// ---------------------------------------------------------------------------

let _accessToken: string | null = null;

// ---------------------------------------------------------------------------
// Public auth object
// ---------------------------------------------------------------------------

export const auth = {
  // -- Access token (in-memory) -------------------------------------------

  getToken: (): string | null => _accessToken,

  setToken: (token: string): void => {
    _accessToken = token;
  },

  clearToken: (): void => {
    _accessToken = null;
  },

  isAuthenticated: (): boolean => _accessToken !== null,

  // -- Refresh token (localStorage) ----------------------------------------

  getRefreshToken: (): string | null => {
    if (typeof window === "undefined") return null;
    return localStorage.getItem(REFRESH_TOKEN_KEY);
  },

  setRefreshToken: (token: string): void => {
    if (typeof window === "undefined") return;
    localStorage.setItem(REFRESH_TOKEN_KEY, token);
  },

  clearRefreshToken: (): void => {
    if (typeof window === "undefined") return;
    localStorage.removeItem(REFRESH_TOKEN_KEY);
  },

  // -- Convenience helpers -------------------------------------------------

  /**
   * Stores both tokens after a successful login or refresh.
   * Call with the raw server response body.
   */
  login: (data: LoginResponse): void => {
    _accessToken = data.accessToken;
    if (typeof window !== "undefined") {
      localStorage.setItem(REFRESH_TOKEN_KEY, data.refreshToken);
    }
  },

  /**
   * Clears both tokens. Call on logout or when refresh fails.
   */
  logout: (): void => {
    _accessToken = null;
    if (typeof window !== "undefined") {
      localStorage.removeItem(REFRESH_TOKEN_KEY);
    }
  },
};
