/**
 * Self-service profile & password helpers (RAL-88).
 * All calls use the shared Axios instance (api.ts) so JWT and refresh-on-401 apply.
 */

import api from "./api";
import type { UserResponse } from "@/types";

export interface UpdateProfileRequest {
  fullName:  string;
  username:  string;
  email:     string | null;
  position:  string | null;
  contactNo: string | null;
}

export interface ChangePasswordRequest {
  currentPassword: string;
  newPassword:     string;
  confirmPassword: string;
}

/** GET /api/users/me — returns the caller's full user record. */
export async function getMyProfile(): Promise<UserResponse> {
  const { data } = await api.get<UserResponse>("/users/me");
  return data;
}

/** PUT /api/users/me — updates editable profile fields; returns the updated record. */
export async function updateMyProfile(body: UpdateProfileRequest): Promise<UserResponse> {
  const { data } = await api.put<UserResponse>("/users/me", body);
  return data;
}

/** PUT /api/users/me/password — 204 No Content on success; throws on any error. */
export async function changePassword(body: ChangePasswordRequest): Promise<void> {
  await api.put("/users/me/password", body);
}
