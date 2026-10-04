/**
 * AIP Budget Planning API helpers (RAL-76).
 *
 * All endpoints use the { data, error, message } envelope from ConfigHttp.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type {
  UpdateAipActivityDivisionRequest,
  AipDivisionStatusList,
  AipDivisionSubmitResult,
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
  AipActivitySummary,
  AipProgramSummary,
  ApiResponse,
  AipExpenditure,
  SaveAipExpenditureRequest,
  AipExpenditureWriteResult,
  AddAipProgramsWithGroupRequest,
  UpdateAipActivityDetailsRequest,
  AipAddablePrograms,
  AipCeilingStatus,
  AipReadiness,
  AipSubmitResult,
  AipDeleteResult,
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

export async function deleteAipProgram(programId: number): Promise<AipDeleteResult> {
  const { data } = await api.delete<ApiResponse<AipDeleteResult>>(`/budget-planning/aip/programs/${programId}`);
  return unwrap(data);
}

export async function deleteAipProject(projectId: number): Promise<AipDeleteResult> {
  const { data } = await api.delete<ApiResponse<AipDeleteResult>>(`/budget-planning/aip/projects/${projectId}`);
  return unwrap(data);
}

export async function deleteAipActivity(activityId: number): Promise<AipDeleteResult> {
  const { data } = await api.delete<ApiResponse<AipDeleteResult>>(`/budget-planning/aip/activities/${activityId}`);
  return unwrap(data);
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

/**
 * `officeId` (PPDO-184) narrows the tree to one office. It never widens the caller's scope — the
 * server applies it after its own office filter. AIP Entry passes the encoder's office; the detail
 * page passes nothing and gets everything the caller may see.
 */
export async function getAipById(id: number, officeId?: number | null): Promise<AipRecordDetail> {
  const { data } = await api.get<ApiResponse<AipRecordDetail>>(
    `/budget-planning/aip/${id}`,
    officeId != null ? { params: { officeId } } : undefined
  );
  return restoreDetailNulls(unwrap(data));
}

// ---------------------------------------------------------------------------
// AIP summary — GET /api/budget-planning/aip/{id}/summary
// Slim hierarchy for the WFP grid: omits heavy free-text fields (~10× smaller).
// ---------------------------------------------------------------------------

export async function getAipSummary(id: number): Promise<AipRecordSummary> {
  const { data } = await api.get<ApiResponse<AipRecordSummary>>(
    `/budget-planning/aip/${id}/summary`
  );
  return restoreSummaryNulls(unwrap(data));
}

// ---------------------------------------------------------------------------
// PPDO-185 — the detail and summary reads leave null fields out of the JSON
// (`ConfigHttp.JsonOmitNulls`); most activities carry no division, CC figures or typology, and
// those nulls were a fifth of a 1.5 MB body. They are put back here, once, so every reader still
// sees `null` and not `undefined`.
//
// ⚠️ Why here and not in each reader: several pages and components read this tree, and some of
// them pass a field straight into form state (`setCcAdaptation(activity.ccAdaptation)`) or test
// `=== null`. A missing field reaching those would be a silent behaviour change, not a type error.
//
// The key lists are checked against the types with `satisfies`: a new nullable field on any of
// these interfaces fails the build until it is added here.
// ---------------------------------------------------------------------------

/** The keys of `T` whose type includes `null`. */
type NullableKeys<T> = { [K in keyof T]-?: null extends T[K] ? K : never }[keyof T];

function restoreNulls<T extends object>(node: T, keys: Record<NullableKeys<T>, true>): void {
  const fields = node as Record<string, unknown>;
  for (const key of Object.keys(keys)) {
    if (fields[key] === undefined) fields[key] = null;
  }
}

const RECORD_NULLS = { originalFilename: true, ldipId: true, sourceId: true } satisfies Record<NullableKeys<AipRecordDetail>, true>;
const OFFICE_NULLS = { officeId: true } satisfies Record<NullableKeys<AipOfficeDetail>, true>;
const PROGRAM_NULLS = { functionBand: true } satisfies Record<NullableKeys<AipProgramDetail>, true>;
const PROJECT_NULLS = { description: true, objective: true } satisfies Record<NullableKeys<AipProjectDetail>, true>;
const ACTIVITY_NULLS = {
  esreCode: true, implementingOffice: true, startDate: true, endDate: true, expectedOutputs: true,
  fundingSourceId: true, fundingSourceSnapshot: true, ps: true, mooe: true, co: true, total: true,
  ccAdaptation: true, ccMitigation: true, ccTypologyCode: true, divisionId: true, divisionName: true,
} satisfies Record<NullableKeys<AipActivityDetail>, true>;

const SUMMARY_PROGRAM_NULLS = { functionBand: true } satisfies Record<NullableKeys<AipProgramSummary>, true>;
const SUMMARY_ACTIVITY_NULLS = {
  ps: true, mooe: true, co: true, total: true, fundingSourceId: true, fundingSourceSnapshot: true,
} satisfies Record<NullableKeys<AipActivitySummary>, true>;

