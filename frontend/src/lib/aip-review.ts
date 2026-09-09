/**
 * The PPDO consolidated reviewer's surface — reading one office's AIP and deciding on it
 * (v1.8.0 Phase 4 — V18-56 / PPDO-74).
 *
 * ⚠️ **Every call here needs `CanReviewAllOffices`.** These are the only routes in the system that
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
  AipReviewSearchParams, AipReviewSearchResult,
} from "@/types";

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) throw new Error(body.error ?? "Unexpected empty response.");
  return body.data;
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
