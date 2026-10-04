/**
 * PPDO-175 — what the Investment Planning dashboard says about an office's AIP submission.
 *
 * Before this the submission stage was a constant ("Opens in a later release") and the department
 * head saw a disabled Submit AIP, although submission has worked in AIP Entry since Phase 4. Both
 * now come from the office's real workflow state on `/dashboard/office` (`OfficeAipSummary`):
 * `workflowStatus`, `workflowStatusSince` and `lastHandOff`.
 *
 * ⚠️ The dashboard never submits. Every action here links to AIP Entry, where the checklist and its
 * gates live; duplicating a submit button here would duplicate those gates.
 *
 * Pure functions, so the page stays a renderer and the wording lives in one place.
 */

import type { StatusRisk, StatusStage } from "@/components/ui/StatusPill";
import type { ActionTone } from "@/components/ui/ActionCard";

/** `AipWorkflowStatus` (backend/PPDO.Application/Common/AipWorkflowStatus.cs). */
export type AipWorkflowState =
  | "Draft"
  | "DepartmentReview"
  | "SubmittedToPpdo"
  | "ReturnedByPpdo"
  | "Consolidated";

/** `AuditAction.ReturnToEncoder`: the department head sent the AIP back to the encoders. */
const RETURN_TO_ENCODER = "RETURN_DH";
/** `AuditAction.ReturnByPpdo`. Seen on a Draft too: the office's work went back to the encoders after PPDO's return. */
const RETURN_BY_PPDO = "RETURN_PPD";

/** Who sent a Draft back, from its last hand-off; null when it was never returned. */
function returnedBy(lastHandOff: string | null | undefined): "head" | "ppdo" | null {
  if (lastHandOff === RETURN_TO_ENCODER) return "head";
  if (lastHandOff === RETURN_BY_PPDO) return "ppdo";
  return null;
}

export interface SubmissionInput {
  workflowStatus: string | null | undefined;
  /** UTC ISO string, from the office's latest hand-off. */
  workflowStatusSince: string | null | undefined;
  lastHandOff: string | null | undefined;
  /** The reader is this office's department head (`canReviewBudgetPlanning`). */
  isDepartmentHead: boolean;
  fiscalYear: number | null;
}

export interface SubmissionStage {
  stage: StatusStage;
  owner: string;
  detail: string;
  risk?: StatusRisk;
}

export interface SubmissionCard {
  tone: ActionTone;
  title: string;
  description: string;
  actionLabel: string;
}

/** "Oct 3", in Manila time. Empty when there is no date. */
export function shortDate(iso: string | null | undefined): string {
  if (!iso) return "";
  return new Date(iso).toLocaleDateString("en-PH", {
    month: "short",
    day: "numeric",
    timeZone: "Asia/Manila",
  });
}

function since(prefix: string, iso: string | null | undefined): string {
  const d = shortDate(iso);
  return d ? `${prefix} since ${d}` : prefix;
}

/**
 * The pipeline rail's "AIP submission" stage, or null when there is no submission to show: FY2027
 * and earlier have no review workflow (the caller leaves the stage out), and an office with no AIP
 * groups yet has nothing to submit.
 */
export function submissionStage(input: SubmissionInput, firstEnteredYear: number): SubmissionStage | null {
  if (input.fiscalYear == null || input.fiscalYear < firstEnteredYear) return null;

  const head = input.isDepartmentHead;
  switch (input.workflowStatus as AipWorkflowState | null | undefined) {
    case "DepartmentReview":
      return {
        stage: "Review",
        owner: head ? "You" : "Your department head",
        detail: since("With the department head", input.workflowStatusSince),
      };
    case "SubmittedToPpdo":
      return { stage: "Review", owner: "PPDO", detail: since("With PPDO", input.workflowStatusSince) };
    case "ReturnedByPpdo":
      return {
        stage: "In progress",
        owner: head ? "You" : "Your department head",
        detail: "PPDO returned it with comments",
      };
    case "Consolidated": {
      const d = shortDate(input.workflowStatusSince);
      return { stage: "Done", owner: "PPDO", detail: d ? `Accepted by PPDO on ${d}` : "Accepted by PPDO" };
    }
    case "Draft": {
      const by = returnedBy(input.lastHandOff);
      return by
        ? {
            stage: "In progress",
            owner: "Your office",
            detail: by === "ppdo" ? "Returned by PPDO for changes" : "Returned to the encoders for changes",
          }
        : { stage: "Todo", owner: "Your office", detail: "Encoders submit to the department head" };
    }
    default:
      return null;
  }
}

