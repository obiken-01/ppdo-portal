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
  SaveWfpExpenditurePeriodRequest,
  SaveWfpExpenditureRequest,
  SaveWfpProcurementItemRequest,
  SaveWfpRequest,
  WfpActivityRefDto,
  WfpExpenditureDto,
  WfpExpenditureFrequency,
  WfpRecord,
  WfpRecordDetail,
  WfpReportDto,
  WfpReportOfficeDto,
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

/** Deletes an expenditure and its child periods/procurement items (RAL-129). Forbidden when the parent WFP record is Final. */
export async function deleteWfpExpenditure(id: number): Promise<void> {
  const { data } = await api.delete<ApiResponse<boolean>>(
    `/budget-planning/wfp/expenditures/${id}`
  );
  unwrap(data);
}

/** The current reserve rate (e.g. 0.10) — never hard-code "10%" client-side. */
export async function getReserveRate(): Promise<WfpReserveRateDto> {
  const { data } = await api.get<ApiResponse<WfpReserveRateDto>>(
    "/budget-planning/wfp/reserve-rate"
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// WFP Report preview (RAL-132)
// ---------------------------------------------------------------------------

/** Offices with at least a Draft WFP for the fiscal year — the Report page's office picker. */
export async function getWfpReportOffices(fiscalYear: number): Promise<WfpReportOfficeDto[]> {
  const { data } = await api.get<ApiResponse<WfpReportOfficeDto[]>>(
    "/budget-planning/wfp/report/offices",
    { params: { fiscalYear } }
  );
  return unwrap(data);
}

/** The full WFP report preview for one office + fiscal year. */
export async function getWfpReportPreview(officeId: number, fiscalYear: number): Promise<WfpReportDto> {
  const { data } = await api.get<ApiResponse<WfpReportDto>>(
    "/budget-planning/wfp/report/preview",
    { params: { officeId, fiscalYear } }
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Client-side roll-up preview (RAL-124) — mirrors WfpExpenditureCalculator.Compute
// EXACTLY (backend: PPDO.Application/Common/WfpExpenditureCalculator.cs). There is no
// preview endpoint, and hitting /expenditures (the SAVE endpoint) on every keystroke would
// create/replace real rows just to show a live total — so this small pure function is
// duplicated client-side for the live totals strip only. The server is still the sole
// source of truth: Q1-4/Net/Total actually persisted always come from the save response,
// never from this preview. Keep this in lock-step with the backend if the roll-up rules
// ever change.
// ---------------------------------------------------------------------------

export interface WfpRollUpPreview {
  q1: number;
  q2: number;
  q3: number;
  q4: number;
  net: number;
  total: number;
}

/** 1-based period count for a frequency, e.g. Monthly -> 12. Mirrors WfpExpenditureCalculator.PeriodRange. */
export function wfpPeriodCount(frequency: WfpExpenditureFrequency): number {
  switch (frequency) {
    case "M": return 12;
    case "Q": return 4;
    case "B": return 2;
    case "A": return 1;
  }
}

/** Rolls up period amounts into Q1-Q4/Net/Total per the frequency rules (§2) — preview only. */
export function computeWfpRollUpPreview(
  frequency: WfpExpenditureFrequency,
  periods: SaveWfpExpenditurePeriodRequest[],
  reserveAmount: number,
  annualQuarterChoice: number | null,
): WfpRollUpPreview {
  const amounts = new Map<number, number>();
  for (const p of periods) amounts.set(p.periodNo, (amounts.get(p.periodNo) ?? 0) + p.amount);
  const get = (periodNo: number) => amounts.get(periodNo) ?? 0;

  let q1 = 0, q2 = 0, q3 = 0, q4 = 0;
  switch (frequency) {
    case "M":
      q1 = get(1) + get(2) + get(3);
      q2 = get(4) + get(5) + get(6);
      q3 = get(7) + get(8) + get(9);
      q4 = get(10) + get(11) + get(12);
      break;
    case "Q":
      q1 = get(1);
      q2 = get(2);
      q3 = get(3);
      q4 = get(4);
      break;
    case "B":
      q1 = get(1); // 1st Half -> Q1
      q3 = get(2); // 2nd Half -> Q3
      break;
    case "A": {
      const amount = get(1);
      switch (annualQuarterChoice ?? 1) {
        case 2: q2 = amount; break;
        case 3: q3 = amount; break;
        case 4: q4 = amount; break;
        default: q1 = amount; break; // 1 or unset -> Q1
      }
      break;
    }
  }

  const net = q1 + q2 + q3 + q4;
  const total = net + reserveAmount;
  return { q1, q2, q3, q4, net, total };
}

/**
 * Merges typed period amounts with Σ(qty × unitPrice × numberOfDays) per period from procurement
 * items — mirrors WfpExpenditureCalculator.MergePeriodAmounts (RAL-125, days factor RAL-127).
 * Works uniformly for Procurement (periods empty), Non-Procurement (procurementItems empty), or
 * Combined (both present, summed) — matches the backend's no-nature-branching design (§5.3).
 */
export function mergeWfpPeriodAndItemAmounts(
  periods: SaveWfpExpenditurePeriodRequest[],
  procurementItems: SaveWfpProcurementItemRequest[],
): SaveWfpExpenditurePeriodRequest[] {
  const merged = new Map<number, number>();
  for (const p of periods) merged.set(p.periodNo, (merged.get(p.periodNo) ?? 0) + p.amount);
  for (const i of procurementItems) {
    merged.set(i.periodNo, (merged.get(i.periodNo) ?? 0) + i.qty * i.unitPrice * i.numberOfDays);
  }
  return Array.from(merged, ([periodNo, amount]) => ({ periodNo, amount }));
}
