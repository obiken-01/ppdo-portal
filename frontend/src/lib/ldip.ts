/**
 * LDIP Budget Planning API helpers (RAL-75).
 *
 * All endpoints use the { data, error, message } envelope.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type {
  ApiResponse,
  CreateLdipRequest,
  LdipRecord,
  LdipStatus,
  UpdateLdipRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Envelope helpers
// ---------------------------------------------------------------------------

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) {
    throw new Error(body.error ?? "Unexpected empty response.");
  }
  return body.data;
}

export function ldipErrorMessage(err: unknown, fallback: string): string {
  const body = (err as { response?: { data?: ApiResponse<unknown> } })?.response?.data;
  return body?.error ?? body?.message ?? fallback;
}

// ---------------------------------------------------------------------------
// List — GET /api/budget-planning/ldip?status=
// ---------------------------------------------------------------------------

export async function listLdip(
  params: { status?: LdipStatus | "" } = {}
): Promise<LdipRecord[]> {
  const query: Record<string, string> = {};
  if (params.status) query.status = params.status;
  const { data } = await api.get<ApiResponse<LdipRecord[]>>("/budget-planning/ldip", {
    params: query,
  });
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Get by ID — GET /api/budget-planning/ldip/{id}
// ---------------------------------------------------------------------------

export async function getLdipById(id: number): Promise<LdipRecord> {
  const { data } = await api.get<ApiResponse<LdipRecord>>(`/budget-planning/ldip/${id}`);
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Create — POST /api/budget-planning/ldip
// ---------------------------------------------------------------------------

export async function createLdip(body: CreateLdipRequest): Promise<LdipRecord> {
  const { data } = await api.post<ApiResponse<LdipRecord>>("/budget-planning/ldip", body);
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Update — PUT /api/budget-planning/ldip/{id}
// ---------------------------------------------------------------------------

export async function updateLdip(id: number, body: UpdateLdipRequest): Promise<LdipRecord> {
  const { data } = await api.put<ApiResponse<LdipRecord>>(`/budget-planning/ldip/${id}`, body);
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Finalize — POST /api/budget-planning/ldip/{id}/finalize
// ---------------------------------------------------------------------------

export async function finalizeLdip(id: number): Promise<LdipRecord> {
  const { data } = await api.post<ApiResponse<LdipRecord>>(
    `/budget-planning/ldip/${id}/finalize`
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Unlock — POST /api/budget-planning/ldip/{id}/unlock  (admin only)
// ---------------------------------------------------------------------------

export async function unlockLdip(id: number): Promise<LdipRecord> {
  const { data } = await api.post<ApiResponse<LdipRecord>>(
    `/budget-planning/ldip/${id}/unlock`
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Archive — DELETE /api/budget-planning/ldip/{id}
// ---------------------------------------------------------------------------

export async function archiveLdip(id: number): Promise<LdipRecord> {
  const { data } = await api.delete<ApiResponse<LdipRecord>>(`/budget-planning/ldip/${id}`);
  return unwrap(data);
}
