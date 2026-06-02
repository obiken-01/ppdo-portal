/** Mirrors PPDO.Application/DTOs/User/ */

export type UserRole = "SuperAdmin" | "Admin" | "Staff" | "Observer";
export type Division = "Admin" | "Planning" | "RM" | "MIS" | "SPD";

export interface UserResponse {
  id: string;
  fullName: string;
  email: string;
  role: UserRole;
  division: Division | null;
  groupId: string | null;
  position: string | null;
  contactNo: string | null;
  isActive: boolean;
  createdAt: string;
  overrideCanAccessInventory: boolean | null;
  overrideCanAccessReports: boolean | null;
  overrideCanManageUsers: boolean | null;
  overrideCanManageResourceLinks: boolean | null;
}

export interface CreateUserRequest {
  fullName: string;
  email: string;
  role: UserRole;
  division: Division | null;
  position: string | null;
  contactNo: string | null;
  // groupId intentionally omitted — backend auto-assigns from Role + Division
}

export interface UpdateUserRequest extends CreateUserRequest {
  groupId: string | null;
  isActive: boolean;
  overrideCanAccessInventory: boolean | null;
  overrideCanAccessReports: boolean | null;
  overrideCanManageUsers: boolean | null;
  overrideCanManageResourceLinks: boolean | null;
}

export interface PermissionGroupResponse {
  id: string;
  name: string;
  division: Division | null;
  canAccessInventory: boolean;
  canAccessReports: boolean;
  canManageUsers: boolean;
  canManageResourceLinks: boolean;
}
