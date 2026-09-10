/**
 * Budget Planning Dashboard API helpers (RAL-80, RAL-60).
 *
 * getDashboard/getRecentActivity return raw JSON (no { data, error, message }
 * envelope) — same pattern as the main DashboardFunctions, not the config endpoints.
 * getOfficeDashboard (RAL-60) uses the envelope, per its ticket.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type {
  ApiResponse,
  FiscalYears,
  OfficeDashboard,
  OfficeSummary,
  PpdoDashboard,
  RecentActivity,
} from "@/types";

/** PPDO-scoped (v1.4.5 — RAL-161) — the server always resolves the PPDO office internally and
 * clamps byDivision/ceilingByFund to the caller's own division for division-scoped Staff. */
export async function getDashboard(fiscalYear?: number): Promise<PpdoDashboard> {
  const params = fiscalYear != null ? { fiscalYear } : {};
  const { data } = await api.get<PpdoDashboard>("/budget-planning/dashboard", { params });
  return data;
}

/** Fiscal-year picker only (RAL-166 follow-up) — use this instead of getDashboard() when a page
 * only needs the fiscal year list (e.g. the Report page), not the full Dashboard payload. */
export async function getFiscalYears(fiscalYear?: number): Promise<FiscalYears> {
  const params = fiscalYear != null ? { fiscalYear } : {};
  const { data } = await api.get<FiscalYears>("/budget-planning/fiscal-years", { params });
  return data;
}

export async function getRecentActivity(officeId?: number): Promise<RecentActivity[]> {
  const params = officeId != null ? { officeId } : {};
  const { data } = await api.get<RecentActivity[]>("/budget-planning/activity", { params });
  return data;
}

export async function getOfficeDashboard(
  officeId: number,
  fiscalYear: number
): Promise<OfficeDashboard> {
  const { data } = await api.get<ApiResponse<OfficeDashboard>>(
    "/budget-planning/dashboard/office",
    { params: { officeId, fiscalYear } }
  );
  if (data.data == null) throw new Error(data.error ?? "Unexpected empty response.");
  return data.data;
}

/**
 * One row per office in the caller's CROSS-OFFICE scope (PPDO-20) — the dashboard's office table.
 *
 * 403s a caller who holds Budget Planning but no cross-office grant. That is the correct answer,
 * not an error to surface: the page decides whether to render this band from the same flags, so a
 * 403 here means the band should not have been requested. Callers gate on
 * `canReviewAllOffices || canManagePboCeiling || role === "SuperAdmin"` before calling.
 */
export async function getDashboardOffices(fiscalYear: number): Promise<OfficeSummary[]> {
  const { data } = await api.get<ApiResponse<OfficeSummary[]>>(
    "/budget-planning/dashboard/offices",
    { params: { fiscalYear } }
  );
  if (data.data == null) throw new Error(data.error ?? "Unexpected empty response.");
  return data.data;
}
