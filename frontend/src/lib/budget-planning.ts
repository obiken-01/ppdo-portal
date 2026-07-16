/**
 * Budget Planning Dashboard API helpers (RAL-80, RAL-60).
 *
 * getDashboard/getRecentActivity return raw JSON (no { data, error, message }
 * envelope) — same pattern as the main DashboardFunctions, not the config endpoints.
 * getOfficeDashboard (RAL-60) uses the envelope, per its ticket.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type { ApiResponse, OfficeDashboard, PpdoDashboard, RecentActivity } from "@/types";

/** PPDO-scoped (v1.4.5 — RAL-161) — the server always resolves the PPDO office internally and
 * clamps wfpByDivision/ceilingByFund to the caller's own division for division-scoped Staff. */
export async function getDashboard(fiscalYear?: number): Promise<PpdoDashboard> {
  const params = fiscalYear != null ? { fiscalYear } : {};
  const { data } = await api.get<PpdoDashboard>("/budget-planning/dashboard", { params });
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
