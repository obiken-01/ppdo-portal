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
