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
  UpsertAccountRequest,
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
