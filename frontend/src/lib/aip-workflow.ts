/**
 * The AIP office workflow, mirrored for the UI (v1.8.0 Phase 4 — PPDO-70).
 *
 * ⚠️ **This is a mirror of `AipWorkflowStatus` in `PPDO.Application/Common`, not a second source
 * of truth.** The server refuses the write regardless of what this file says; the mirror exists so
 * the UI can show the right controls and the right sentence *before* someone tries. When the two
 * disagree the server wins and the user sees a refusal they were not warned about — which is a
 * confusing bug, not a security one. Change both together.
 *
 * ⚠️ The three names below are the wire values — they are string constants on the C# side too, and
 * they cross the wire verbatim in `AipReadinessDto.WorkflowStatus`. Do not localise them.
 */

export const AIP_WORKFLOW = {
  draft: "Draft",
  departmentReview: "DepartmentReview",
  submittedToPpdo: "SubmittedToPpdo",
  returnedByPpdo: "ReturnedByPpdo",
  consolidated: "Consolidated",
} as const;

/**
 * Whether the office's own people may still change its tree — encoder and department head alike.
 *
 * ↩️ **Widened from `Draft`-only in PPDO-70.** The lock falls when the work goes to PPDO, not at
 * the encoder's first submit: during department review both the encoder and the department head
 * are meant to keep working, because the department head's job there is to fix the minor things
 * they find (`AIP_Review_Spec.md` decision 4).
 *
 * ⚠️ Mirrors `AipWorkflowStatus.IsOfficeEditable`. Closed is the default for an unrecognised
 * status, matching the server.
 */
export function isOfficeEditable(status: string): boolean {
  return (
    status === AIP_WORKFLOW.draft ||
    status === AIP_WORKFLOW.departmentReview ||
    status === AIP_WORKFLOW.returnedByPpdo
  );
}

/**
 * Who is holding the work, phrased to drop into "With …" and "This office's AIP is with …".
 *
 * ⚠️ Names a **holder**, never a bare "read-only". An encoder told only that the page is read-only
 * has no idea who has their work or how to get it back — and UI states are this project's largest
 * fix category to date.
 */
export function describeAipHolder(status: string): string {
  switch (status) {
    case AIP_WORKFLOW.departmentReview:
      return "your department head";
    case AIP_WORKFLOW.submittedToPpdo:
      return "PPDO";
    case AIP_WORKFLOW.returnedByPpdo:
      return "you — returned by PPDO";
    case AIP_WORKFLOW.consolidated:
      return "the consolidated AIP";
    default:
      return status;
  }
}
