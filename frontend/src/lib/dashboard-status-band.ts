/**
 * PPDO-178 — the Investment Planning dashboard's status band: one sentence saying where the AIP is
 * and who has it, one line saying why, the one next step, and a thin step track underneath.
 *
 * It replaces the action card and the 3–5 pipeline stage cards (design pass F12, wireframe boards
 * 1, 3, 4 and 5 in `docs/v1.8/wireframes/dashboard/`). Every state and reader has its own sentence;
 * board 5 is the list.
 *
 * Pure functions, so the page stays a renderer and every sentence lives in this one file. It took
 * over `aip-submission-status.ts` (PPDO-175), whose stage and card wording is folded in here.
 *
 * ⚠️ The dashboard never submits or returns. Every button is a link to the page where that action
 * and its gates live (AIP Entry, Allocation, the review queue), so the checks stay in one place.
 *
 * ⚠️ The holder is named by ROLE, never by person (design pass open question 1): names change, and
 * the role answers "who do I ask".
 */

/** `AipWorkflowStatus` (backend/PPDO.Application/Common/AipWorkflowStatus.cs). */
export type AipWorkflowState =
  | "Draft"
  | "DepartmentReview"
  | "SubmittedToPpdo"
  | "ReturnedByPpdo"
  | "Consolidated";

/** `AuditAction.ReturnToEncoder`: the department head sent the AIP back to the encoders. */
const RETURN_TO_ENCODER = "RETURN_DH";
/** `AuditAction.ReturnByPpdo`. Also seen on a Draft: the head passed PPDO's return on to the encoders. */
const RETURN_BY_PPDO = "RETURN_PPD";

/**
 * What the band says about the state, not how it looks. `todo` — this reader can act now;
 * `waiting` — the next move is somebody else's; `blocking` — something must change first;
 * `returned` — sent back with comments; `done` — accepted; `info` — no workflow this year.
 */
export type BandTone = "todo" | "waiting" | "blocking" | "returned" | "done" | "info";

export interface BandLink {
  label: string;
  href: string;
}

export type StepState = "done" | "current" | "todo";

export interface BandStep {
  key: string;
  label: string;
  /** One short line under the label: who holds it, or how far along it is. */
  note: string;
  state: StepState;
}

/** The reviewer's track: offices per board column instead of one AIP's steps. */
export interface BandCount {
  key: string;
  label: string;
  count: number;
}

export interface StatusBand {
  tone: BandTone;
  /** e.g. "FY <year> AIP · with you since Oct 2". */
  eyebrow: string;
  headline: string;
  /** One line saying why. Omitted when the headline says it all. */
  reason?: string;
  /** The one next step. None when there is nothing for this reader to do. */
  action?: BandLink;
  /** A second, quieter link: only where the wireframe has one (return to encoders, assign programs). */
  secondary?: BandLink;
  /** The AIP's steps. Empty for the reviewer, who gets `counts` instead. */
  steps: BandStep[];
  counts?: BandCount[];
}

export interface StatusBandInput {
  fiscalYear: number | null;
  firstEnteredYear: number;
  /** Short office name for the eyebrow when there is no holder to name ("PPDO"). */
  officeCode: string;

  // ── Who is reading ──
  /** `canReviewAllOffices` or SuperAdmin: the reviewer band, whatever their own office is doing. */
  isCrossOfficeReviewer: boolean;
  /** PPDO finance (PBO until PPDO-87): publishes every office's ceiling. */
  canManageOfficeCeilings: boolean;
  /** This office's department head (`canReviewBudgetPlanning`). */
  isDepartmentHead: boolean;
  /** The host office (PPDO): adds Division allocation and Program assignment to the track. */
  isHost: boolean;
  /** PPDO finance: sets the division split. */
  canManageAllocation: boolean;

  // ── The office's figures (/dashboard/office, plus the host dashboard for PPDO) ──
  /** False until the office endpoint answers; the band shows its skeleton meanwhile. */
  loaded: boolean;
  /** General Fund ceiling — the one the send to PPDO checks. Null when not published. */
  ceiling: number | null;
  /** Costed against that ceiling (GF, MOOE + CO, rounded up): the submit check's own figure. */
  costedAgainstCeiling: number;
  hasAip: boolean;
  activityCount: number;
  uncostedActivityCount: number;
  workflowStatus: string | null | undefined;
  /** UTC ISO string of the latest hand-off. */
  workflowStatusSince: string | null | undefined;
  lastHandOff: string | null | undefined;
  divisionsSubmitted: number | null | undefined;
  divisionsRequired: number | null | undefined;
  divisionsWaiting: string[] | null | undefined;
  unresolvedComments: number | null | undefined;

