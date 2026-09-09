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
import type { ApiResponse, AipOfficeReview, AipSubmitResult } from "@/types";

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
