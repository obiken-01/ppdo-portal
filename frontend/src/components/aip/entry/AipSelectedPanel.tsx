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
  offices, onSelect, onChangeActivity, onProjectAdded, onActivityAdded,
  onDeleted, onProjectUpdated, onActivityTotals, onActivityDetails,
}: {
  selection: AipResolvedSelection | null;
  canEdit: boolean;
  holder: string;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
  generalFundId: number | null;
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  /** Configured offices for the implementing-office picker (PPDO-100). */
  offices: OfficeResponse[];
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
    return (
      <AipActivityPanel
        // ⚠️ Keyed, so each activity gets its OWN instance. ↩️ The tree this replaced keyed every
        // row (`<ActivityBlock key={activity.id}>`) and the drill-down dropped it, which is not a
        // cosmetic difference: one shared instance carries the previous activity's in-flight
        // expenditure refetch, a failed delete's error text and a half-typed add across the switch.
        key={activity.id}
        activity={activity}
        canEdit={canEdit}
        lockedReason={holder}
        isLastSibling={isLastActivity}
        accounts={accounts}
        funds={funds}
        generalFundId={generalFundId}
        priceIndex={priceIndex}
        priceIndexLoading={priceIndexLoading}
        offices={offices}
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
        isLastSibling={isLastProject}
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
