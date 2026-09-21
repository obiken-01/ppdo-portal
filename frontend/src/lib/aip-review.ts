/**
 * The PPDO consolidated reviewer's surface — reading one office's AIP and deciding on it
 * (v1.8.0 Phase 4 — V18-56 / PPDO-74).
 *
 * ⚠️ **Every call here needs `CanReviewAllOffices`** — except the search and the activity read, which
 * the department-head reviewer uses too (PPDO-79). These are the only routes in the system that
 * deliberately bypass `OfficeScope`, and a caller without the flag gets a 403 from the endpoint —
 * not a clamp to their own office. Do not "helpfully" fall back to the entry endpoints on a 403;
 * an office reads its own work on the entry page, under the editability rules that belong to it.
 *
 * ⚠️ **A 409 from either action is not a bug.** It means another reviewer moved this office first,
 * and its message is the sentence to show. Re-read afterwards so the screen shows the state they
 * left rather than the one this reader started from.
 */

import api from "./api";
import type {
  ApiResponse, AipOfficeReview, AipSubmitResult,
  AipReviewSearchParams, AipReviewSearchResult, AipActivityReview, AipConsolidatedSheet,
} from "@/types";

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) throw new Error(body.error ?? "Unexpected empty response.");
  return body.data;
}

/**
 * Downloads the fiscal year as the province's Annex B workbook — all four sector sheets
 * (V18-60 / PPDO-84, `AIP_Form_Spec.md` §12). Axios rather than a plain link, because the JWT goes in
 * the Authorization header (as `downloadPpmpReportExcel`).
 *
 * ⚠️ **A refusal arrives as a Blob**, since the request asked for one. Its JSON envelope is parsed
 * back onto the error so `aipErrorMessage` shows the server's sentence rather than the fallback.
 */
export async function downloadAipConsolidatedExcel(
  fiscalYear: number,
  // ⚠️ Omitted, never sent as null (PPDO-90): the server reads "absent" as the consolidated scope,
  // and a department head's value is ignored in favour of their own office either way.
  officeId?: number | null
): Promise<void> {
  let response;
  try {
    response = await api.get<Blob>("/budget-planning/aip/consolidated/export", {
      params: officeId == null ? { fiscalYear } : { fiscalYear, officeId },
      responseType: "blob",
    });
  } catch (err) {
    const res = (err as { response?: { data?: unknown } })?.response;
    if (res?.data instanceof Blob) {
      try {
        res.data = JSON.parse(await res.data.text()) as ApiResponse<unknown>;
      } catch {
        // Not an envelope — leave it; the caller's fallback message applies.
      }
    }
    throw err;
  }

  // The server names the file, but Content-Disposition is not readable cross-origin, so the same
  // name is built here: AIP_FY<year>_<Manila yyyy-MM-dd>.xlsx.
  const manilaDate = new Date().toLocaleDateString("en-CA", { timeZone: "Asia/Manila" });
  const url = URL.createObjectURL(response.data);
  const a = document.createElement("a");
  a.href = url;
  a.download = `AIP_FY${fiscalYear}_${manilaDate}.xlsx`;
  document.body.appendChild(a);
  a.click();
  document.body.removeChild(a);
  URL.revokeObjectURL(url);
}

/**
 * One sector sheet of the consolidated AIP (PPDO-73) — the Annex B rows of every office with PPDO or
 * already accepted, with the figures the form prints. Cross-office reviewers only.
 */
export async function getAipConsolidated(
  fiscalYear: number,
  sector: string,
  /** One office instead of every submitted one (PPDO-90). Omitted, not null — see the export. */
  officeId?: number | null
): Promise<AipConsolidatedSheet> {
  const { data } = await api.get<ApiResponse<AipConsolidatedSheet>>(
    "/budget-planning/aip/consolidated",
    { params: officeId == null ? { fiscalYear, sector } : { fiscalYear, sector, officeId } }
  );
  return unwrap(data);
}

