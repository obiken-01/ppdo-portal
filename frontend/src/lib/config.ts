/**
 * Configuration API helpers (RAL-70 endpoints).
 *
 * All config endpoints return the { data, error, message } envelope. These
 * helpers unwrap `data` for the happy path and throw the envelope `error`
 * (or the message) on failure so callers can surface it via Toast / inline error.
 *
 * All calls go through the shared Axios instance (`@/lib/api`) so the JWT and
 * refresh-on-401 behaviour apply — never call the config API with bare fetch().
 */

import api from "./api";
import type {
  AccountResponse,
  AccountType,
  ApiResponse,
  ActiveFilter,
  CsvImportResult,
  DivisionResponse,
  FundingSourceResponse,
  OfficeResponse,
  PriceIndexItemResponse,
  ProcurementPresetResponse,
  UpsertAccountRequest,
  UpsertDivisionRequest,
  UpsertFundingSourceRequest,
  UpsertOfficeRequest,
  UpsertPriceIndexItemRequest,
  UpsertProcurementPresetRequest,
} from "@/types";

// ---------------------------------------------------------------------------
// Envelope helpers
// ---------------------------------------------------------------------------

/** Unwraps `data` from a config envelope; throws on a missing payload. */
function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) {
    throw new Error(body.error ?? "Unexpected empty response.");
  }
  return body.data;
}

/** Pulls a human-readable message out of an Axios error envelope, if present. */
export function configErrorMessage(err: unknown, fallback: string): string {
  const body = (err as { response?: { data?: ApiResponse<unknown> } })?.response?.data;
  return body?.error ?? body?.message ?? fallback;
}

// ---------------------------------------------------------------------------
// Accounts (Chart of Accounts) — /api/config/accounts
// ---------------------------------------------------------------------------

export interface AccountListParams {
  search?: string;
  /** PS / MOOE / CO — filters by account_number prefix server-side. */
  accountType?: Exclude<AccountType, "Other"> | null;
  active?: ActiveFilter;
}

/** GET /api/config/accounts — list with optional search / type / status filters. */
export async function listAccounts(params: AccountListParams = {}): Promise<AccountResponse[]> {
  const query: Record<string, string> = {};
  if (params.search?.trim()) query.search = params.search.trim();
  if (params.accountType) query.accountType = params.accountType;
  if (params.active) query.active = params.active;

  const { data } = await api.get<ApiResponse<AccountResponse[]>>("/config/accounts", { params: query });
  return unwrap(data);
}

/** POST /api/config/accounts — create a new account. */
export async function createAccount(body: UpsertAccountRequest): Promise<AccountResponse> {
  const { data } = await api.post<ApiResponse<AccountResponse>>("/config/accounts", body);
  return unwrap(data);
}

/** PUT /api/config/accounts/{id} — update an existing account. */
export async function updateAccount(id: number, body: UpsertAccountRequest): Promise<AccountResponse> {
  const { data } = await api.put<ApiResponse<AccountResponse>>(`/config/accounts/${id}`, body);
  return unwrap(data);
}

/** DELETE /api/config/accounts/{id} — soft delete (isActive = false). */
export async function deactivateAccount(id: number): Promise<AccountResponse> {
  const { data } = await api.delete<ApiResponse<AccountResponse>>(`/config/accounts/${id}`);
  return unwrap(data);
}

/** GET /api/config/accounts/csv — raw CSV text in seed column order. */
export async function exportAccountsCsv(): Promise<string> {
  const { data } = await api.get<string>("/config/accounts/csv", { responseType: "text" });
  return data;
}

