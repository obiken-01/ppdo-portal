/** Mirrors PPDO.Application/DTOs/User/ */

export type UserRole = "SuperAdmin" | "Admin" | "Staff" | "Observer";
export type Division = "Admin" | "Planning" | "RM" | "MIS" | "SPD";

export interface UserResponse {
  id: string;
  fullName: string;
  username: string;
  email?: string;
  role: UserRole;
  division: Division | null;
  /** Provincial office (v1.1) — set for non-PPDO office users. */
  officeId: number | null;
  officeName: string | null;
  groupId: string | null;
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
}

export interface CreateUserRequest {
  fullName: string;
  username: string;
  email?: string;
  role: UserRole;
  division: Division | null;
  /** Set to create a non-PPDO office user (Division is then ignored). */
  officeId: number | null;
  position: string | null;
  contactNo: string | null;
  // groupId intentionally omitted — backend auto-assigns from Role + Division/office
}

export interface UpdateUserRequest extends CreateUserRequest {
  groupId: string | null;
  isActive: boolean;
  overrideCanAccessInventory: boolean | null;
  overrideCanAccessReports: boolean | null;
  overrideCanManageUsers: boolean | null;
  overrideCanManageResourceLinks: boolean | null;
  overrideCanAccessBudgetPlanning: boolean | null;
  overrideCanUploadAip: boolean | null;
  overrideCanManageConfig: boolean | null;
}

export interface PermissionGroupResponse {
  id: string;
  name: string;
  division: Division | null;
  canAccessInventory: boolean;
  canAccessReports: boolean;
  canManageUsers: boolean;
  canManageResourceLinks: boolean;
  canAccessBudgetPlanning: boolean;
  canUploadAip: boolean;
  canManageConfig: boolean;
}

// OfficeResponse moved to ./config.ts (RAL-73) — re-exported via the @/types barrel.