/**
 * The action card once the ceiling is published and the office has AIP work — the cases before
 * that ("publish the ceiling", "start this year's AIP") stay with the page.
 */
export function submissionCard(input: SubmissionInput, firstEnteredYear: number): SubmissionCard {
  if (input.fiscalYear == null || input.fiscalYear < firstEnteredYear) {
    return {
      tone: "waiting",
      title: "Keep costing your AIP activities",
      description: `Review and submission start with FY ${firstEnteredYear}. Earlier years are edited directly.`,
      actionLabel: "AIP Entry",
    };
  }

  const head = input.isDepartmentHead;
  const date = shortDate(input.workflowStatusSince);
  const on = date ? ` on ${date}` : "";

  switch (input.workflowStatus as AipWorkflowState | null | undefined) {
    case "DepartmentReview":
      return head
        ? {
            tone: "action",
            title: "Review and send to PPDO",
            description: `Your encoders submitted the AIP${on}. Check it in AIP Entry, then send it to PPDO or return it with comments.`,
            actionLabel: "Review and send to PPDO",
          }
        : {
            tone: "waiting",
            title: "With your department head",
            description: `You submitted the AIP${on}. Your department head reviews it and sends it to PPDO.`,
            actionLabel: "AIP Entry",
          };
    case "SubmittedToPpdo":
      return {
        tone: "waiting",
        title: since("With PPDO", input.workflowStatusSince),
        description: "PPDO is reviewing your AIP. If they return it, the comments will be on the activities in AIP Entry.",
        actionLabel: "AIP Entry",
      };
    case "ReturnedByPpdo":
      return {
        tone: "blocked",
        title: "PPDO returned the AIP with comments",
        description: head
          ? "Read the comments on the activities in AIP Entry. Fix the AIP or return it to your encoders, then send it to PPDO again."
          : "Read the comments on the activities in AIP Entry. Your department head sends it back to PPDO once it is fixed.",
        actionLabel: "See the comments",
      };
    case "Consolidated":
      return {
        tone: "waiting",
        title: "Accepted by PPDO",
        description: `PPDO accepted this office's FY ${input.fiscalYear} AIP${on}. It is in the consolidated AIP.`,
        actionLabel: "AIP Entry",
      };
    default:
      // Draft, or no state yet.
      switch (returnedBy(input.lastHandOff)) {
        case "head":
          return head
            ? {
                tone: "waiting",
                title: "Returned to your encoders",
                description: `You returned the AIP${on}. It comes back to you when they submit it again.`,
                actionLabel: "AIP Entry",
              }
            : {
                tone: "blocked",
                title: "Your department head returned the AIP",
                description: "Read their comments in AIP Entry, make the changes, then submit it again.",
                actionLabel: "See the comments",
              };
        case "ppdo":
          return head
            ? {
                tone: "waiting",
                title: "With your encoders after PPDO's return",
                description: `PPDO returned the AIP${on}. It comes back to you when your encoders submit it again.`,
                actionLabel: "AIP Entry",
              }
            : {
                tone: "blocked",
                title: "PPDO returned the AIP with comments",
                description: "Read the comments on the activities in AIP Entry, make the changes, then submit it to your department head again.",
                actionLabel: "See the comments",
              };
      }
      return head
        ? {
            tone: "waiting",
            title: "Waiting for your encoders",
            description: "Your encoders are costing the AIP. It comes to you once they submit it.",
            actionLabel: "AIP Entry",
          }
        : {
            tone: "action",
            title: "Keep costing your AIP activities",
            description: "When every activity carries a cost, submit the AIP to your department head from AIP Entry.",
            actionLabel: "AIP Entry",
          };
  }
}