/** POST /api/config/accounts/csv — upsert by account_number; returns counts. */
export async function importAccountsCsv(csvText: string): Promise<CsvImportResult> {
  const { data } = await api.post<ApiResponse<CsvImportResult>>("/config/accounts/csv", csvText, {
    headers: { "Content-Type": "text/csv" },
  });
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Offices (provincial offices) — /api/config/offices
// ---------------------------------------------------------------------------

export interface OfficeListParams {
  search?: string;
  active?: ActiveFilter;
}

/** GET /api/config/offices — list with optional search / status filters. */
export async function listOffices(params: OfficeListParams = {}): Promise<OfficeResponse[]> {
  const query: Record<string, string> = {};
  if (params.search?.trim()) query.search = params.search.trim();
  if (params.active) query.active = params.active;

  const { data } = await api.get<ApiResponse<OfficeResponse[]>>("/config/offices", { params: query });
  return unwrap(data);
}

/** office_code of PPDO itself — the default office for PPDO-internal budget-planning users (those with no me.officeId). */
export const PPDO_OFFICE_CODE = "PPDO";

/** Finds the PPDO office row in an already-loaded office list, or null if not configured/loaded yet. */
export function findPpdoOffice(offices: OfficeResponse[]): OfficeResponse | null {
  return offices.find((o) => o.officeCode === PPDO_OFFICE_CODE) ?? null;
}

/** POST /api/config/offices — create a new office. */
export async function createOffice(body: UpsertOfficeRequest): Promise<OfficeResponse> {
  const { data } = await api.post<ApiResponse<OfficeResponse>>("/config/offices", body);
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Price Index — /api/config/price-index (v1.4 RAL-118)
// ---------------------------------------------------------------------------

export interface PriceIndexListParams {
  search?: string;
  active?: ActiveFilter;
}

/** GET /api/config/price-index — list with optional search / status filters. */
export async function listPriceIndex(
  params: PriceIndexListParams = {},
): Promise<PriceIndexItemResponse[]> {
  const query: Record<string, string> = {};
  if (params.search?.trim()) query.search = params.search.trim();
  if (params.active) query.active = params.active;

  const { data } = await api.get<ApiResponse<PriceIndexItemResponse[]>>("/config/price-index", {
    params: query,
  });
  return unwrap(data);
}

/** POST /api/config/price-index — create a new price index item. */
export async function createPriceIndexItem(
  body: UpsertPriceIndexItemRequest,
): Promise<PriceIndexItemResponse> {
  const { data } = await api.post<ApiResponse<PriceIndexItemResponse>>("/config/price-index", body);
  return unwrap(data);
}

/** PUT /api/config/price-index/{id} — update an existing price index item. */
export async function updatePriceIndexItem(
  id: number,
  body: UpsertPriceIndexItemRequest,
): Promise<PriceIndexItemResponse> {
  const { data } = await api.put<ApiResponse<PriceIndexItemResponse>>(
    `/config/price-index/${id}`,
    body,
  );
  return unwrap(data);
}

/** DELETE /api/config/price-index/{id} — soft delete (isActive = false). */
export async function deactivatePriceIndexItem(id: number): Promise<PriceIndexItemResponse> {
  const { data } = await api.delete<ApiResponse<PriceIndexItemResponse>>(
    `/config/price-index/${id}`,
  );
  return unwrap(data);
}

/** GET /api/config/price-index/csv — raw CSV text in seed column order. */
export async function exportPriceIndexCsv(): Promise<string> {
  const { data } = await api.get<string>("/config/price-index/csv", { responseType: "text" });
  return data;
}

/** POST /api/config/price-index/csv — upsert by (name, unit); returns counts. */
export async function importPriceIndexCsv(csvText: string): Promise<CsvImportResult> {
  const { data } = await api.post<ApiResponse<CsvImportResult>>(
    "/config/price-index/csv",
    csvText,
    { headers: { "Content-Type": "text/csv" } },
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Divisions — RAL-97
// ---------------------------------------------------------------------------

export interface DivisionListParams {
  active?: ActiveFilter;
  officeId?: number;
}

/** GET /api/config/divisions — list configurable divisions (drives the user-form dropdown). */
export async function listDivisions(params: DivisionListParams = {}): Promise<DivisionResponse[]> {
  const query: Record<string, string> = {};
  if (params.active) query.active = params.active;
  if (params.officeId != null) query.officeId = String(params.officeId);

  const { data } = await api.get<ApiResponse<DivisionResponse[]>>("/config/divisions", { params: query });
  return unwrap(data);
}

/** POST /api/config/divisions — create a new division. */
export async function createDivision(body: UpsertDivisionRequest): Promise<DivisionResponse> {
  const { data } = await api.post<ApiResponse<DivisionResponse>>("/config/divisions", body);
  return unwrap(data);
}

/** PUT /api/config/divisions/{id} — update an existing division. */
export async function updateDivision(id: number, body: UpsertDivisionRequest): Promise<DivisionResponse> {
  const { data } = await api.put<ApiResponse<DivisionResponse>>(`/config/divisions/${id}`, body);
  return unwrap(data);
}

/** DELETE /api/config/divisions/{id} — soft delete (isActive = false). */
export async function deactivateDivision(id: number): Promise<DivisionResponse> {
  const { data } = await api.delete<ApiResponse<DivisionResponse>>(`/config/divisions/${id}`);
  return unwrap(data);
}

/** GET /api/config/divisions/csv — raw CSV text in canonical 11-column order. */
export async function exportDivisionsCsv(): Promise<string> {
  const { data } = await api.get<string>("/config/divisions/csv", { responseType: "text" });
  return data;
}

/** POST /api/config/divisions/csv — upsert by (office_code, name); returns counts. */
export async function importDivisionsCsv(csvText: string): Promise<CsvImportResult> {
  const { data } = await api.post<ApiResponse<CsvImportResult>>("/config/divisions/csv", csvText, {
    headers: { "Content-Type": "text/csv" },
  });
  return unwrap(data);
}

/** PUT /api/config/offices/{id} — update an existing office. */
export async function updateOffice(id: number, body: UpsertOfficeRequest): Promise<OfficeResponse> {
  const { data } = await api.put<ApiResponse<OfficeResponse>>(`/config/offices/${id}`, body);
  return unwrap(data);
}

/** DELETE /api/config/offices/{id} — soft delete (isActive = false). */
export async function deactivateOffice(id: number): Promise<OfficeResponse> {
  const { data } = await api.delete<ApiResponse<OfficeResponse>>(`/config/offices/${id}`);
  return unwrap(data);
}

/** GET /api/config/offices/csv — raw CSV text in seed column order. */
export async function exportOfficesCsv(): Promise<string> {
  const { data } = await api.get<string>("/config/offices/csv", { responseType: "text" });
  return data;
}

/** POST /api/config/offices/csv — upsert by office_code; returns counts. */
export async function importOfficesCsv(csvText: string): Promise<CsvImportResult> {
  const { data } = await api.post<ApiResponse<CsvImportResult>>("/config/offices/csv", csvText, {
    headers: { "Content-Type": "text/csv" },
  });
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Funding Sources — /api/config/funding-sources
// ---------------------------------------------------------------------------

export interface FundingSourceListParams {
  search?: string;
  active?: ActiveFilter;
}

/** GET /api/config/funding-sources — list with optional search / status filters. */
export async function listFundingSources(
  params: FundingSourceListParams = {},
): Promise<FundingSourceResponse[]> {
  const query: Record<string, string> = {};
  if (params.search?.trim()) query.search = params.search.trim();
  if (params.active) query.active = params.active;

  const { data } = await api.get<ApiResponse<FundingSourceResponse[]>>("/config/funding-sources", {
    params: query,
  });
  return unwrap(data);
}

/** Code identifying the General Fund row — matches AllocationService's GeneralFundCode (v1.4.3). */
export const GENERAL_FUND_CODE = "GF";

/** Resolves the General Fund entry from a funding-source list, for callers not yet fund-aware. */
export function findGeneralFund(
  fundingSources: FundingSourceResponse[],
): FundingSourceResponse | null {
  return fundingSources.find((f) => f.code === GENERAL_FUND_CODE) ?? null;
}

/** POST /api/config/funding-sources — create a new funding source. */
export async function createFundingSource(
  body: UpsertFundingSourceRequest,
): Promise<FundingSourceResponse> {
  const { data } = await api.post<ApiResponse<FundingSourceResponse>>("/config/funding-sources", body);
  return unwrap(data);
}

/** PUT /api/config/funding-sources/{id} — update an existing funding source. */
export async function updateFundingSource(
  id: number,
  body: UpsertFundingSourceRequest,
): Promise<FundingSourceResponse> {
  const { data } = await api.put<ApiResponse<FundingSourceResponse>>(
    `/config/funding-sources/${id}`,
    body,
  );
  return unwrap(data);
}

/** DELETE /api/config/funding-sources/{id} — soft delete (isActive = false). */
export async function deactivateFundingSource(id: number): Promise<FundingSourceResponse> {
  const { data } = await api.delete<ApiResponse<FundingSourceResponse>>(
    `/config/funding-sources/${id}`,
  );
  return unwrap(data);
}

/** GET /api/config/funding-sources/csv — raw CSV text in seed column order. */
export async function exportFundingSourcesCsv(): Promise<string> {
  const { data } = await api.get<string>("/config/funding-sources/csv", { responseType: "text" });
  return data;
}

/** POST /api/config/funding-sources/csv — upsert by code; returns counts. */
export async function importFundingSourcesCsv(csvText: string): Promise<CsvImportResult> {
  const { data } = await api.post<ApiResponse<CsvImportResult>>(
    "/config/funding-sources/csv",
    csvText,
    { headers: { "Content-Type": "text/csv" } },
  );
  return unwrap(data);
}

// ---------------------------------------------------------------------------
// Procurement Presets — /api/config/procurement-presets (v1.4 RAL-119)
// ---------------------------------------------------------------------------

export interface ProcurementPresetListParams {
  /** Omit (or null) to list presets across ALL accounts. */
  accountId?: number | null;
  active?: ActiveFilter;
}

/**
 * GET /api/config/procurement-presets?accountId=&active= — presets scoped to one account, or
 * across all accounts when accountId is omitted.
 */
export async function listProcurementPresets(
  params: ProcurementPresetListParams = {},
): Promise<ProcurementPresetResponse[]> {
  const query: Record<string, string> = {};
  if (params.accountId != null) query.accountId = String(params.accountId);
  if (params.active) query.active = params.active;

  const { data } = await api.get<ApiResponse<ProcurementPresetResponse[]>>(
    "/config/procurement-presets",
    { params: query },
  );
  return unwrap(data);
}

/** POST /api/config/procurement-presets — create a new preset. */
export async function createProcurementPreset(
  body: UpsertProcurementPresetRequest,
): Promise<ProcurementPresetResponse> {
  const { data } = await api.post<ApiResponse<ProcurementPresetResponse>>(
    "/config/procurement-presets",
    body,
  );
  return unwrap(data);
}

/** PUT /api/config/procurement-presets/{id} — update an existing preset (replaces its items). */
export async function updateProcurementPreset(
  id: number,
  body: UpsertProcurementPresetRequest,
): Promise<ProcurementPresetResponse> {
  const { data } = await api.put<ApiResponse<ProcurementPresetResponse>>(
    `/config/procurement-presets/${id}`,
    body,
  );
  return unwrap(data);
}

/** DELETE /api/config/procurement-presets/{id} — soft delete (isActive = false). */
export async function deactivateProcurementPreset(id: number): Promise<ProcurementPresetResponse> {
  const { data } = await api.delete<ApiResponse<ProcurementPresetResponse>>(
    `/config/procurement-presets/${id}`,
  );
  return unwrap(data);
}

/**
 * GET /api/config/procurement-presets/for-entry?accountId=&active= — presets scoped to one
 * account, readable by any CanAccessBudgetPlanning user (not just CanManageConfig). This is
 * what the WFP entry wizard's "Load preset" uses — listProcurementPresets above requires
 * CanManageConfig and is for the standalone config page only.
 */
export async function listProcurementPresetsForEntry(
  accountId: number,
  active: ActiveFilter = "true",
): Promise<ProcurementPresetResponse[]> {
  const { data } = await api.get<ApiResponse<ProcurementPresetResponse[]>>(
    "/config/procurement-presets/for-entry",
    { params: { accountId: String(accountId), active } },
  );
  return unwrap(data);
}

/**
 * POST /api/config/procurement-presets/quick-save — create a preset from the WFP entry wizard's
 * "Save as preset" action. Same permission gate as listProcurementPresetsForEntry
 * (CanAccessBudgetPlanning) — no CanManageConfig required.
 */
export async function quickSaveProcurementPreset(
  body: UpsertProcurementPresetRequest,
): Promise<ProcurementPresetResponse> {
  const { data } = await api.post<ApiResponse<ProcurementPresetResponse>>(
    "/config/procurement-presets/quick-save",
    body,
  );
  return unwrap(data);
}
