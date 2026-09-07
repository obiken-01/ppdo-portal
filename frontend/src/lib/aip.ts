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
  OpenAipFiscalYearRequest,
  OpenAipFiscalYearResult,
  CreateAipOfficeRequest,
  SeedAipProgramsFromLdipRequest,
  CreateAipProgramRequest,
  CreateAipProjectRequest,
  CreateAipActivityRequest,
  UpdateAipActivityRequest,
  UpdateAipOfficeRequest,
  UpdateAipProgramRequest,
  UpdateAipProjectRequest,
  AipOfficeDetail,
  AipProgramDetail,
  AipProjectDetail,
  AipActivityDetail,
  ApiResponse,
  AipExpenditure,
  SaveAipExpenditureRequest,
  AipExpenditureWriteResult,
  AddAipProgramsWithGroupRequest,
  AipAddablePrograms,
  AipCeilingStatus,
  AipReadiness,
  AipSubmitResult,
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
// Opening a fiscal year (PPDO-62) — Admin only; creates the one base record and
// populates every office's programs from its own LDIP.
// ---------------------------------------------------------------------------

export async function openAipFiscalYear(body: OpenAipFiscalYearRequest): Promise<OpenAipFiscalYearResult> {
  const { data } = await api.post<ApiResponse<OpenAipFiscalYearResult>>("/budget-planning/aip", body);
  return unwrap(data);
}

