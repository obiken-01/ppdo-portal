/**
 * Bulk initial WFP export API helpers (PPDO-230, v1.9.0).
 *
 * "Download all" queues one background job per fiscal year that builds a ZIP with one Excel per
 * Consolidated office (`docs/v1.9/Bulk_Initial_WFP_Export_Spec.md` §4). The page restores the year's
 * latest job on load and polls it while it is Queued or Running.
 *
 * All calls go through the shared Axios instance for JWT + refresh-on-401.
 */

import api from "./api";
import type { ApiResponse, InitialWfpExportJobDto } from "@/types";

const BASE = "/budget-planning/wfp/report/bulk-exports";

/** Queued and Running are the only states that still change on their own — the poll's whole condition. */
export function isActiveExport(job: InitialWfpExportJobDto | null): boolean {
  return job?.status === "Queued" || job?.status === "Running";
}

/** The year's latest job, or `null` when it has none (the API answers 204). */
export async function getLatestBulkExport(fiscalYear: number): Promise<InitialWfpExportJobDto | null> {
  const res = await api.get<ApiResponse<InitialWfpExportJobDto> | "">(BASE, { params: { fiscalYear } });
  if (res.status === 204 || res.data === "" || res.data == null) return null;
  return res.data.data ?? null;
}

export async function getBulkExport(id: number): Promise<InitialWfpExportJobDto> {
  const { data } = await api.get<ApiResponse<InitialWfpExportJobDto>>(`${BASE}/${id}`);
  if (data.data == null) throw new Error(data.error ?? "Unexpected empty response.");
  return data.data;
}

/** Queues the year's export, or returns the one already active (200 vs 202 — the page treats both alike). */
export async function requestBulkExport(fiscalYear: number): Promise<InitialWfpExportJobDto> {
  const { data } = await api.post<ApiResponse<InitialWfpExportJobDto>>(BASE, { fiscalYear });
  if (data.data == null) throw new Error(data.error ?? "Unexpected empty response.");
  return data.data;
}

/** True when the download endpoint said the file is gone (410) — the page flips to Expired. */
export function isExpiredDownloadError(err: unknown): boolean {
  return (err as { response?: { status?: number } })?.response?.status === 410;
}

/**
 * The file name out of a `Content-Disposition` header, or `null`.
 * ⚠️ The browser only exposes this header cross-origin when the server lists it in
 * `Access-Control-Expose-Headers`, which it does not today — so the caller supplies a fallback name.
 */
export function fileNameFromDisposition(header: string | null | undefined): string | null {
  if (!header) return null;
  const match = /filename\*?=(?:UTF-8'')?"?([^";]+)"?/i.exec(header);
  if (!match) return null;
  try { return decodeURIComponent(match[1]); } catch { return match[1]; }
}

/** Reads a Blob-typed error body back into the API's envelope so the server's sentence can be shown. */
async function unwrapBlobError(err: unknown): Promise<void> {
  const res = (err as { response?: { data?: unknown } })?.response;
  if (res?.data instanceof Blob) {
    try {
      res.data = JSON.parse(await res.data.text()) as ApiResponse<unknown>;
    } catch {
      // Not an envelope — the caller's fallback message applies.
    }
  }
}

/** Manila `yyyy-MM-dd HHmm` of an ISO instant — the stamp the server puts in the ZIP's own name. */
function manilaStamp(iso: string | null | undefined): string {
  const parts = new Intl.DateTimeFormat("en-CA", {
    timeZone: "Asia/Manila", year: "numeric", month: "2-digit", day: "2-digit",
    hour: "2-digit", minute: "2-digit", hourCycle: "h23",
  }).formatToParts(iso ? new Date(iso) : new Date());
  const get = (t: string): string => parts.find((p) => p.type === t)?.value ?? "";
  return `${get("year")}-${get("month")}-${get("day")} ${get("hour")}${get("minute")}`;
}

/**
 * Streams the finished ZIP down through the API and saves it. ⚠️ A refusal arrives as a Blob, since
 * the request asked for one — it is parsed back onto the error so `status` (410 = expired) and the
 * envelope message both survive for the caller.
 */
export async function downloadBulkExport(job: InitialWfpExportJobDto): Promise<void> {
  let response;
  try {
    response = await api.get<Blob>(`${BASE}/${job.id}/download`, { responseType: "blob" });
  } catch (err) {
    await unwrapBlobError(err);
    throw err;
  }

  // The server's own name when the header is readable; otherwise the same name rebuilt
  // (`Initial WFP FY2028 - 2026-10-15 1430.zip`, Manila time of the finish).
  const headers = response.headers as Record<string, string | undefined>;
  const fileName =
    fileNameFromDisposition(headers["content-disposition"])
    ?? `Initial WFP FY${job.fiscalYear} - ${manilaStamp(job.finishedAt)}.zip`;
  const url = URL.createObjectURL(response.data);
  const a = document.createElement("a");
  a.href = url;
  a.download = fileName;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}
