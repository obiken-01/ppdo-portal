/** Mirrors PPDO.Application/DTOs/User/ */

// Observer retired in v1.2 (RAL-97).
export type UserRole = "SuperAdmin" | "Admin" | "Staff";

/**
 * Division is a configurable record now (v1.2 — RAL-97), no longer a fixed enum.
 * The user form fetches the list from GET /api/config/divisions (see DivisionResponse in ./config).
 */

export interface UserResponse {
  id: string;
  fullName: string;
  username: string;
  email?: string;
  role: UserRole;
  /** Configurable division id — carries scope + feature flags. Null for SuperAdmin/Admin. */
  divisionId: number | null;
  /** Division name (display only). */
  division: string | null;
  /** Provincial office (v1.1) — set for non-PPDO office users. */
  officeId: number | null;
  officeName: string | null;
  position: string | null;
  contactNo: string | null;
  isActive: boolean;
  createdAt: string;
  overrideCanAccessInventory: boolean | null;
  overrideCanAccessReports: boolean | null;
  overrideCanManageUsers: boolean | null;
  overrideCanManageResourceLinks: boolean | null;
  overrideCanAccessBudgetPlanning: boolean | null;
  overrideCanUploadAip: boolean | null;
  overrideCanManageConfig: boolean | null;
  overrideCanManageAllocation: boolean | null;
}

export interface CreateUserRequest {
  fullName: string;
  username: string;
  email?: string;
  role: UserRole;
  /** Configurable division id — required for Staff, null for SuperAdmin/Admin. */
  divisionId: number | null;
  /** Set to create a non-PPDO office user (its division must belong to that office). */
  officeId: number | null;
  position: string | null;
  contactNo: string | null;
}

export interface UpdateUserRequest extends CreateUserRequest {
  isActive: boolean;
  overrideCanAccessInventory: boolean | null;
  overrideCanAccessReports: boolean | null;
  overrideCanManageUsers: boolean | null;
  overrideCanManageResourceLinks: boolean | null;
  overrideCanAccessBudgetPlanning: boolean | null;
  overrideCanUploadAip: boolean | null;
  overrideCanManageConfig: boolean | null;
  overrideCanManageAllocation: boolean | null;
}

// OfficeResponse / DivisionResponse live in ./config.ts — re-exported via the @/types barrel.
