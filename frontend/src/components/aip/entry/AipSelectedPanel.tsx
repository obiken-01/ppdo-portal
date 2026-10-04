"use client";

/**
 * Which of AIP Entry's three panels is on screen (PPDO-89).
 *
 * ⚠️ **One level, chosen deepest-first.** The whole point of the drill-down is that an encoder
 * entering one activity is not also looking at its siblings' rows, so this never renders two
 * levels at once — a "show the project too" convenience here would walk the page back to the tree
 * it replaced one prop at a time.
 *
 * ⚠️ Kept out of `page.tsx`. The page was 784 lines before this ticket and the spec's own
 * instruction was that the new layout must SHRINK it by moving the panels out
 * (`docs/v1.8/AIP_Entry_Layout_Spec.md` §6, `RETROSPECTIVE.md`).
 */

import type {
  AccountResponse, AipActivityDetail, AipDeleteResult, AipExpenditureWriteResult,
  AipProjectDetail, FundingSourceResponse, OfficeResponse, PriceIndexPickerItem,
} from "@/types";
import AipProgramPanel from "./AipProgramPanel";
import AipProjectPanel from "./AipProjectPanel";
import AipActivityPanel from "./AipActivityPanel";
import { useAipUnresolvedCount } from "./AipComments";
import { activityDivisionLock, type AipDivisionView } from "./AipDivisionParts";
import {
  EMPTY_SELECTION_IDS, type AipResolvedSelection, type AipSelectionIds,
} from "./AipEntrySelection";

/**
 * Renders the deepest selected level and nothing else (spec decision 3).
 *
 * ⚠️ Deepest-first, so there is never a moment where two levels are on screen: the whole point of
 * the redesign is that an encoder entering one activity is not also looking at its siblings' rows.
 */
export default function AipSelectedPanel({
  selection, canEdit, holder, accounts, funds, generalFundId, priceIndex, priceIndexLoading,
  offices, proponentOfficeCode, onSelect, onChangeActivity, onProjectAdded, onActivityAdded,
  onDeleted, onProjectUpdated, onActivityTotals, onActivityDetails, divisionView = null,
  renderProposalSlot,
}: {
  selection: AipResolvedSelection | null;
  /**
   * The office-level gate: the office is in its own hands (and, in a divisioned office, the
   * division status has loaded and the reader is assigned). An activity is further narrowed by
   * its own server-computed `canEdit` below.
   */
  canEdit: boolean;
  /** PPDO-151 — the caller's place in the division flow; null outside it. */
  divisionView?: AipDivisionView | null;
  /**
   * Demo 2.15 (PPDO-159) — the Investment proposal strip for a project, shown in its panel. AIP
   * Entry passes it; AIP Review does not, so the review screen has no strip.
   */
  renderProposalSlot?: (projectId: number) => React.ReactNode;
  /** Passed straight through as each panel's `lockedReason`. Null on AIP Review (PPDO-94) — there is
   * no holder to name, so the add/delete controls are omitted rather than disabled with a reason. */
  holder: string | null;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
  generalFundId: number | null;
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  /** Configured offices for the implementing-office picker (PPDO-100). */
  offices: OfficeResponse[];
  /** This office’s own code — always saved, always printed first, never a chip. */
  proponentOfficeCode: string | null;
  onSelect: (ids: AipSelectionIds) => void;
  onChangeActivity: () => void;
  onProjectAdded: (project: AipProjectDetail) => void;
  onActivityAdded: (activity: AipActivityDetail) => void;
  onDeleted: (result: AipDeleteResult) => void;
  onProjectUpdated: (
    patch: Pick<AipProjectDetail, "id" | "name" | "description" | "objective">
  ) => void;
  onActivityTotals: (result: AipExpenditureWriteResult) => void;
  onActivityDetails: (updated: AipActivityDetail) => void;
}) {
  const unresolvedCount = useAipUnresolvedCount();

  if (!selection?.program) {
    return (
      <p className="border border-slate-200 bg-white px-4 py-6 text-center text-sm text-slate-600">
        Pick a program to start.
      </p>
    );
  }

  if (selection.activity && selection.project && selection.program) {
    const { program, project, activity } = selection;
    const isLastActivity = project.activities[project.activities.length - 1]?.id === activity.id;
    // ⚠️ AND-ed with the server's per-activity flag (PPDO-148), never re-derived here. On an
    // office without divisions the two agree, so nothing changes there.
    const activityCanEdit = canEdit && activity.canEdit;
    // A division lock names itself in the panel; the delete control's "With {holder}" would name
    // the office's holder instead, which is wrong while the office is still in its own hands.
    const divisionLocked = activityDivisionLock(activity, divisionView) != null;
    return (
      <AipActivityPanel
        // ⚠️ Keyed, so each activity gets its OWN instance. ↩️ The tree this replaced keyed every
        // row (`<ActivityBlock key={activity.id}>`) and the drill-down dropped it, which is not a
        // cosmetic difference: one shared instance carries the previous activity's in-flight
        // expenditure refetch, a failed delete's error text and a half-typed add across the switch.
        key={activity.id}
        activity={activity}
        canEdit={activityCanEdit}
        lockedReason={divisionLocked ? null : holder}
        divisionView={divisionView}
        isLastSibling={isLastActivity}
        accounts={accounts}
        funds={funds}
        generalFundId={generalFundId}
        priceIndex={priceIndex}
        priceIndexLoading={priceIndexLoading}
        offices={offices}
        proponentOfficeCode={proponentOfficeCode}
        onTotals={onActivityTotals}
        onDetails={onActivityDetails}
        onDeleted={onDeleted}
        onChangeActivity={onChangeActivity}
        onChangeProject={() =>
          onSelect({ programId: program.id, projectId: null, activityId: null })
        }
        onDone={() => onSelect(EMPTY_SELECTION_IDS)}
      />
    );
  }

  if (selection.project) {
    const { program, project } = selection;
    const isLastProject = program.projects[program.projects.length - 1]?.id === project.id;
    return (
      <AipProjectPanel
        key={project.id}
        project={project}
        canEdit={canEdit}
        lockedReason={holder}
        divisionView={divisionView}
        proposalSlot={renderProposalSlot?.(project.id) ?? null}
        isLastSibling={isLastProject}
        proponentOfficeCode={proponentOfficeCode}
        unresolvedCount={unresolvedCount}
        onSelectActivity={(activityId) =>
          onSelect({ programId: program!.id, projectId: project.id, activityId })
        }
        onActivityAdded={onActivityAdded}
        onDeleted={onDeleted}
        onUpdated={onProjectUpdated}
      />
    );
  }

  return (
    <AipProgramPanel
      key={selection.program.id}
      program={selection.program}
      canEdit={canEdit}
      lockedReason={holder}
      unresolvedCount={unresolvedCount}
      onSelectProject={(projectId) =>
        onSelect({ programId: selection.program!.id, projectId, activityId: null })
      }
      onProjectAdded={onProjectAdded}
    />
  );
}