/** One office's tree, read-only, with the state the reviewer is deciding on. */
export async function getAipOfficeReview(
  aipId: number,
  officeId: number
): Promise<AipOfficeReview> {
  const { data } = await api.get<ApiResponse<AipOfficeReview>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/review`
  );
  return unwrap(data);
}

/**
 * Sends the whole office back for changes — `SubmittedToPpdo` → `ReturnedByPpdo`.
 *
 * ⚠️ No covering note, and no body. The row-anchored comments are how a reviewer says what needs to
 * change; a second free-text channel attached to the transition would compete with them.
 */
export async function returnAipToOffice(
  aipId: number,
  officeId: number
): Promise<AipSubmitResult> {
  const { data } = await api.post<ApiResponse<AipSubmitResult>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/return`, {}
  );
  return unwrap(data);
}

/**
 * Takes the office into the consolidated AIP — `SubmittedToPpdo` → `Consolidated`.
 *
 * ⚠️ **There is no un-accept.** `Consolidated` is terminal in shipped code and the return path
 * refuses from it, so this is a one-way door — which is why the button is behind a confirm dialog
 * that names the office.
 */
export async function acceptAipOffice(
  aipId: number,
  officeId: number
): Promise<AipSubmitResult> {
  const { data } = await api.post<ApiResponse<AipSubmitResult>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/accept`, {}
  );
  return unwrap(data);
}

/**
 * Re-opens an accepted office and sends it back — `Consolidated` → `ReturnedByPpdo` (added
 * 2026-09-14). A 409 means the office is no longer accepted: someone moved it while it was open.
 */
export async function reopenAipOffice(
  aipId: number,
  officeId: number
): Promise<AipSubmitResult> {
  const { data } = await api.post<ApiResponse<AipSubmitResult>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/reopen`, {}
  );
  return unwrap(data);
}

/**
 * The query-first AIP Review search (V18-75 / PPDO-76).
 *
 * ⚠️ **`refCode` goes over the wire raw.** The server splits it on `OR` or a comma — a second
 * parser here would be a second thing to keep in step, and the two would disagree the first time
 * one of them learned a new separator.
 *
 * ⚠️ **`title` is never split, anywhere.** A project may legitimately be called
 * "Aid or relief distribution".
 *
 * ⚠️ Multi-selects travel as one comma-joined parameter. That comma is transport only — it is not
 * the typed OR-list, and it never touches the title.
 */
export async function searchAipReview(
  params: AipReviewSearchParams
): Promise<AipReviewSearchResult> {
  const query: Record<string, string> = { fiscalYear: String(params.fiscalYear) };

  if (params.officeIds?.length) query.officeIds = params.officeIds.join(",");
  if (params.sectors?.length) query.sectors = params.sectors.join(",");
  if (params.workflowStatuses?.length) query.workflowStatuses = params.workflowStatuses.join(",");
  if (params.refCode?.trim()) query.refCode = params.refCode.trim();
  if (params.title?.trim()) query.title = params.title.trim();
  if (params.mine) query.mine = "true";
  if (params.page != null) query.page = String(params.page);
  if (params.pageSize != null) query.pageSize = String(params.pageSize);

  const { data } = await api.get<ApiResponse<AipReviewSearchResult>>(
    "/budget-planning/aip/review/search", { params: query }
  );
  return unwrap(data);
}

/**
 * One activity with its path, its expenditure lines and whether this reader may edit it — the
 * activity modal on the AIP Review search (PPDO-79).
 *
 * ⚠️ **Open to both reviewers.** The server lets a cross-office reviewer open any office's activity
 * and a department head only their own; anything else is a 404 worded like a missing activity.
 *
 * ⚠️ **Use this, not `listAipExpenditures`, for the lines.** That endpoint scopes with the entry
 * page's resolver, which 404s a cross-office reviewer who does not sit in PPDO.
 */
export async function getAipActivityReview(activityId: number): Promise<AipActivityReview> {
  const { data } = await api.get<ApiResponse<AipActivityReview>>(
    `/budget-planning/aip/activities/${activityId}/review`
  );
  return unwrap(data);
}