export async function addAipOffice(aipId: number, body: CreateAipOfficeRequest): Promise<AipOfficeDetail> {
  const { data } = await api.post<ApiResponse<AipOfficeDetail>>(
    `/budget-planning/aip/${aipId}/offices`, body
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP seed-from-LDIP (RAL-181) — seed an office's programs from its LDIP
// ---------------------------------------------------------------------------

export async function seedAipProgramsFromLdip(
  body: SeedAipProgramsFromLdipRequest
): Promise<AipOfficeDetail> {
  const { data } = await api.post<ApiResponse<AipOfficeDetail>>(
    "/budget-planning/aip/seed-programs-from-ldip", body
  );
  return unwrap(data);
}

export async function addAipProgram(officeId: number, body: CreateAipProgramRequest): Promise<AipProgramDetail> {
  const { data } = await api.post<ApiResponse<AipProgramDetail>>(
    `/budget-planning/aip/offices/${officeId}/programs`, body
  );
  return unwrap(data);
}

export async function addAipProject(programId: number, body: CreateAipProjectRequest): Promise<AipProjectDetail> {
  const { data } = await api.post<ApiResponse<AipProjectDetail>>(
    `/budget-planning/aip/programs/${programId}/projects`, body
  );
  return unwrap(data);
}

export async function addAipActivity(projectId: number, body: CreateAipActivityRequest): Promise<AipActivityDetail> {
  const { data } = await api.post<ApiResponse<AipActivityDetail>>(
    `/budget-planning/aip/projects/${projectId}/activities`, body
  );
  return unwrap(data);
}

// Mistakes happen (e.g. data entered under the wrong level) — Draft-only, cascades to children.
export async function deleteAipOffice(officeId: number): Promise<void> {
  const { data } = await api.delete<ApiResponse<boolean>>(`/budget-planning/aip/offices/${officeId}`);
  unwrap(data);
}

export async function deleteAipProgram(programId: number): Promise<void> {
  const { data } = await api.delete<ApiResponse<boolean>>(`/budget-planning/aip/programs/${programId}`);
  unwrap(data);
}

export async function deleteAipProject(projectId: number): Promise<void> {
  const { data } = await api.delete<ApiResponse<boolean>>(`/budget-planning/aip/projects/${projectId}`);
  unwrap(data);
}

export async function deleteAipActivity(activityId: number): Promise<void> {
  const { data } = await api.delete<ApiResponse<boolean>>(`/budget-planning/aip/activities/${activityId}`);
  unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP inline activity edit (RAL-179) — PUT /api/budget-planning/aip/{id}/activities/{activityId}
// ---------------------------------------------------------------------------

export async function updateAipActivity(
  aipRecordId: number, activityId: number, body: UpdateAipActivityRequest
): Promise<AipActivityDetail> {
  const { data } = await api.put<ApiResponse<AipActivityDetail>>(
    `/budget-planning/aip/${aipRecordId}/activities/${activityId}`, body
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP inline office/program/project edit (detail-page CRUD)
// ---------------------------------------------------------------------------

export async function updateAipOffice(officeId: number, body: UpdateAipOfficeRequest): Promise<AipOfficeDetail> {
  const { data } = await api.put<ApiResponse<AipOfficeDetail>>(`/budget-planning/aip/offices/${officeId}`, body);
  return unwrap(data);
}

export async function updateAipProgram(programId: number, body: UpdateAipProgramRequest): Promise<AipProgramDetail> {
  const { data } = await api.put<ApiResponse<AipProgramDetail>>(`/budget-planning/aip/programs/${programId}`, body);
  return unwrap(data);
}

export async function updateAipProject(projectId: number, body: UpdateAipProjectRequest): Promise<AipProjectDetail> {
  const { data } = await api.put<ApiResponse<AipProjectDetail>>(`/budget-planning/aip/projects/${projectId}`, body);
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

// ---------------------------------------------------------------------------
// Field updates (v1.4 WFP Rework Q1/Q2) — captured during WFP data entry.
// Response only confirms success; callers should trust their own request value
// for optimistic local state, not the response body (the service's field-update
// DTO omits nested collections by design — see AipService.UpdateProgramFunctionBandAsync).
// ---------------------------------------------------------------------------

export async function updateAipProgramFunctionBand(
  programId: number,
  functionBand: string | null
): Promise<void> {
  const { data } = await api.put<ApiResponse<unknown>>(
    `/budget-planning/aip/programs/${programId}/function-band`,
    { functionBand }
  );
  unwrap(data);
}

export async function updateAipActivityIsCreation(
  activityId: number,
  isCreation: boolean
): Promise<void> {
  const { data } = await api.put<ApiResponse<unknown>>(
    `/budget-planning/aip/activities/${activityId}/is-creation`,
    { isCreation }
  );
  unwrap(data);
}

// ---------------------------------------------------------------------------
// AIP entry — v1.8.0 Phase 3 (PPDO-52, 56, 59)
// ---------------------------------------------------------------------------

/**
 * Adds programs from the office's LDIP under a named sub-office group, creating the group if it
 * does not exist (PPDO-52).
 *
 * ⚠️ Not the same as `seedAipProgramsFromLdip`, which it resembles. That one finds its target
 * office by REF CODE ALONE and so can only ever reach the first group under it. This keys on
 * (ref code, group name), which is what lets one office carry several printed blocks — the
 * province's FY2027 SOCIAL sheet has three under one office.
 */
export async function addAipProgramsWithGroup(
  aipId: number,
  body: AddAipProgramsWithGroupRequest
): Promise<AipOfficeDetail> {
  const { data } = await api.post<ApiResponse<AipOfficeDetail>>(
    `/budget-planning/aip/${aipId}/programs`, body
  );
  return unwrap(data);
}

export async function listAipExpenditures(activityId: number): Promise<AipExpenditure[]> {
  const { data } = await api.get<ApiResponse<AipExpenditure[]>>(
    `/budget-planning/aip/activities/${activityId}/expenditures`
  );
  return unwrap(data);
}

export async function addAipExpenditure(
  activityId: number, body: SaveAipExpenditureRequest
): Promise<AipExpenditureWriteResult> {
  const { data } = await api.post<ApiResponse<AipExpenditureWriteResult>>(
    `/budget-planning/aip/activities/${activityId}/expenditures`, body
  );
  return unwrap(data);
}

export async function updateAipExpenditure(
  id: number, body: SaveAipExpenditureRequest
): Promise<AipExpenditureWriteResult> {
  const { data } = await api.put<ApiResponse<AipExpenditureWriteResult>>(
    `/budget-planning/aip/expenditures/${id}`, body
  );
  return unwrap(data);
}

/**
 * ⚠️ Returns the recomputed activity rather than nothing. Deleting the last line takes the
 * activity's total to 0, and the caller must render that — discarding the result leaves the page
 * showing the pre-delete figure until someone reloads.
 */
export async function deleteAipExpenditure(id: number): Promise<AipExpenditureWriteResult> {
  const { data } = await api.delete<ApiResponse<AipExpenditureWriteResult>>(
    `/budget-planning/aip/expenditures/${id}`
  );
  return unwrap(data);
}

/** What is blocking submit. Side-effect free — safe to refetch as the encoder works. */
export async function getAipReadiness(aipId: number): Promise<AipReadiness> {
  const { data } = await api.get<ApiResponse<AipReadiness>>(
    `/budget-planning/aip/${aipId}/readiness`
  );
  return unwrap(data);
}

/** Moves every one of this office's sub-office group rows to department review, in one action. */
export async function submitAip(aipId: number): Promise<AipSubmitResult> {
  const { data } = await api.post<ApiResponse<AipSubmitResult>>(
    `/budget-planning/aip/${aipId}/submit`, {}
  );
  return unwrap(data);
}

/** ⚠️ `remaining` may be negative. Render the sign; never clamp it. */
export async function getAipCeiling(aipId: number): Promise<AipCeilingStatus> {
  const { data } = await api.get<ApiResponse<AipCeilingStatus>>(
    `/budget-planning/aip/${aipId}/ceiling`
  );
  return unwrap(data);
}

/**
 * The LDIP programs this office may add for a sector.
 *
 * ⚠️ Ask the server rather than resolving the LDIP here. The resolution is two-tier — the office's
 * own LDIP first, then a multi-office bulk LDIP matched on ref code — and a client-side copy of it
 * diverged from the server's, so the picker offered programs the add path then refused.
 */
export async function getAipAddablePrograms(
  officeConfigId: number, sector: string
): Promise<AipAddablePrograms> {
  const { data } = await api.get<ApiResponse<AipAddablePrograms>>(
    "/budget-planning/aip/addable-programs",
    { params: { officeConfigId, sector } }
  );
  return unwrap(data);
}
