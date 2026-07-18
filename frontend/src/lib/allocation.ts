/**
 * Allocation API helpers (RAL-99 endpoints).
 *
 * All endpoints use the { data, error, message } envelope.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 *
 * Amounts are in PESOS — no ×1000 conversion (that lives in the WFP page only).
 */

import api from "./api";
import type {
  ApiResponse,
  AllocationSetupStatusDto,
  BudgetCeilingDto,
  DivisionAllocationDto,
  ProgramAssignmentDto,
  UpsertAllocationsRequest,
  UpsertCeilingRequest,
  UpsertProgramAssignmentRequest,
  WfpCeilingStatusDto,
} from "@/types";

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) {
    throw new Error(body.error ?? "Unexpected empty response.");
  }
  return body.data;
}

export function allocationErrorMessage(err: unknown, fallback: string): string {
  const body = (err as { response?: { data?: ApiResponse<unknown> } })?.response?.data;
  return body?.error ?? body?.message ?? fallback;
}

// ---------------------------------------------------------------------------
// Ceiling — GET/PUT /api/budget-planning/allocation/ceiling
// ---------------------------------------------------------------------------

/** Returns null when no ceiling is set for this office + FY + fund (404). */
export async function getCeiling(
  officeId: number,
  fiscalYear: number,
  fundingSourceId: number
): Promise<BudgetCeilingDto | null> {
  try {
    const { data } = await api.get<ApiResponse<BudgetCeilingDto>>(
      "/budget-planning/allocation/ceiling",
      { params: { officeId, fiscalYear, fundingSourceId } }
    );
    return data.data;
  } catch (err: unknown) {
    const status = (err as { response?: { status?: number } })?.response?.status;
    if (status === 404) return null;
    throw err;
  }
}

/** Every fund source's ceiling for the office + FY (v1.4.3 — RAL-154/155). Funds with none set are absent. */
export async function getCeilings(
  officeId: number,
  fiscalYear: number
): Promise<BudgetCeilingDto[]> {
  const { data } = await api.get<ApiResponse<BudgetCeilingDto[]>>(
    "/budget-planning/allocation/ceilings",
    { params: { officeId, fiscalYear } }
  );
  return unwrap(data);
}

export async function upsertCeiling(body: UpsertCeilingRequest): Promise<BudgetCeilingDto> {
  const { data } = await api.put<ApiResponse<BudgetCeilingDto>>(
    "/budget-planning/allocation/ceiling",
    body
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Division allocations — GET/PUT /api/budget-planning/allocation/divisions
// ---------------------------------------------------------------------------

export async function getAllocations(
  officeId: number,
  fiscalYear: number,
  fundingSourceId: number
): Promise<DivisionAllocationDto[]> {
  const { data } = await api.get<ApiResponse<DivisionAllocationDto[]>>(
    "/budget-planning/allocation/divisions",
    { params: { officeId, fiscalYear, fundingSourceId } }
  );
  return unwrap(data);
}

/** Every fund's division-allocation rows for the office + FY in one call (RAL-166 follow-up) —
 * replaces N parallel getAllocations() calls (one per active fund) the Allocation page used to fire. */
export async function getAllocationsAllFunds(
  officeId: number,
  fiscalYear: number
): Promise<DivisionAllocationDto[]> {
  const { data } = await api.get<ApiResponse<DivisionAllocationDto[]>>(
    "/budget-planning/allocation/divisions/all-funds",
    { params: { officeId, fiscalYear } }
  );
  return unwrap(data);
}

export async function upsertAllocations(
  body: UpsertAllocationsRequest
): Promise<DivisionAllocationDto[]> {
  const { data } = await api.put<ApiResponse<DivisionAllocationDto[]>>(
    "/budget-planning/allocation/divisions",
    body
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Program assignments — GET/PUT /api/budget-planning/allocation/programs
// ---------------------------------------------------------------------------

export async function getPrograms(
  officeId: number,
  fiscalYear: number
): Promise<ProgramAssignmentDto[]> {
  const { data } = await api.get<ApiResponse<ProgramAssignmentDto[]>>(
    "/budget-planning/allocation/programs",
    { params: { officeId, fiscalYear } }
  );
  return unwrap(data);
}

export async function upsertProgram(
  body: UpsertProgramAssignmentRequest
): Promise<ProgramAssignmentDto> {
  const { data } = await api.put<ApiResponse<ProgramAssignmentDto>>(
    "/budget-planning/allocation/programs",
    body
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Setup status — GET /api/budget-planning/allocation/status
// Gated on CanAccessBudgetPlanning — used by WFP users for the gate check.
// ---------------------------------------------------------------------------

export async function getSetupStatus(
  officeId: number,
  fiscalYear: number,
  divisionId: number
): Promise<AllocationSetupStatusDto> {
  const { data } = await api.get<ApiResponse<AllocationSetupStatusDto>>(
    "/budget-planning/allocation/status",
    { params: { officeId, fiscalYear, divisionId } }
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// WFP ceiling status — GET /api/budget-planning/wfp/ceilings (RAL-122)
// Lives here (not lib/wfp.ts) since it's a read over allocation + AIP-budget data,
// mirroring getAllocations/getSetupStatus above. Shared by the entry wizard's (RAL-123)
// sticky header AND its live debounced pre-save check — one computation, two callers.
// ---------------------------------------------------------------------------

export async function getCeilingStatus(
  activityId: number,
  divisionId: number,
  fiscalYear: number
): Promise<WfpCeilingStatusDto> {
  const { data } = await api.get<ApiResponse<WfpCeilingStatusDto>>(
    "/budget-planning/wfp/ceilings",
    { params: { activityId, divisionId, fiscalYear } }
  );
  return unwrap(data);
}
