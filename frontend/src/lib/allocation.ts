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

/** Returns null when no ceiling is set for this office + FY (404). */
export async function getCeiling(
  officeId: number,
  fiscalYear: number
): Promise<BudgetCeilingDto | null> {
  try {
    const { data } = await api.get<ApiResponse<BudgetCeilingDto>>(
      "/budget-planning/allocation/ceiling",
      { params: { officeId, fiscalYear } }
    );
    return data.data;
  } catch (err: unknown) {
    const status = (err as { response?: { status?: number } })?.response?.status;
    if (status === 404) return null;
    throw err;
  }
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
  fiscalYear: number
): Promise<DivisionAllocationDto[]> {
  const { data } = await api.get<ApiResponse<DivisionAllocationDto[]>>(
    "/budget-planning/allocation/divisions",
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
