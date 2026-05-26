/**
 * PPDO Portal — Token storage & auth helpers
 *
 * Access tokens are stored IN MEMORY ONLY (never localStorage/sessionStorage)
 * to mitigate XSS. The refresh token lives in an httpOnly cookie managed
 * by the server — the client never reads it directly.
 *
 * TODO RAL-xx: Implement once JWT auth endpoints (RAL-xx) are complete.
 */

// In-memory access token store (cleared on page reload — intentional)
let _accessToken: string | null = null;

export const auth = {
  getToken: (): string | null => _accessToken,
  setToken: (token: string): void => {
    _accessToken = token;
  },
  clearToken: (): void => {
    _accessToken = null;
  },
  isAuthenticated: (): boolean => _accessToken !== null,
};