  // ── Host office (PPDO) only ──
  /** Across every fund, as the money tiles count it. */
  officeCeilingAllFunds: number | null;
  allocatedToDivisions: number;
  assignedProgramCount: number;
  unassignedProgramCount: number;

  // ── Reviewer only ──
  /** Offices at SubmittedToPpdo (the sidebar badge's count). Null while it loads. */
  pendingForPpdo: number | null;
  /** The earliest year with an office waiting — where the queue link goes. */
  pendingFiscalYear: number | null;
  /** Offices per board column for the year. Null while the offices list loads. */
  officeColumns: BandCount[] | null;
  /** Offices without a FY ceiling, for PBO's "26 offices are waiting on them". */
  officesWithoutCeiling: number | null;

  // ── Where the buttons go ──
  aipEntryHref: string;
  allocationHref: string;
  reportHref: string;
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

function peso(n: number): string {
  return `₱${n.toLocaleString("en-PH", { minimumFractionDigits: 2, maximumFractionDigits: 2 })}`;
}

function plural(n: number, one: string, many: string): string {
  return `${n.toLocaleString("en-PH")} ${n === 1 ? one : many}`;
}

/** "2 divisions has" / "2 divisions have": the verb agrees with how many submitted ("1 of 2 … has"). */
function divisionsOf(submitted: number, required: number): string {
  return `${required.toLocaleString("en-PH")} ${required === 1 ? "division" : "divisions"} ${submitted === 1 ? "has" : "have"}`;
}

/** "Admin", "Admin and Planning", "Admin, Planning and Records". */
function listNames(names: string[]): string {
  if (names.length <= 1) return names.join("");
  return `${names.slice(0, -1).join(", ")} and ${names[names.length - 1]}`;
}

function since(phrase: string, iso: string | null | undefined): string {
  const d = shortDate(iso);
  return d ? `${phrase} since ${d}` : phrase;
}

/** Who sent a Draft back, from its last hand-off; null when it was never returned. */
function returnedBy(lastHandOff: string | null | undefined): "head" | "ppdo" | null {
  if (lastHandOff === RETURN_TO_ENCODER) return "head";
  if (lastHandOff === RETURN_BY_PPDO) return "ppdo";
  return null;
}

/** "PPDO returned the AIP" + this. Zero open comments is said outright, not as "with comments". */
function comments(n: number | null | undefined): string {
  if (n == null) return " with comments";
  return n > 0 ? ` with ${plural(n, "comment", "comments")} to resolve` : ". Every comment on it is resolved";
}

// ── Step track ──────────────────────────────────────────────────────────────

/** The index of the step the AIP is at, on the office track (Ceiling = 0 … Accepted = 4). */
function officeStepIndex(i: StatusBandInput): number {
  if (i.ceiling == null) return 0;
  switch (i.workflowStatus as AipWorkflowState | null | undefined) {
    case "DepartmentReview":
      return 2;
    case "ReturnedByPpdo":
      // With divisions still resubmitting, the work is back at encoding; otherwise it is the head's.
      return i.divisionsRequired != null && (i.divisionsSubmitted ?? 0) < i.divisionsRequired ? 1 : 2;
    case "SubmittedToPpdo":
      return 3;
    case "Consolidated":
      return 5; // past the last step: everything done
    default:
      return 1;
  }
}

function stepState(index: number, current: number): StepState {
  return index < current ? "done" : index === current ? "current" : "todo";
}

function buildSteps(i: StatusBandInput): BandStep[] {
  const head = i.isDepartmentHead;
  const status = i.workflowStatus as AipWorkflowState | null | undefined;
  const entered = i.fiscalYear != null && i.fiscalYear >= i.firstEnteredYear;

  const ceilingStep: BandStep = {
    key: "ceiling",
    label: "Ceiling",
    note: i.ceiling != null ? "Published by PPDO" : i.canManageOfficeCeilings ? "Waiting on you" : "Waiting for ceiling",
    state: i.ceiling != null ? "done" : "current",
  };

  // FY2027 and earlier: an uploaded AIP with no review workflow. The track stops at encoding.
  if (!entered) {
    return [
      ceilingStep,
      {
        key: "encoding",
        label: "Encoding",
        note: i.hasAip ? plural(i.activityCount, "activity", "activities") : "Nothing yet",
        state: i.ceiling != null ? "current" : "todo",
      },
    ];
  }

  const current = officeStepIndex(i);
  const by = returnedBy(i.lastHandOff);

  let encodingNote: string;
  if (current > 1) encodingNote = "Done";
  else if (status === "ReturnedByPpdo")
    encodingNote = `Returned, ${i.divisionsSubmitted ?? 0} of ${i.divisionsRequired} divisions resubmitted`;
  else if (by != null) encodingNote = by === "ppdo" ? "Returned by PPDO" : "Returned by the department head";
  else if (i.divisionsRequired != null && i.divisionsRequired > 0)
    encodingNote = `${i.divisionsSubmitted ?? 0} of ${i.divisionsRequired} divisions submitted`;
  else encodingNote = head ? "Your encoders" : "Your office";

  const steps: BandStep[] = [
    ceilingStep,
    { key: "encoding", label: "Encoding", note: encodingNote, state: stepState(1, current) },
    {
      key: "department",
      label: "Department review",
      note:
        status === "ReturnedByPpdo"
          ? "Returned by PPDO"
          : head
          ? "You"
          : "Department head",
      state: stepState(2, current),
    },
    {
      key: "ppdo",
      label: "PPDO review",
      note: status === "SubmittedToPpdo" ? since("With PPDO", i.workflowStatusSince) : "PPDO reviewer",
      state: stepState(3, current),
    },
    {
      key: "accepted",
      label: "Accepted",
      note: status === "Consolidated" ? `On ${shortDate(i.workflowStatusSince)}` : "Goes into the provincial AIP",
      state: status === "Consolidated" ? "done" : "todo",
    },
  ];

  if (!i.isHost) return steps;

  // The host office splits its ceiling across divisions and assigns programs before anyone can
  // encode them: two steps between the ceiling and encoding. Neither moves the AIP's own position.
  // Finance sees the whole split, so "done" means the whole ceiling is allocated. A clamped reader
  // only sees their own division's share, so any allocation is all they can tell.
  const allocated = i.canManageAllocation
    ? i.officeCeilingAllFunds != null && i.officeCeilingAllFunds > 0 && i.allocatedToDivisions >= i.officeCeilingAllFunds
    : i.allocatedToDivisions > 0;
  const programsTotal = i.assignedProgramCount + i.unassignedProgramCount;
  const assigned = i.assignedProgramCount > 0 && i.unassignedProgramCount === 0;
  // ⚠️ Only one step is ever "current": the AIP's own. Once encoding has started, an unfinished
  // setup step reads as not done ("19 of 25"), not as a second current step beside Encoding.
  const pastSetup = current > 1;
  const encodingStarted = pastSetup || i.activityCount > 0;
  // ⚠️ Finance only: a division-clamped reader's `allocatedToDivisions` is their own division, so a
  // percentage of the office ceiling would differ per reader (68% for one, 35% for another).
  const pct =
    i.canManageAllocation && i.officeCeilingAllFunds != null && i.officeCeilingAllFunds > 0
      ? Math.round((i.allocatedToDivisions / i.officeCeilingAllFunds) * 100)
      : null;
  const owner = i.canManageAllocation ? "you" : "PPDO finance";
  const hostSteps: BandStep[] = [
    {
      key: "allocation",
      label: "Division allocation",
      note: i.allocatedToDivisions > 0 ? (pct != null ? `${pct}% · ${owner}` : `Allocated · ${owner}`) : `Not started · ${owner}`,
      state: allocated || pastSetup ? "done" : i.ceiling != null && !encodingStarted ? "current" : "todo",
    },
    {
      key: "programs",
      label: "Program assignment",
      note: programsTotal > 0 ? `${i.assignedProgramCount} of ${programsTotal} · ${owner}` : `None yet · ${owner}`,
      state: assigned || pastSetup ? "done" : allocated && !encodingStarted ? "current" : "todo",
    },
  ];
  return [steps[0], ...hostSteps, ...steps.slice(1)];
}

// ── The band ────────────────────────────────────────────────────────────────

/**
 * The band for this reader, or null while the figures it needs are still loading (the page shows
 * the band's skeleton, sized like the loaded band).
 */
export function statusBand(i: StatusBandInput): StatusBand | null {
  const fy = i.fiscalYear;
  const fyLabel = fy != null ? `FY ${fy}` : "This year's";

  // ── The cross-office reviewer: the queue, whatever PPDO's own AIP is doing (PPDO-176) ──
  if (i.isCrossOfficeReviewer) {
    const total = i.officeColumns?.reduce((n, c) => n + c.count, 0) ?? null;
    const eyebrow = `${fyLabel} AIP · ${total != null ? plural(total, "office", "offices") : "every office"}`;
    const queueHref = `/budget-planning/aip/review/search?mine=true${
      i.pendingFiscalYear != null ? `&fiscalYear=${i.pendingFiscalYear}` : ""
    }`;
    const counts = i.officeColumns ?? undefined;
    const waiting = i.pendingForPpdo;
    if (waiting == null) {
      // The count failed or is still loading: send them to the queue rather than guess a number.
      return {
        tone: "todo",
        eyebrow,
        headline: "Review the offices' AIPs.",
        reason: "Offices that have sent their AIP to PPDO are listed in the review queue.",
        action: { label: "Open the review queue", href: "/budget-planning/aip/review/search?mine=true" },
        steps: [],
        counts,
      };
    }
    if (waiting > 0) {
      const earlier =
        i.pendingFiscalYear != null && i.pendingFiscalYear !== fy
          ? `The earliest is for FY ${i.pendingFiscalYear}. `
          : "";
      return {
        tone: "todo",
        eyebrow,
        headline: `${plural(waiting, "office is", "offices are")} waiting for PPDO review.`,
        reason: `${earlier}Review each office's AIP, then accept it or return it with comments.`,
        action: { label: "Open the review queue", href: queueHref },
        steps: [],
        counts,
      };
    }
    return {
      tone: "waiting",
      eyebrow,
      headline: "No office is waiting for PPDO review.",
      reason: "Offices appear here when their department head sends the AIP to PPDO.",
      steps: [],
      counts,
    };
  }

  if (!i.loaded) return null;

  const steps = buildSteps(i);
  const head = i.isDepartmentHead;
  const status = i.workflowStatus as AipWorkflowState | null | undefined;
  const sinceDate = shortDate(i.workflowStatusSince);
  const entry = (label: string): BandLink => ({ label, href: i.aipEntryHref });

  // ── No ceiling yet ──
  if (i.ceiling == null) {
    if (i.canManageOfficeCeilings) {
      const others =
        i.officesWithoutCeiling != null && i.officesWithoutCeiling > 0
          ? ` ${plural(i.officesWithoutCeiling, "office is", "offices are")} waiting on them.`
          : "";
      return {
        tone: "blocking",
        eyebrow: `${fyLabel} AIP · ${i.officeCode}`,
        headline: `Publish the ${fyLabel} ceilings.${others}`,
        reason: "Offices can encode without one, but none can send its AIP to PPDO until it is published.",
        action: { label: "Set ceilings", href: i.allocationHref },
        steps,
      };
    }
    return {
      tone: "waiting",
      eyebrow: `${fyLabel} AIP · ${i.officeCode}`,
      headline: `Waiting for the ${fyLabel} ceiling. You can start encoding now.`,
      reason: "Sending the AIP to PPDO opens once the ceiling is published.",
      action: entry("Open AIP Entry"),
      steps,
    };
  }

  // ── FY2027 and earlier: no review workflow ──
  if (fy == null || fy < i.firstEnteredYear) {
    return {
      tone: "info",
      eyebrow: `${fyLabel} AIP · ${i.officeCode}`,
      headline: `${fyLabel} uses the uploaded AIP. Review and submission start with FY ${i.firstEnteredYear}.`,
      reason: i.hasAip ? `${plural(i.activityCount, "activity", "activities")} in this office's AIP.` : undefined,
      action: entry("Open AIP Entry"),
      steps,
    };
  }

  // ── PPDO finance: the split comes before anyone can encode ──
  // Only while the AIP is still with the office (Draft or never started): once it has moved on, the
  // money is settled and the band follows the AIP like everyone else's.
  if (i.isHost && i.canManageAllocation && (status == null || status === "Draft")) {
    const unallocated =
      i.officeCeilingAllFunds != null ? i.officeCeilingAllFunds - i.allocatedToDivisions : 0;
    const unassigned = i.unassignedProgramCount;
    if (unallocated > 0 || unassigned > 0) {
      const noDivision = `${plural(unassigned, "program has", "programs have")} no division`;
      return {
        tone: "todo",
        eyebrow: `${fyLabel} AIP · ${i.officeCode}`,
        headline:
          unallocated > 0
            ? `${peso(unallocated)} of the ceiling is not allocated to a division yet.`
            : `${noDivision}, so nobody can encode them.`,
        reason:
          unallocated > 0 && unassigned > 0 ? `${noDivision} either, so nobody can encode them.` : undefined,
        action: {
          label: unallocated > 0 ? "Allocate to divisions" : "Assign programs",
          href: i.allocationHref,
        },
        secondary:
          unallocated > 0 && unassigned > 0 ? { label: "Assign programs", href: i.allocationHref } : undefined,
        steps,
      };
    }
  }

  // ── Nothing encoded ──
  // ⚠️ An office can have AIP groups (seeded from its LDIP) and still no activities: that is
  // "nothing encoded", not "every activity is costed" (found on the local PBO office).
  if (!i.hasAip || i.activityCount === 0) {
    return {
      tone: head ? "waiting" : "todo",
      eyebrow: `${fyLabel} AIP · ${i.officeCode}`,
      headline: `Start the ${fyLabel} AIP. Nothing is encoded yet.`,
      reason: head ? "Your encoders enter the activities; it comes to you once they submit it." : undefined,
      action: entry("Open AIP Entry"),
      steps,
    };
  }

  const over = i.costedAgainstCeiling - i.ceiling;
  const overLine = `${peso(over)} over the General Fund ceiling.`;

  switch (status) {
    case "DepartmentReview":
      if (!head) {
        return {
          tone: "waiting",
          eyebrow: `${fyLabel} AIP · ${i.officeCode}`,
          headline: `${since("With your department head", i.workflowStatusSince)}.`,
          reason: "They review it, then send it to PPDO or return it to you with comments.",
          action: entry("View the AIP"),
          steps,
        };
      }
      // ⚠️ The ceiling blocks the send to PPDO only (PPDO-146): this is the hop where it bites.
      if (over > 0) {
        return {
          tone: "blocking",
          eyebrow: `${fyLabel} AIP · ${since("with you", i.workflowStatusSince)}`,
          headline: `${overLine} It cannot go to PPDO until it fits.`,
          reason: "Lower the costing, or return the AIP to your encoders with comments.",
          action: entry("Open AIP Entry"),
          secondary: entry("Return to encoders"),
          steps,
        };
      }
      return {
        tone: "todo",
        eyebrow: `${fyLabel} AIP · ${since("with you", i.workflowStatusSince)}`,
        headline: "Your encoders are done. The AIP is ready for you to send to PPDO.",
        reason: "Check it in AIP Entry, then send it to PPDO or return it with comments.",
        action: entry("Review and send to PPDO"),
        secondary: entry("Return to encoders"),
        steps,
      };

    case "SubmittedToPpdo":
      return {
        tone: "waiting",
        eyebrow: `${fyLabel} AIP · ${i.officeCode}`,
        headline: `${since("With PPDO", i.workflowStatusSince)}. Nothing to do until they respond.`,
        reason: "If PPDO returns it, the comments will be on the activities in AIP Entry.",
        action: entry("View the AIP"),
        steps,
      };

    case "ReturnedByPpdo": {
      // An office with divisions: they resubmit before the head can send it again (PPDO-149).
      const divisionsLeft =
        i.divisionsRequired != null &&
        i.divisionsRequired > 0 &&
        (i.divisionsSubmitted ?? 0) < i.divisionsRequired
          ? `${i.divisionsSubmitted ?? 0} of ${divisionsOf(i.divisionsSubmitted ?? 0, i.divisionsRequired)} submitted again${
              (i.divisionsWaiting ?? []).length > 0 ? `; waiting on ${listNames(i.divisionsWaiting ?? [])}` : ""
            }. `
          : "";
      return {
        tone: over > 0 && head ? "blocking" : "returned",
        eyebrow: `${fyLabel} AIP · ${since("returned", i.workflowStatusSince)}`,
        headline: `PPDO returned the AIP${comments(i.unresolvedComments)}.`,
        reason: head
          ? over > 0
            ? `${divisionsLeft}It is also ${overLine.charAt(0).toLowerCase()}${overLine.slice(1)} Fix it, or return it to your encoders, then send it to PPDO again.`
            : `${divisionsLeft}Fix the AIP or return it to your encoders, then send it to PPDO again.`
          : `${divisionsLeft}Your department head sends it back to PPDO once it is fixed.`,
        action: entry("Open the comments"),
        secondary: head ? entry("Return to encoders") : undefined,
        steps,
      };
    }

    case "Consolidated":
      return {
        tone: "done",
        eyebrow: `${fyLabel} AIP · accepted`,
        headline: `PPDO accepted the AIP${sinceDate ? ` on ${sinceDate}` : ""}. It is part of the provincial AIP.`,
        // The Report is PPDO-only (PPDO-20); a guest office reads its accepted AIP in AIP Entry.
        action: i.isHost ? { label: "Open Report", href: i.reportHref } : entry("View the AIP"),
        steps,
      };

    default: {
      // Draft, or no state yet.
      const by = returnedBy(i.lastHandOff);
      if (by != null) {
        const who = by === "ppdo" ? "PPDO" : "Your department head";
        if (head) {
          return {
            tone: "waiting",
            eyebrow: `${fyLabel} AIP · ${since("with your encoders", i.workflowStatusSince)}`,
            headline:
              by === "ppdo"
                ? "With your encoders after PPDO's return."
                : `You returned the AIP to your encoders${sinceDate ? ` on ${sinceDate}` : ""}.`,
            reason: "It comes back to you when they submit it again.",
            action: entry("View the AIP"),
            steps,
          };
        }
        return {
          tone: "returned",
          eyebrow: `${fyLabel} AIP · ${since("returned", i.workflowStatusSince)}`,
          headline: `${who} returned the AIP${comments(i.unresolvedComments)}.`,
          reason:
            by === "ppdo"
              ? "Make the changes, then submit it to your department head again."
              : "Make the changes, then submit it again.",
          action: entry("Open the comments"),
          steps,
        };
      }

      // An office with divisions: each division submits before the department head sees it.
      if (
        i.divisionsRequired != null &&
        i.divisionsRequired > 0 &&
        (i.divisionsSubmitted ?? 0) < i.divisionsRequired
      ) {
        const waitingOn = i.divisionsWaiting ?? [];
        return {
          tone: "waiting",
          eyebrow: `${fyLabel} AIP · with the divisions`,
          headline: `${i.divisionsSubmitted ?? 0} of ${divisionsOf(i.divisionsSubmitted ?? 0, i.divisionsRequired)} submitted.${
            waitingOn.length > 0 ? ` Waiting on ${listNames(waitingOn)}.` : ""
          }`,
          reason:
            i.uncostedActivityCount > 0
              ? `${plural(i.uncostedActivityCount, "activity", "activities")} still ${i.uncostedActivityCount === 1 ? "has" : "have"} no cost.`
              : undefined,
          action: entry("Open AIP Entry"),
          steps,
        };
      }

      const overNote = over > 0 ? ` It is ${overLine.charAt(0).toLowerCase()}${overLine.slice(1)} PPDO will not take it until it fits.` : "";
      if (i.uncostedActivityCount > 0) {
        return {
          tone: head ? "waiting" : "todo",
          eyebrow: `${fyLabel} AIP · ${head ? "with your encoders" : "with your office"}`,
          headline: `${plural(i.uncostedActivityCount, "activity still has", "activities still have")} no cost.`,
          reason: head
            ? `Your encoders are costing the AIP. It comes to you once they submit it.${overNote}`
            : `Cost them, then submit the AIP to your department head.${overNote}`,
          action: entry("Open AIP Entry"),
          steps,
        };
      }
      return {
        tone: head ? "waiting" : "todo",
        eyebrow: `${fyLabel} AIP · ${head ? "with your encoders" : "with your office"}`,
        headline: head
          ? "Every activity is costed. Waiting for your encoders to submit it."
          : "Every activity is costed. Submit the AIP to your department head.",
        reason: overNote ? overNote.trim() : undefined,
        action: entry("Open AIP Entry"),
        steps,
      };
    }
  }
}