function restoreDetailNulls(record: AipRecordDetail): AipRecordDetail {
  restoreNulls(record, RECORD_NULLS);
  for (const office of record.offices) {
    restoreNulls(office, OFFICE_NULLS);
    for (const program of office.programs) {
      restoreNulls(program, PROGRAM_NULLS);
      for (const project of program.projects) {
        restoreNulls(project, PROJECT_NULLS);
        for (const activity of project.activities) restoreNulls(activity, ACTIVITY_NULLS);
      }
    }
  }
  return record;
}

function restoreSummaryNulls(record: AipRecordSummary): AipRecordSummary {
  for (const office of record.offices) {
    for (const program of office.programs) {
      restoreNulls(program, SUMMARY_PROGRAM_NULLS);
      for (const project of program.projects) {
        for (const activity of project.activities) restoreNulls(activity, SUMMARY_ACTIVITY_NULLS);
      }
    }
  }
  return record;
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

/**
 * The AIP Entry page's activity editor — descriptive fields only (PPDO-52).
 *
 * ⚠️ Deliberately NOT `updateAipActivity`. That one owns ps/mooe/co/fundingSourceId and writes
 * them unconditionally; on an entered year those are derived from the expenditure lines, so
 * saving a description through it would zero the costing. The endpoint cannot accept them.
 */
export async function updateAipActivityDetails(
  activityId: number, body: UpdateAipActivityDetailsRequest
): Promise<AipActivityDetail> {
  const { data } = await api.put<ApiResponse<AipActivityDetail>>(
    `/budget-planning/aip/activities/${activityId}/details`, body
  );
  return unwrap(data);
}

/**
 * PPDO-148 — the department head moves an activity to another division of its office. Encoders
 * get a 403; the office must still be in its own hands. Returns the activity with its new
 * `divisionId`/`divisionName`, ready to splice into the tree.
 */
export async function retagAipActivityDivision(
  activityId: number, divisionId: number
): Promise<AipActivityDetail> {
  const body: UpdateAipActivityDivisionRequest = { divisionId };
  const { data } = await api.put<ApiResponse<AipActivityDetail>>(
    `/budget-planning/aip/activities/${activityId}/division`, body
  );
  return unwrap(data);
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

/**
 * The **second** submit: the department head sends the reviewed work on to PPDO (PPDO-69).
 *
 * ⚠️ Not the same call as `submitAip` above, and not the same authority. That one is the
 * encoder's (Draft → department review); this one requires `canReviewBudgetPlanning` and runs
 * from department review — or from returned-by-PPDO, which is the re-submit.
 *
 * ⚠️ The completeness and ceiling checks run **again** server-side, because the department head
 * may have edited values during review. A success here is not implied by the checklist having
 * been green when the encoder submitted.
 */
export async function submitAipToPpdo(
  aipId: number,
  officeId: number
): Promise<AipSubmitResult> {
  const { data } = await api.post<ApiResponse<AipSubmitResult>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/submit-to-ppdo`, {}
  );
  return unwrap(data);
}

/**
 * The department head hands the office's work back down to its encoders — department review →
 * Draft (added 2026-09-14). From department review only; the server refuses every other state.
 */
export async function returnAipToEncoder(
  aipId: number,
  officeId: number
): Promise<AipSubmitResult> {
  const { data } = await api.post<ApiResponse<AipSubmitResult>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/return-to-encoder`, {}
  );
  return unwrap(data);
}

/** PPDO-149 — every division of one office and where it stands, with the caller's actions. */
export async function getAipOfficeDivisions(
  aipId: number, officeId: number
): Promise<AipDivisionStatusList> {
  const { data } = await api.get<ApiResponse<AipDivisionStatusList>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/divisions`
  );
  return unwrap(data);
}

/**
 * PPDO-149 — a division hands its work to the department head. The last division with activities
 * moves the office to department review. Over the ceiling still succeeds, with `ceilingWarning`.
 */
export async function submitAipDivision(
  aipId: number, divisionId: number
): Promise<AipDivisionSubmitResult> {
  const { data } = await api.post<ApiResponse<AipDivisionSubmitResult>>(
    `/budget-planning/aip/${aipId}/divisions/${divisionId}/submit`, {}
  );
  return unwrap(data);
}

/** PPDO-149 — the department head reopens one division; the others stay submitted. */
export async function returnAipDivision(
  aipId: number, divisionId: number
): Promise<AipDivisionSubmitResult> {
  const { data } = await api.post<ApiResponse<AipDivisionSubmitResult>>(
    `/budget-planning/aip/${aipId}/divisions/${divisionId}/return`, {}
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
  // ⚠️ The record id is required by the server: the response flags which programs that AIP already
  // carries, and there is no answer to that without knowing which AIP.
  aipRecordId: number, officeConfigId: number, sector: string
): Promise<AipAddablePrograms> {
  const { data } = await api.get<ApiResponse<AipAddablePrograms>>(
    "/budget-planning/aip/addable-programs",
    { params: { aipRecordId, officeConfigId, sector } }
  );
  return unwrap(data);
}
