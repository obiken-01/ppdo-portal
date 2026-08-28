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

/** Mirrors PPDO.Application/DTOs/Auth/ForgotPasswordRequestDto.cs and ...ResponseDto.cs (RAL-265). */
export interface ForgotPasswordRequest {
  username: string;
}

export interface ForgotPasswordResponse {
  /** Always present — an unknown username or an account with no question set both fall back
   * to the same catalog default, so this response never reveals whether the account exists. */
  questionText: string;
}

/** Mirrors PPDO.Application/DTOs/Auth/VerifyRecoveryRequestDto.cs and ...ResponseDto.cs (RAL-265). */
export interface VerifyRecoveryRequest {
  username: string;
  answer: string;
}

export interface VerifyRecoveryResponse {
  /** Shown once — never persisted client-side beyond the confirmation screen. */
  temporaryPassword: string;
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
  /**
   * Provincial office id. Every user has one as of RAL-258 — it is no longer the PPDO
   * discriminator, and a null here now means an incomplete record rather than full access.
   * Use {@link isHostOffice} to ask whether a user holds cross-office authority.
   */
  officeId: number | null;
  /** Short office code, e.g. "PEO". */
  officeCode: string | null;
  /** Full office name. */
  officeName: string | null;
  /**
   * Whether this user belongs to the host office — PPDO today (DECISION F, RAL-258).
   *
   * The single client-side answer to "can this user see every office's data?". It replaces
   * `officeId == null`, which meant the same thing by proxy until every user gained an office,
   * and the `officeCode === "PPDO"` string comparisons that had to agree with it by hand.
   */
  isHostOffice: boolean;
  position?: string | null;
  canAccessInventory: boolean;
  canAccessReports: boolean;
  canManageUsers: boolean;
  canAccessProfile: boolean;
  canManageResourceLinks: boolean;
  canAccessBudgetPlanning: boolean;
  canUploadAip: boolean;
  canManageConfig: boolean;
  canManagePpdoAllocation: boolean;
  /** PBO finance officer: may set ANY office's budget ceiling (RAL-243). Separate
   *  authority from canManagePpdoAllocation — neither implies the other. */
  canManagePboCeiling: boolean;

  /** True after a reset — the portal blocks everything except changing the password (RAL-266). */
  mustChangePassword: boolean;
  /** True when no recovery question is set yet — the portal blocks everything except setup (RAL-266). */
  needsRecoverySetup: boolean;
  /**
   * ISO timestamp of the most recent reset, only when not yet acknowledged. Null means
   * nothing to show. Non-blocking — rendered as a dismissible banner (RAL-267).
   */
  unacknowledgedPasswordResetAt: string | null;
}

/** Mirrors PPDO.Application/DTOs/Auth/RecoveryQuestionOptionDto.cs (RAL-266). */
export interface RecoveryQuestionOption {
  /** Enum name, e.g. "BirthTown" — the same value SetRecoveryAnswerRequest.questionKey expects. */
  key: string;
  text: string;
}

/** Mirrors PPDO.Application/DTOs/Users/SetRecoveryAnswerDto.cs (RAL-266). */
export interface SetRecoveryAnswerRequest {
  questionKey: string;
  answer: string;
}
