/** Mirrors PPDO.Application/DTOs/Auth/ */

export interface LoginRequest {
  username: string;
  password: string;
}

export interface LoginResponse {
  accessToken: string;
  expiresInSeconds: number;
}

export interface MeResponse {
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
