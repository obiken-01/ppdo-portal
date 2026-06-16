/** Mirrors PPDO.Application/DTOs/Auth/ */

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  refreshToken: string;
  expiresInSeconds: number;
}

export interface RefreshRequest {
  refreshToken: string;
}

export interface MeResponse {
  userId: string;
  fullName: string;
  username: string;
  email?: string;
  /** "SuperAdmin" | "Admin" | "Staff" | "Observer" */
  role: string;
  /** "Admin" | "Planning" | "RM" | "MIS" | "SPD" — null for non-PPDO office users */
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
}
