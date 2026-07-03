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
  LdipImportConfirmRequest,
  LdipImportPreviewResponse,
  LdipRecord,
  LdipRecordDetail,
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
// List — GET /api/budget-planning/ldip?status=&officeId=
// (office users are always scoped server-side; officeId is a PPDO-only filter)
// ---------------------------------------------------------------------------

export async function listLdip(
  params: { status?: LdipStatus | ""; officeId?: number } = {}
): Promise<LdipRecord[]> {
  const query: Record<string, string> = {};
  if (params.status) query.status = params.status;
  if (params.officeId != null) query.officeId = String(params.officeId);
  const { data } = await api.get<ApiResponse<LdipRecord[]>>("/budget-planning/ldip", {
    params: query,
  });
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Get by ID — GET /api/budget-planning/ldip/{id} (returns the full hierarchy)
// ---------------------------------------------------------------------------

export async function getLdipById(id: number): Promise<LdipRecordDetail> {
  const { data } = await api.get<ApiResponse<LdipRecordDetail>>(`/budget-planning/ldip/${id}`);
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Create — POST /api/budget-planning/ldip
// ---------------------------------------------------------------------------

export async function createLdip(body: CreateLdipRequest): Promise<LdipRecordDetail> {
  const { data } = await api.post<ApiResponse<LdipRecordDetail>>("/budget-planning/ldip", body);
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Update — PUT /api/budget-planning/ldip/{id} (full-replace of the hierarchy)
// ---------------------------------------------------------------------------

export async function updateLdip(id: number, body: UpdateLdipRequest): Promise<LdipRecordDetail> {
  const { data } = await api.put<ApiResponse<LdipRecordDetail>>(`/budget-planning/ldip/${id}`, body);
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

// ---------------------------------------------------------------------------
// File upload (RAL-113) — POST /api/budget-planning/ldip/upload (parse preview)
// Body: raw .xlsx binary (Content-Type: application/octet-stream)
// ---------------------------------------------------------------------------

export async function uploadLdipFile(
  file: File,
  fiscalYearStart: number,
  fiscalYearEnd: number
): Promise<LdipImportPreviewResponse> {
  const { data } = await api.post<ApiResponse<LdipImportPreviewResponse>>(
    `/budget-planning/ldip/upload?fiscalYearStart=${fiscalYearStart}&fiscalYearEnd=${fiscalYearEnd}`,
    file,
    { headers: { "Content-Type": "application/octet-stream" } }
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// File upload — confirm import — POST /api/budget-planning/ldip/confirm
// Creates one Draft LDIP record per office found in the file.
// ---------------------------------------------------------------------------

export async function confirmLdipImport(body: LdipImportConfirmRequest): Promise<LdipRecord[]> {
  const { data } = await api.post<ApiResponse<LdipRecord[]>>(
    "/budget-planning/ldip/confirm",
    body
  );
  return unwrap(data);
}
