/**
 * Configuration module types (v1.1) — mirrors PPDO.Application/DTOs/Config/
 * and PPDO.Application/Common/ (ApiResponse, CsvImportResult).
 *
 * Config endpoints (RAL-70) return the { data, error, message } envelope —
 * see ApiResponse<T> below. Shared across Accounts (RAL-72), Offices (RAL-73),
 * and Funding Sources (RAL-74).
 */

// ---------------------------------------------------------------------------
// Shared config-API envelope + helpers
// ---------------------------------------------------------------------------

/** Standard config-API response envelope: { data, error, message }. */
export interface ApiResponse<T> {
  data: T | null;
  error: string | null;
  message: string | null;
}

/** Outcome of a CSV upsert: counts + per-row error messages. */
export interface CsvImportResult {
  new: number;
  updated: number;
  skipped: number;
  errors: string[];
}

/** Status filter for config list endpoints (?active=true|false|all). */
export type ActiveFilter = "true" | "false" | "all";

// ---------------------------------------------------------------------------
// Accounts (Chart of Accounts) — RAL-72
// ---------------------------------------------------------------------------

/** Expenditure type derived from the account_number prefix (not stored). */
export type AccountType = "PS" | "MOOE" | "CO" | "Other";

/** Read model for a Chart of Accounts entry. */
export interface AccountResponse {
  id: number;
  accountTitle: string;
  accountNumber: string;
  normalBalance: string | null;
  description: string | null;
  isActive: boolean;
  /** Derived from accountNumber prefix (5-01-/5-02-/5-03-/other). */
  accountType: AccountType;
}

/** Create/update body for an account. accountNumber is the unique key. */
export interface UpsertAccountRequest {
  accountTitle: string;
  accountNumber: string;
  normalBalance: string | null;
  description: string | null;
  isActive: boolean;
}

// ---------------------------------------------------------------------------
// Offices (provincial offices) — RAL-73
// ---------------------------------------------------------------------------

/** Read model for a provincial office (config table `offices`). Mirrors OfficeDto. */
export interface OfficeResponse {
  id: number;
  officeCode: string;
  officeName: string;
  /** Last segment of the AIP office ref code (e.g. "013"). Used to match AIP → config office in WFP. */
  officeRefCode: string | null;
  isActive: boolean;
}

/** Create/update body for an office. officeCode is the unique key. */
export interface UpsertOfficeRequest {
  officeCode: string;
  officeName: string;
  officeRefCode?: string | null;
  isActive: boolean;
}

// ---------------------------------------------------------------------------
// Funding Sources — RAL-74
// ---------------------------------------------------------------------------

/** Read model for a funding source (config table `funding_sources`). */
export interface FundingSourceResponse {
  id: number;
  code: string;
  name: string;
  description: string | null;
  /** Hex color (#RRGGBB) for WFP report total groups. Null = default green group. */
  color: string | null;
  isActive: boolean;
}

/** Create/update body for a funding source. code is the unique key. */
export interface UpsertFundingSourceRequest {
  code: string;
  name: string;
  description: string | null;
  /** Hex color (#RRGGBB) for WFP report total groups. Null = default green group. */
  color: string | null;
  isActive: boolean;
}

// ---------------------------------------------------------------------------
// Divisions — RAL-97 (configurable division = data scope + feature flags)
// ---------------------------------------------------------------------------

/** Read model for a configurable division (config table `divisions`). Mirrors DivisionDto. */
export interface DivisionResponse {
  id: number;
  officeId: number;
  officeName: string | null;
  code: string | null;
  name: string;
  isActive: boolean;
  canAccessInventory: boolean;
  canAccessReports: boolean;
  canManageUsers: boolean;
  canManageResourceLinks: boolean;
  canAccessBudgetPlanning: boolean;
  canUploadAip: boolean;
  canManageConfig: boolean;
}

/** Create/update body for a configurable division. name is the upsert key within an office. */
export interface UpsertDivisionRequest {
  officeId: number;
  code: string | null;
  name: string;
  isActive: boolean;
  canAccessBudgetPlanning: boolean;
  canAccessInventory: boolean;
  canAccessReports: boolean;
  canManageConfig: boolean;
  canUploadAip: boolean;
  canManageUsers: boolean;
  canManageResourceLinks: boolean;
}
