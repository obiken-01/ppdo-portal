/**
 * PPMP Report API helper (v1.5 — RAL-183).
 *
 * The PPMP report shares the WFP report's office picker (`getWfpReportOffices`) and its
 * office/fiscalYear/division scoping — division-scoped callers are always forced server-side to
 * their own division regardless of the divisionId passed here (RAL-136). Only the preview shape
 * differs (item-grained, procurement-only). The `.xlsx` export is a separate follow-up (RAL-184).
 */

import api from "./api";
import type { ApiResponse, PpmpReportDto } from "@/types";

/**
 * The full PPMP report preview for one office + fiscal year. divisionId is only honored for
 * allocation officers (CanManageAllocation) — division-scoped callers are always forced
 * server-side to their own division (RAL-136).
 */
export async function getPpmpReportPreview(
  officeId: number, fiscalYear: number, divisionId?: number
): Promise<PpmpReportDto> {
  const { data } = await api.get<ApiResponse<PpmpReportDto>>(
    "/budget-planning/ppmp/report/preview",
    { params: { officeId, fiscalYear, ...(divisionId != null ? { divisionId } : {}) } }
  );
  if (data.data == null) throw new Error(data.error ?? "Unexpected empty response.");
  return data.data;
}
