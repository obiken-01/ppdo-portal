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
 * allocation officers (CanManagePpdoAllocation) — division-scoped callers are always forced
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

/**
 * Downloads the PPMP report as an .xlsx matching the province's PPMP form (RAL-184). Same
 * office/fiscalYear/divisionId scoping as the preview. JWT must be sent via the Authorization
 * header, so this uses Axios (not a plain link) — mirrors downloadWfpReportExcel. `filename` is
 * caller-supplied (built client-side) rather than read from Content-Disposition.
 */
export async function downloadPpmpReportExcel(
  officeId: number, fiscalYear: number, filename: string, divisionId?: number
): Promise<void> {
  const response = await api.get("/budget-planning/ppmp/report/export", {
    params: { officeId, fiscalYear, ...(divisionId != null ? { divisionId } : {}) },
    responseType: "blob",
  });

  const url = URL.createObjectURL(new Blob([response.data as BlobPart]));
  const a = document.createElement("a");
  a.href = url;
  a.download = filename;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
