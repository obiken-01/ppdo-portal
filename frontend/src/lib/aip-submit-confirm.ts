/**
 * The one confirmation an AIP submit shows (PPDO-206).
 *
 * ↩️ **One dialog per action, listing every warning.** The encoder's division submit used to stack
 * up to three dialogs — "no final investment proposal" → "Submit Division?" → "Submit with
 * unresolved comments?" — and the first two both said "Submit anyway", so answering only the first
 * read as done. The department head's Send to PPDO, the harder step to undo, showed only the
 * proposals warning and then sent. Every submit now builds one dialog from this function: the same
 * structure on both hops, the warnings as a list, one confirm button.
 *
 * ⚠️ **Every warning stays soft.** None of them blocks; the hard gates (completeness, and the
 * ceiling on the PPDO hop) disable the button before this is reached. Pure, so the rules are tested
 * on their own (`aip-submit-confirm.test.ts`).
 */

import type { AipUnresolvedCounts, ProposalProjectOption } from "@/types";

export type AipSubmitHop =
  | { kind: "encoder" }
  | { kind: "division"; divisionName: string }
  | { kind: "toPpdo"; resubmit: boolean };

export interface AipSubmitConfirmInput {
  hop: AipSubmitHop;
  /** The office's ceiling warning, or null. Only warned on the hops to the department head. */
  ceilingWarning: string | null;
  /** Null when the comment read failed — then there is no warning, never a guessed one. */
  unresolved: AipUnresolvedCounts | null;
  /** Projects without a final proposal, read at the click; empty or null for none. */
  proposalGaps: ProposalProjectOption[] | null;
}

export interface AipSubmitConfirmation {
  title: string;
  /** What the submit does, always said — the "are you sure?" half. */
  intro: string;
  /** Every warning that applies, in a fixed order. Empty for a plain confirm. */
  warnings: string[];
  confirmLabel: string;
  variant: "primary" | "warning";
}

/** How many proposals to name before "and N more"; each warning is a sentence, not a list. */
const PROPOSAL_NAMES_SHOWN = 5;

export function buildAipSubmitConfirmation(input: AipSubmitConfirmInput): AipSubmitConfirmation {
  const { hop, ceilingWarning, unresolved, proposalGaps } = input;
  const toPpdo = hop.kind === "toPpdo";
  const recipient = toPpdo ? "PPDO" : "your department head";

  const warnings: string[] = [];
  if (proposalGaps && proposalGaps.length > 0) warnings.push(proposalWarning(proposalGaps, recipient));
  // On the PPDO hop the button is disabled while over the ceiling, so there is nothing to confirm.
  if (!toPpdo && ceilingWarning) {
    warnings.push(`${ceilingWarning} Your department head cannot send it to PPDO until it is within the ceiling.`);
  }
  // ↩️ PPDO-206 — every hop warns, the first Send to PPDO included. PPDO-133 kept that one silent on
  // the grounds that only the head's own comments could be open; the UAT run found a PPDO comment
  // still open at the send, and the head went on without being told.
  if (unresolved && unresolved.total > 0) warnings.push(unresolvedWarning(unresolved, hop));

  const warned = warnings.length > 0;

  switch (hop.kind) {
    case "encoder":
      return {
        title: "Submit for department review?",
        intro: "This hands the office’s whole AIP to your department head.",
        warnings,
        confirmLabel: warned ? "Submit anyway" : "Submit",
        variant: warned ? "warning" : "primary",
      };
    case "division":
      return {
        title: `Submit ${hop.divisionName}?`,
        intro: "You won’t be able to edit these activities unless your department head returns them.",
        warnings,
        confirmLabel: warned ? "Submit anyway" : `Submit ${hop.divisionName}`,
        variant: warned ? "warning" : "primary",
      };
    case "toPpdo":
      return {
        title: hop.resubmit ? "Re-submit to PPDO?" : "Send to PPDO?",
        intro: "Sending locks this office’s AIP — nobody here can edit it while PPDO has it.",
        warnings,
        confirmLabel: warned
          ? (hop.resubmit ? "Re-submit anyway" : "Send anyway")
          : (hop.resubmit ? "Re-submit to PPDO" : "Send to PPDO"),
        variant: warned ? "warning" : "primary",
      };
  }
}

/**
 * The proposal warning: how many projects, split into "no proposal" and "still a draft" because
 * the fix differs (create one, or finish and finalize it), then the first few by name.
 */
function proposalWarning(gaps: ProposalProjectOption[], recipient: string): string {
  const none = gaps.filter((g) => g.status === "None").length;
  const draft = gaps.length - none;
  const counts: string[] = [];
  if (none > 0) counts.push(`${none} ${none === 1 ? "has" : "have"} no proposal`);
  if (draft > 0) counts.push(`${draft} ${draft === 1 ? "is" : "are"} still a draft`);

  const names = gaps.slice(0, PROPOSAL_NAMES_SHOWN).map((g) => g.projectName).join("; ");
  const more = gaps.length > PROPOSAL_NAMES_SHOWN ? `; and ${gaps.length - PROPOSAL_NAMES_SHOWN} more` : "";

  return (
    `Of your projects, ${counts.join(" and ")}: ${names}${more}. ` +
    `Proposals stay editable after it goes to ${recipient}; finish them on the Investment Proposals page.`
  );
}

/**
 * The unresolved-comment warning, naming **both** counts separately.
 *
 * ⚠️ **Never one merged total.** Only the side that wrote a comment may clear it, so a single figure
 * would imply an action the reader does not have, and "2 from PPDO" is a different situation from
 * "2 from the department head".
 *
 * ⚠️ Says the comments **stay** open rather than that they will be lost — nothing is discarded on
 * submit.
 */
function unresolvedWarning({ fromPpdo, fromDepartmentHead }: AipUnresolvedCounts, hop: AipSubmitHop): string {
  // On the PPDO hop the reader IS the department head, so "your department head" would be wrong.
  const head = hop.kind === "toPpdo" ? "the department head" : "your department head";
  const recipient = hop.kind === "toPpdo" ? "PPDO" : "your department head";

  // ⚠️ "unresolved" sits with the FIRST count, so the sentence does not have to be re-read.
  const parts: string[] = [];
  if (fromPpdo > 0) parts.push(`${fromPpdo} unresolved comment${fromPpdo === 1 ? "" : "s"} from PPDO`);
  if (fromDepartmentHead > 0) {
    parts.push(
      fromPpdo > 0
        ? `${fromDepartmentHead} from ${head}`
        : `${fromDepartmentHead} unresolved comment${fromDepartmentHead === 1 ? "" : "s"} from ${head}`
    );
  }

  return `This office still has ${parts.join(" and ")}. They stay on the record, and ${recipient} will see them.`;
}
