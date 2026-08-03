/**
 * PPDO Portal — logout-reason helpers (RAL-198).
 *
 * When a silent refresh fails, the backend distinguishes *why* on the 401 body
 * (see RefreshErrorDto.cs). These helpers pull that reason out of the failed
 * axios call and build the /login redirect so the reason survives the
 * navigation, letting /login explain the logout instead of bouncing silently.
 */

import axios from "axios";
import type { RefreshErrorReason } from "@/types/auth";

const VALID_REASONS: readonly RefreshErrorReason[] = ["token_superseded", "token_expired"];

/** Extracts a known RefreshErrorReason from a failed /auth/refresh call, if present. */
export function getRefreshErrorReason(error: unknown): RefreshErrorReason | null {
  if (!axios.isAxiosError(error)) return null;
  const reason: unknown = error.response?.data?.reason;
  return VALID_REASONS.includes(reason as RefreshErrorReason) ? (reason as RefreshErrorReason) : null;
}

/** Builds the /login URL, carrying the reason as a query param when present. */
export function loginUrlWithReason(reason: RefreshErrorReason | null): string {
  return reason ? `/login?reason=${reason}` : "/login";
}
