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
  CreateAipRecordRequest,
  CreateAipOfficeRequest,
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
// AIP manual entry (RAL-62) — one node at a time
// ---------------------------------------------------------------------------

export async function createManualAipRecord(body: CreateAipRecordRequest): Promise<AipRecordResponse> {
  const { data } = await api.post<ApiResponse<AipRecordResponse>>("/budget-planning/aip", body);
  return unwrap(data);
}

export async function addAipOffice(aipId: number, body: CreateAipOfficeRequest): Promise<AipOfficeDetail> {
  const { data } = await api.post<ApiResponse<AipOfficeDetail>>(
    `/budget-planning/aip/${aipId}/offices`, body
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
