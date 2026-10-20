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

import type { AipUnresolvedCounts, DivisionSummary, ProposalProjectOption } from "@/types";
import { formatPesosSigned } from "@/lib/money";

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
  /**
   * PPDO-237 — divisions over their allocation, from `divisionAllocationOverages`, already scoped
   * to the hop. Null or omitted when the read failed or does not apply: then there is no warning.
   */
  allocationOverages?: DivisionAllocationOverage[] | null;
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
  const { hop, ceilingWarning, unresolved, proposalGaps, allocationOverages } = input;
  const toPpdo = hop.kind === "toPpdo";
  const recipient = toPpdo ? "PPDO" : "your department head";

  const warnings: string[] = [];
  if (proposalGaps && proposalGaps.length > 0) warnings.push(proposalWarning(proposalGaps, recipient));
  // On the PPDO hop the button is disabled while over the ceiling, so there is nothing to confirm.
  if (!toPpdo && ceilingWarning) {
    warnings.push(`${ceilingWarning} Your department head cannot send it to PPDO until it is within the ceiling.`);
  }
  // PPDO-237 — after the office ceiling, so the two money warnings sit together. Soft on every hop.
  if (allocationOverages && allocationOverages.length > 0) {
    warnings.push(...allocationWarnings(allocationOverages, toPpdo ? "head" : "encoder"));
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

// ── Division allocations (PPDO-237) ───────────────────────────────────────────

/** One division over its allocation in one fund. */
export interface DivisionAllocationOverage {
  /** The division's code, or its name when it has none — what the allocation tables show. */
  divisionLabel: string;
  fundCode: string;
  /** Costed minus allocated, in pesos. Always > 0. */
  over: number;
  /** No allocation in this fund — treated as ₱0, like an unset office ceiling. */
  unset: boolean;
}

/** The untagged "No division" row's id (`BudgetPlanningDashboardService.NoDivisionId`). */
const NO_DIVISION_ID = 0;

/**
 * Every division × fund costed beyond its allocation (PPDO-237, Ralph 2026-10-20).
 *
 * ↩️ **Reverses `Division_Submit_Spec.md` §7's "no per-division ceiling" non-goal.** UAT found REV
 * ₱551,000 over its General Fund allocation, submitting with no warning and a readiness panel that
 * said it "checks out", because only the office ceiling was looked at.
 *
 * ⚠️ **Per fund, never the row total.** Allocations are entered per fund; ₱100,000 spare in one fund
 * does not cover an overspend in another, and a total would hide exactly that.
 *
 * ⚠️ **Unset is ₱0, not unlimited** — the server lists a fund the division has costed with no
 * allocation in it (`amount` 0), and that is over by the whole costed amount.
 *
 * `used` is the AIP-costed figure on the ceiling's rule (MOOE + CO, rounded up per activity), the
 * same basis as the row's "Costed in AIP". `divisionId` scopes to the submitting division; omit it
 * on the department head's hop, where every division counts. Pure — warning only, never a block.
 */
export function divisionAllocationOverages(
  rows: DivisionSummary[], divisionId?: number | null,
): DivisionAllocationOverage[] {
  const overages: DivisionAllocationOverage[] = [];
  for (const row of rows) {
    if (row.divisionId === NO_DIVISION_ID) continue; // untagged work: no allocation to be over
    if (divisionId != null && row.divisionId !== divisionId) continue;
    for (const fund of row.allocationByFund) {
      // Rounded to the centavo so floating-point dust never reads as "over by ₱0.00".
      const over = Math.round((fund.used - fund.amount) * 100) / 100;
      if (over <= 0) continue;
      overages.push({
        divisionLabel: row.divisionCode ?? row.divisionName,
        fundCode: fund.fundCode,
        over,
        unset: fund.amount === 0,
      });
    }
  }
  return overages;
}

/** One sentence per division × fund, as the dialog and the readiness panel both list them. */
export function allocationOverageLine(o: DivisionAllocationOverage): string {
  return o.unset
    ? `${o.divisionLabel}’s ${o.fundCode} allocation is not set (treated as ₱0); over by ${formatPesosSigned(o.over)}.`
    : `${o.divisionLabel} is over its ${o.fundCode} allocation by ${formatPesosSigned(o.over)}.`;
}

/**
 * What to do about it, said once after the last line rather than after each. Depends on the reader:
 * an encoder's department head can rebalance; the department head is told it does not hold the send.
 */
export function allocationOverageAdvice(reader: "encoder" | "head"): string {
  return reader === "head"
    ? "This does not stop the send; division allocations can be rebalanced on the Allocation page."
    : "Your department head can rebalance division allocations before sending to PPDO.";
}

function allocationWarnings(overages: DivisionAllocationOverage[], reader: "encoder" | "head"): string[] {
  const lines = overages.map(allocationOverageLine);
  lines[lines.length - 1] += ` ${allocationOverageAdvice(reader)}`;
  return lines;
}

