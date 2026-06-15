/**
 * Budget Planning Dashboard API helpers (RAL-80).
 *
 * Endpoints return raw JSON (no { data, error, message } envelope) — same pattern
 * as the main DashboardFunctions, not the config endpoints.
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type { PlanningDashboard, RecentActivity } from "@/types";

export async function getDashboard(fiscalYear?: number): Promise<PlanningDashboard> {
  const params = fiscalYear != null ? { fiscalYear } : {};
  const { data } = await api.get<PlanningDashboard>("/budget-planning/dashboard", { params });
  return data;
}

export async function getRecentActivity(officeId?: number): Promise<RecentActivity[]> {
  const params = officeId != null ? { officeId } : {};
  const { data } = await api.get<RecentActivity[]>("/budget-planning/activity", { params });
  return data;
}
