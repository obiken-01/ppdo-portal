/**
 * PPDO reviewer edits to a submitted AIP (v1.9.0 — PPDO-214, `docs/v1.9/AIP_Reviewer_Edit_Spec.md`).
 *
 * During the LFC adjustment a PPDO reviewer brings a part-year activity up to the full year: they
 * may add an expenditure line or change a line's amounts once the office is `SubmittedToPpdo` or
 * `Consolidated`, with a reason the office sees. The office acknowledges the edits with Mark as seen.
 *
 * ⚠️ Courtesy only, like `budget-planning-access.ts`: the server decides every one of these rules
 * again (PPDO-212/213). These keep the page from offering controls that would only be refused.
 */

import type { AipReviewNotifications, MeResponse } from "@/types";
import { AIP_WORKFLOW } from "./aip-workflow";
import { isCommentOnlyReviewer } from "./budget-planning-access";

/** The server's limit on a reviewer's reason, after trimming (`AipReviewerEditComment`). */
export const REVIEWER_REASON_MAX = 500;

/**
 * Whether to offer "Edit amounts" on an office in `workflowStatus`.
 *
 * ⚠️ The comment-only reviewer, exactly: the server takes the reviewer path only for a caller its
 * write guard would otherwise refuse, so SuperAdmin (exempt from that guard) would get the ordinary
 * "not editable here" refusal instead.
 */
export function canPpdoEditAmounts(me: MeResponse | null, workflowStatus: string | null | undefined): boolean {
  if (!me || !isCommentOnlyReviewer(me)) return false;
  return workflowStatus === AIP_WORKFLOW.submittedToPpdo || workflowStatus === AIP_WORKFLOW.consolidated;
}

/** What is wrong with a reason, or null when it may be sent. Mirrors the server's 1–500 rule. */
export function reviewerReasonProblem(reason: string): string | null {
  const length = reason.trim().length;
  if (length === 0) return "Give a reason. The office will see it with your change.";
  if (length > REVIEWER_REASON_MAX) return `Keep the reason to ${REVIEWER_REASON_MAX} characters.`;
  return null;
}

/**
 * The office's "PPDO edited your AIP" notice, or null when there is nothing to show.
 *
 * @param fiscalYear the year on screen, when the notice belongs to one page (AIP Entry). Omitted
 *   where it is page-independent (the sidebar).
 */
export function reviewerEditNotice(
  notifications: AipReviewNotifications | null,
  fiscalYear?: number,
): { count: number; fiscalYear: number } | null {
  if (!notifications || notifications.reviewerEditCount <= 0 || notifications.reviewerEditFiscalYear == null) {
    return null;
  }
  if (fiscalYear !== undefined && fiscalYear !== notifications.reviewerEditFiscalYear) return null;
  return { count: notifications.reviewerEditCount, fiscalYear: notifications.reviewerEditFiscalYear };
}
