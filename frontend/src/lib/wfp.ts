/**
 * WFP Budget Planning API helpers (RAL-68).
 *
 * All endpoints use the { data, error, message } envelope from ConfigHttp.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type {
  ApiResponse,
  SaveWfpRequest,
  WfpRecord,
  WfpRecordDetail,
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

export function wfpErrorMessage(err: unknown, fallback: string): string {
  const body = (err as { response?: { data?: ApiResponse<unknown> } })?.response?.data;
  return body?.error ?? body?.message ?? fallback;
}

// ---------------------------------------------------------------------------
// WFP list — GET /api/budget-planning/wfp
// ---------------------------------------------------------------------------

export interface WfpListParams {
  aipRecordId?: number;
  officeId?: number;
}

export async function listWfp(params: WfpListParams = {}): Promise<WfpRecord[]> {
  const query: Record<string, string> = {};
  if (params.aipRecordId != null) query.aipRecordId = String(params.aipRecordId);
  if (params.officeId != null) query.officeId = String(params.officeId);
  const { data } = await api.get<ApiResponse<WfpRecord[]>>("/budget-planning/wfp", {
    params: query,
  });
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// WFP detail — GET /api/budget-planning/wfp/{id}
// ---------------------------------------------------------------------------

export async function getWfpById(id: number): Promise<WfpRecordDetail> {
  const { data } = await api.get<ApiResponse<WfpRecordDetail>>(
    `/budget-planning/wfp/${id}`
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Save (upsert) — POST /api/budget-planning/wfp
// ---------------------------------------------------------------------------

export async function saveWfp(body: SaveWfpRequest): Promise<WfpRecord> {
  const { data } = await api.post<ApiResponse<WfpRecord>>(
    "/budget-planning/wfp",
    body
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Finalize — POST /api/budget-planning/wfp/{id}/finalize
// ---------------------------------------------------------------------------

export async function finalizeWfp(id: number): Promise<WfpRecord> {
  const { data } = await api.post<ApiResponse<WfpRecord>>(
    `/budget-planning/wfp/${id}/finalize`
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Unlock — POST /api/budget-planning/wfp/{id}/unlock
// ---------------------------------------------------------------------------

export async function unlockWfp(id: number): Promise<WfpRecord> {
  const { data } = await api.post<ApiResponse<WfpRecord>>(
    `/budget-planning/wfp/${id}/unlock`
  );
  return unwrap(data);
}
