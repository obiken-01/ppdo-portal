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

/** Expenditure type — mirrors the stored expense_class column (RAL-117). */
export type AccountType = "PS" | "MOOE" | "CO" | "Other";

/** default_nature: default-only pre-fill for the WFP expenditure Nature field — never an enforced gate. */
export type DefaultNature = "Procurement" | "Non-Procurement" | "Combined";

/** Read model for a Chart of Accounts entry. */
export interface AccountResponse {
  id: number;
  accountTitle: string;
  accountNumber: string;
  normalBalance: string | null;
  description: string | null;
  isActive: boolean;
  /** Mirrors expenseClass — kept for existing WFP/AIP consumers (RAL-117). */
  accountType: AccountType;
  /** Stored expenditure class (RAL-117) — no longer derived from the account_number prefix. */
  expenseClass: string;
  /** Optional default-only pre-fill for the WFP "Nature" field; null = no default, user chooses explicitly. */
  defaultNature: DefaultNature | null;
  /** Default-only pre-fill for the WFP reserve toggle — every account may still enable it regardless. */
  defaultApplyReserve: boolean;
}

/** Create/update body for an account. accountNumber is the unique key. */
export interface UpsertAccountRequest {
  accountTitle: string;
  accountNumber: string;
  normalBalance: string | null;
  description: string | null;
  isActive: boolean;
  expenseClass: string;
  defaultNature: DefaultNature | null;
  defaultApplyReserve: boolean;
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
  /** Pipe-delimited alternate names, matched against AIP fund-source labels. Null = none. */
  aliases: string | null;
}

/** Create/update body for a funding source. code is the unique key. */
export interface UpsertFundingSourceRequest {
  code: string;
  name: string;
  description: string | null;
  /** Hex color (#RRGGBB) for WFP report total groups. Null = default green group. */
  color: string | null;
  isActive: boolean;
  /** Pipe-delimited alternate names, matched against AIP fund-source labels. Null = none. */
  aliases: string | null;
}

// ---------------------------------------------------------------------------
// Price Index — v1.4 RAL-118 (procurement item catalogue)
// ---------------------------------------------------------------------------

/** Read model for a price index item (config table `price_index_items`). */
export interface PriceIndexItemResponse {
  id: number;
  name: string;
  unit: string;
  unitPrice: number;
  category: string | null;
  /** Last time unitPrice actually changed — shown so a stale price is visible, not silently trusted. */
  priceUpdatedAt: string;
  isActive: boolean;
  /** Gates the WFP procurement line-item "Days" field (RAL-138) — only venue/food/accommodation-type items need it. */
  daysEnabled: boolean;
  /**
   * GSO stock card number / item code (v1.5) — e.g. "OS-PAP-0000004". Reproduced per line item
   * in the PPMP report's "Stock Card No." column. Optional and not unique; most catalogue items
   * don't carry one.
   */
  stockCardNo: string | null;
}

/** Create/update body for a price index item. (name, unit) is the unique key. */
export interface UpsertPriceIndexItemRequest {
  name: string;
  unit: string;
  unitPrice: number;
  category: string | null;
  isActive: boolean;
  daysEnabled: boolean;
  stockCardNo: string | null;
}

// ---------------------------------------------------------------------------
// Procurement Presets — v1.4 RAL-119 (account-scoped reusable line-item templates)
// ---------------------------------------------------------------------------

/** Read model for one procurement preset line item. */
export interface ProcurementPresetItemResponse {
  id: number;
  priceIndexItemId: number | null;
  /** Snapshotted from the price index item at save time, or free-typed. Editable, never a live link. */
  name: string;
  unit: string;
  unitPrice: number;
  defaultQty: number;
}

/** Read model for a procurement preset, with its items expanded (config table `procurement_presets`). */
export interface ProcurementPresetResponse {
  id: number;
  accountId: number;
  accountNumber: string | null;
  accountTitle: string | null;
  name: string;
  isActive: boolean;
  createdById: string;
  /** Shown for traceability only — presets are shared across all offices/divisions. */
  createdByName: string | null;
  createdAt: string;
  updatedAt: string;
  items: ProcurementPresetItemResponse[];
}

/** Create/update body for one preset line item. */
export interface UpsertProcurementPresetItemRequest {
  priceIndexItemId: number | null;
  /** Required when priceIndexItemId is null (free-typed); ignored/re-snapshotted otherwise. */
  name: string | null;
  unit: string | null;
  unitPrice: number | null;
  defaultQty: number;
}

/** Create/update body for a procurement preset. */
export interface UpsertProcurementPresetRequest {
  accountId: number;
  name: string;
  isActive: boolean;
  items: UpsertProcurementPresetItemRequest[];
}

// ---------------------------------------------------------------------------
// Divisions — RAL-97 (configurable division = data scope + feature flags)
// ---------------------------------------------------------------------------

/** Read model for a configurable division (config table `divisions`). Mirrors DivisionDto. */
export interface DivisionResponse {
  id: number;
  officeId: number;
  officeName: string | null;
  officeCode: string | null;
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

// ── Audit Log (SuperAdmin-only) ───────────────────────────────────────────────

/** One row of the Audit Log page. Exactly one of recordId/recordGuid is set, depending
 * on whether the affected table has an int or Guid PK (e.g. accounts vs users). */
export interface AuditLogEntry {
  id: number;
  changedAt: string; // ISO 8601
  tableName: string;
  action: string; // "CREATE" | "UPDATE" | "DELETE"
  recordId: number | null;
  recordGuid: string | null;
  actorName: string;
  /** Human-readable, possibly multi-line ("\n"-joined) summary of what changed. */
  description: string;
}

/** One filtered/paginated page of audit log entries. */
export interface AuditLogPage {
  items: AuditLogEntry[];
  totalCount: number;
  page: number;
  pageSize: number;
}
