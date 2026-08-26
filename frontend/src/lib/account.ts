/**
 * Self-service profile & password helpers (RAL-88).
 * All calls use the shared Axios instance (api.ts) so JWT and refresh-on-401 apply.
 */

import api from "./api";
import type { LandingPageKey, RecoveryQuestionOption, SetRecoveryAnswerRequest, UserResponse } from "@/types";

export interface UpdateProfileRequest {
  fullName:  string;
  username:  string;
  email:     string | null;
  position:  string | null;
  contactNo: string | null;
  /** Preferred landing page, or null for none (RAL-262). */
  landingPage: LandingPageKey | null;
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

/** GET /api/auth/recovery-questions — the fixed catalog, for the setup screen (RAL-266). */
export async function getRecoveryQuestionOptions(): Promise<RecoveryQuestionOption[]> {
  const { data } = await api.get<RecoveryQuestionOption[]>("/auth/recovery-questions");
  return data;
}

/** PUT /api/users/me/recovery-answer — 204 No Content on success; throws on any error (RAL-266). */
export async function setRecoveryAnswer(body: SetRecoveryAnswerRequest): Promise<void> {
  await api.put("/users/me/recovery-answer", body);
}

/** POST /api/users/me/acknowledge-password-reset — dismisses the reset notice (RAL-267). */
export async function acknowledgePasswordReset(): Promise<void> {
  await api.post("/users/me/acknowledge-password-reset");
}
