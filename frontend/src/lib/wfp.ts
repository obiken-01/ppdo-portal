/**
 * WFP Budget Planning API helpers (RAL-68).
 *
 * All endpoints use the { data, error, message } envelope from ConfigHttp.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type {
  ApiResponse,
  EnsureWfpActivityRequest,
  SaveWfpExpenditureRequest,
  SaveWfpRequest,
  WfpActivityRefDto,
  WfpExpenditureDto,
  WfpRecord,
  WfpRecordDetail,
  WfpReserveRateDto,
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
  divisionId?: number;
}

export async function listWfp(params: WfpListParams = {}): Promise<WfpRecord[]> {
  const query: Record<string, string> = {};
  if (params.aipRecordId != null) query.aipRecordId = String(params.aipRecordId);
  if (params.officeId != null) query.officeId = String(params.officeId);
  if (params.divisionId != null) query.divisionId = String(params.divisionId);
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

// ---------------------------------------------------------------------------
// Export Excel — GET /api/budget-planning/wfp/{id}/report
// JWT must be sent via Authorization header, so we use Axios (not a plain link).
// ---------------------------------------------------------------------------

export async function downloadWfpReport(id: number, filename: string): Promise<void> {
  const response = await api.get(`/budget-planning/wfp/${id}/report`, {
    responseType: "blob",
  });
  const url = URL.createObjectURL(new Blob([response.data as BlobPart]));
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

// ---------------------------------------------------------------------------
// v1.4 entry wizard (RAL-120/121/122/123)
// ---------------------------------------------------------------------------

/** Find-or-create the WFP record + activity for a chosen AIP activity. Never deletes/replaces existing activities. */
export async function ensureWfpActivity(body: EnsureWfpActivityRequest): Promise<WfpActivityRefDto> {
  const { data } = await api.post<ApiResponse<WfpActivityRefDto>>(
    "/budget-planning/wfp/activities/ensure",
    body
  );
  return unwrap(data);
}

/** Expenditures already saved under this WFP activity — the wizard's "added so far" list. */
export async function listWfpExpenditures(wfpActivityId: number): Promise<WfpExpenditureDto[]> {
  const { data } = await api.get<ApiResponse<WfpExpenditureDto[]>>(
    "/budget-planning/wfp/expenditures",
    { params: { wfpActivityId } }
  );
  return unwrap(data);
}

export async function getWfpExpenditureById(id: number): Promise<WfpExpenditureDto> {
  const { data } = await api.get<ApiResponse<WfpExpenditureDto>>(
    `/budget-planning/wfp/expenditures/${id}`
  );
  return unwrap(data);
}

/** Create (body.id null) or replace (body.id set) a WFP expenditure. Server computes Q1-4/Net/Total. */
export async function saveWfpExpenditure(body: SaveWfpExpenditureRequest): Promise<WfpExpenditureDto> {
  const { data } = await api.post<ApiResponse<WfpExpenditureDto>>(
    "/budget-planning/wfp/expenditures",
    body
  );
  return unwrap(data);
}

/** The current reserve rate (e.g. 0.10) — never hard-code "10%" client-side. */
export async function getReserveRate(): Promise<WfpReserveRateDto> {
  const { data } = await api.get<ApiResponse<WfpReserveRateDto>>(
    "/budget-planning/wfp/reserve-rate"
  );
  return unwrap(data);
}
