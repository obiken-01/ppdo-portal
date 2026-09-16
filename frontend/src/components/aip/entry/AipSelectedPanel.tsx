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
  AipProjectDetail, FundingSourceResponse, PriceIndexPickerItem,
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
  defaultImplementingOffice, onSelect, onChangeActivity, onProjectAdded, onActivityAdded,
  onDeleted, onActivityTotals, onActivityDetails,
}: {
  selection: AipResolvedSelection | null;
  canEdit: boolean;
  holder: string;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
  generalFundId: number | null;
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  defaultImplementingOffice: string | null;
  onSelect: (ids: AipSelectionIds) => void;
  onChangeActivity: () => void;
  onProjectAdded: (project: AipProjectDetail) => void;
  onActivityAdded: (activity: AipActivityDetail) => void;
  onDeleted: (result: AipDeleteResult) => void;
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
    const { program, activity } = selection;
    return (
      <AipActivityPanel
        activity={activity}
        canEdit={canEdit}
        accounts={accounts}
        funds={funds}
        generalFundId={generalFundId}
        priceIndex={priceIndex}
        priceIndexLoading={priceIndexLoading}
        defaultImplementingOffice={defaultImplementingOffice}
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
    return (
      <AipProjectPanel
        project={project}
        canEdit={canEdit}
        lockedReason={holder}
        defaultImplementingOffice={defaultImplementingOffice}
        unresolvedCount={unresolvedCount}
        onSelectActivity={(activityId) =>
          onSelect({ programId: program!.id, projectId: project.id, activityId })
        }
        onActivityAdded={onActivityAdded}
        onDeleted={onDeleted}
      />
    );
  }

  return (
    <AipProgramPanel
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
