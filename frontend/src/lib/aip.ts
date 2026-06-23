/**
 * AIP Budget Planning API helpers (RAL-76).
 *
 * All endpoints use the { data, error, message } envelope from ConfigHttp.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type {
  AipRecordResponse,
  AipRecordDetail,
  AipRecordSummary,
  AipImportPreviewResponse,
  AipImportConfirmRequest,
  ApiResponse,
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

export function aipErrorMessage(err: unknown, fallback: string): string {
  const body = (err as { response?: { data?: ApiResponse<unknown> } })?.response?.data;
  return body?.error ?? body?.message ?? fallback;
}

// ---------------------------------------------------------------------------
// AIP list — GET /api/budget-planning/aip
// ---------------------------------------------------------------------------

export interface AipListParams {
  fiscalYear?: number;
  status?: string;
}

export async function listAip(params: AipListParams = {}): Promise<AipRecordResponse[]> {
  const query: Record<string, string> = {};
  if (params.fiscalYear != null) query.fiscalYear = String(params.fiscalYear);
  if (params.status) query.status = params.status;
  const { data } = await api.get<ApiResponse<AipRecordResponse[]>>("/budget-planning/aip", {
    params: query,
  });
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP upload (parse preview) — POST /api/budget-planning/aip/upload
// Body: raw .xlsm binary (Content-Type: application/octet-stream)
// ---------------------------------------------------------------------------

export async function uploadAipFile(
  file: File,
  fiscalYear: number
): Promise<AipImportPreviewResponse> {
  const { data } = await api.post<ApiResponse<AipImportPreviewResponse>>(
    `/budget-planning/aip/upload?fiscalYear=${fiscalYear}`,
    file,
    { headers: { "Content-Type": "application/octet-stream" } }
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP confirm import — POST /api/budget-planning/aip/confirm
// ---------------------------------------------------------------------------

export async function confirmAipImport(body: AipImportConfirmRequest): Promise<AipRecordResponse> {
  const { data } = await api.post<ApiResponse<AipRecordResponse>>(
    "/budget-planning/aip/confirm",
    body
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP detail — GET /api/budget-planning/aip/{id}
// ---------------------------------------------------------------------------

export async function getAipById(id: number): Promise<AipRecordDetail> {
  const { data } = await api.get<ApiResponse<AipRecordDetail>>(
    `/budget-planning/aip/${id}`
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP summary — GET /api/budget-planning/aip/{id}/summary
// Slim hierarchy for the WFP grid: omits heavy free-text fields (~10× smaller).
// ---------------------------------------------------------------------------

export async function getAipSummary(id: number): Promise<AipRecordSummary> {
  const { data } = await api.get<ApiResponse<AipRecordSummary>>(
    `/budget-planning/aip/${id}/summary`
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Status transitions
// ---------------------------------------------------------------------------

export async function finalizeAip(id: number): Promise<AipRecordResponse> {
  const { data } = await api.post<ApiResponse<AipRecordResponse>>(
    `/budget-planning/aip/${id}/finalize`
  );
  return unwrap(data);
}

export async function archiveAip(id: number): Promise<AipRecordResponse> {
  const { data } = await api.delete<ApiResponse<AipRecordResponse>>(
    `/budget-planning/aip/${id}`
  );
  return unwrap(data);
}
