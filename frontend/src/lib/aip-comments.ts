/**
 * Inline AIP review comments (v1.8.0 Phase 4 — V18-53 / PPDO-71).
 *
 * ⚠️ **Commenting is not a content write.** These endpoints work while an office is locked at
 * `SubmittedToPpdo` — which is exactly the state a PPDO reviewer comments in — and they are open to
 * cross-office reviewers, who are denied every *content* write. Do not "tidy" them behind the same
 * gates the entry endpoints use.
 *
 * ⚠️ **Only the authoring side resolves.** Read `canResolve` off each comment rather than inferring
 * it from who the reader is; the server decides, and it says no to the side a comment is addressed
 * to however senior they are.
 */

import api from "./api";
import type {
  ApiResponse,
  AipOfficeHistory,
  AipReviewComment,
  AipReviewComments,
  CreateAipReviewCommentRequest,
} from "@/types";

function unwrap<T>(body: ApiResponse<T>): T {
  if (body.data == null) throw new Error(body.error ?? "Unexpected empty response.");
  return body.data;
}

/** Every comment on one office — resolved ones included — plus the tally split by side. */
export async function getAipComments(
  aipId: number,
  officeId: number
): Promise<AipReviewComments> {
  const { data } = await api.get<ApiResponse<AipReviewComments>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/comments`
  );
  return unwrap(data);
}

/**
 * An office's submission history — hand-offs newest first, each carrying the comments written
 * while the office held that state (PPDO-77). Same readers as the comments: the office itself and
 * any cross-office reviewer.
 */
export async function getAipHistory(
  aipId: number,
  officeId: number
): Promise<AipOfficeHistory> {
  const { data } = await api.get<ApiResponse<AipOfficeHistory>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/history`
  );
  return unwrap(data);
}

/** Leaves a comment on one row. Refused for an encoder — they act on comments, never write them. */
export async function addAipComment(
  aipId: number,
  officeId: number,
  body: CreateAipReviewCommentRequest
): Promise<AipReviewComment> {
  const { data } = await api.post<ApiResponse<AipReviewComment>>(
    `/budget-planning/aip/${aipId}/offices/${officeId}/comments`,
    body
  );
  return unwrap(data);
}

/**
 * Marks a comment resolved.
 *
 * ⚠️ Only the side that wrote it may call this. A 403 here is the rule working, not a bug.
 */
export async function resolveAipComment(commentId: number): Promise<AipReviewComment> {
  const { data } = await api.post<ApiResponse<AipReviewComment>>(
    `/budget-planning/aip/comments/${commentId}/resolve`,
    {}
  );
  return unwrap(data);
}
