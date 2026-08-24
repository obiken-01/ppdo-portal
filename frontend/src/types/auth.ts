/** Mirrors PPDO.Application/DTOs/Auth/ */

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresInSeconds: number;
}

/**
 * Reason for a failed POST /auth/refresh (RAL-198), mirrors
 * PPDO.Application/DTOs/Auth/RefreshErrorDto.cs.
 *   token_superseded — a later login/refresh (elsewhere, or another device/tab)
 *                       overwrote this refresh token.
 *   token_expired    — the token matched but is past its natural expiry.
 */
export type RefreshErrorReason = "token_superseded" | "token_expired";

export interface RefreshErrorResponse {
  reason: RefreshErrorReason;
}

/**
 * Landing-page targets (RAL-251). Mirrors the backend `LandingPage` enum by NAME —
 * the API exchanges names, not the underlying numbers.
 */
export type LandingPageKey =
  | "MainDashboard"
  | "InventoryDashboard"
  | "BudgetPlanningDashboard"
  | "Profile";

export interface MeResponse {
  /**
   * Portal route this user should land on, resolved server-side (RAL-261).
   * Always a route they can actually reach — safe to redirect to without checking.
   */
  landingPath: string;

  /** This user's own stored preference, or null when unset. What /account shows. */
  landingPage: LandingPageKey | null;
  userId: string;
  fullName: string;
  username: string;
  email?: string;
  /** "SuperAdmin" | "Admin" | "Staff" */
  role: string;
  /** Configurable division id (divisions.id). Null for SuperAdmin/Admin. */
  divisionId: number | null;
  /** Division name. Null for SuperAdmin/Admin. */
  division: string | null;
  /** Provincial office id, or null for PPDO-internal users (the PPDO discriminator). */
  officeId: number | null;
  /** Short office code, e.g. "PEO". Null for PPDO-internal users. */
  officeCode: string | null;
  /** Full office name. Null for PPDO-internal users. */
  officeName: string | null;
  position?: string | null;
  canAccessInventory: boolean;
  canAccessReports: boolean;
  canManageUsers: boolean;
  canAccessProfile: boolean;
  canManageResourceLinks: boolean;
  canAccessBudgetPlanning: boolean;
  canUploadAip: boolean;
  canManageConfig: boolean;
  canManageAllocation: boolean;
}
