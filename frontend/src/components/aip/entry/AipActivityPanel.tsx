"use client";

/**
 * The Activity level of AIP Entry's drill-down (PPDO-89, spec decision 3) — the leaf, and the only
 * level that carries fields.
 *
 * ⚠️ `AipActivityFields` and `AipExpenditureTable` are used **unchanged**. The redesign is about
 * what surrounds the activity, not about how one is costed; re-shaping the costing surface in the
 * same change would have put the two largest risks of this ticket on top of each other.
 *
 * ⚠️ The activity's expenditure lines are fetched HERE, per selected activity. The tree used to
 * fetch them lazily per expanded row; with one activity on screen that is the same request, made
 * once, and the panel remounts on selection so there is no stale-lines window.
 */

import { useEffect, useRef, useState } from "react";
import type {
  AccountResponse, AipActivityDetail, AipDeleteResult, AipExpenditure,
  AipExpenditureWriteResult, FundingSourceResponse, OfficeResponse, PriceIndexPickerItem,
} from "@/types";
import { aipErrorMessage, listAipExpenditures } from "@/lib/aip";
import AipActivityFields from "./AipActivityFields";
import AipExpenditureTable from "./AipExpenditureTable";
import AipDeleteNodeButton from "./AipDeleteNodeButton";
import { AipCommentAnchor } from "./AipComments";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "./AipHierarchy";
import { AipFigureStrip, AipFundPill, activityFundLabel } from "./AipRowFigures";
import { AipPanel, AipPanelError } from "./AipEntryPanelParts";

export default function AipActivityPanel({
  activity, canEdit, lockedReason, isLastSibling, accounts, funds, generalFundId, priceIndex, priceIndexLoading,
  offices, onTotals, onDetails, onDeleted,
  onChangeActivity, onChangeProject, onDone,
}: {
  activity: AipActivityDetail;
  canEdit: boolean;
  /** Who holds the work, when this office cannot edit — shown as the delete control's reason. */
  lockedReason: string;
  /** Whether this is the last activity in its project — omits the renumber sentence (spec §6). */
  isLastSibling: boolean;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
  generalFundId: number | null;
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  /** Configured offices for the implementing-office picker (PPDO-100). */
  offices: OfficeResponse[];
  onTotals: (result: AipExpenditureWriteResult) => void;
  onDetails: (updated: AipActivityDetail) => void;
  onDeleted: (result: AipDeleteResult) => void;
  onChangeActivity: () => void;
  onChangeProject: () => void;
  onDone: () => void;
}) {
  const [lines, setLines] = useState<AipExpenditure[] | null>(null);
  const [linesError, setLinesError] = useState<string | null>(null);

  // ⚠️ Which activity the panel is currently showing, for the refetch below — belt and braces
  // beside the caller's `key`. A component that silently renders another row's money if someone
  // forgets to key it is not a safe thing to leave lying around.
  const showing = useRef(activity.id);
  useEffect(() => { showing.current = activity.id; }, [activity.id]);

  useEffect(() => {
    let live = true;
    setLines(null);
    setLinesError(null);
    listAipExpenditures(activity.id)
      .then((l) => { if (live) setLines(l); })
      // ⚠️ Said out loud, not rendered as an empty table. Zero lines is "not costed", which is a
      // submit-blocking state an encoder would then go and "fix" by re-entering costing that is
      // already on the record.
      .catch((e) => {
        if (live) setLinesError(aipErrorMessage(e, "This activity’s expenditure lines could not be loaded."));
      });
    return () => { live = false; };
  }, [activity.id]);

  return (
    <AipPanel>
      <div className={`px-3 py-2 ${aipHeaderRow("activity")}`}>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
          <div className="min-w-0">
            <div className="flex flex-wrap items-center gap-2">
              <AipLevelChip level="activity" />
              <AipRefCode code={activity.refCode} />
              {/* The form's Funding Source column (7) — beside the name, never among the numbers. */}
              <AipFundPill label={activityFundLabel(activity)} />
            </div>
            {/* The encoder's own line breaks are kept (PPDO-85). */}
            <p className="mt-0.5 whitespace-pre-line text-sm text-slate-800">{activity.name}</p>
          </div>
          <div className="flex flex-wrap items-center justify-end gap-x-6 gap-y-2">
            <AipFigureStrip amounts={activity} />
            <AipDeleteNodeButton
              target={{ kind: "Activity", activity, isLastSibling }}
              canEdit={canEdit}
              lockedReason={lockedReason}
              onDeleted={onDeleted}
            />
          </div>
        </div>
        <AipCommentAnchor nodeType="Activity" nodeId={activity.id} />
      </div>

      {/* ⚠️ Above the lines, not below. eSRE blocks submit just as hard as a missing costing does,
          and an encoder who opens an activity to cost it should see what else it still needs in
          the same glance. */}
      <AipActivityFields activity={activity} canEdit={canEdit} onSaved={onDetails}
        offices={offices} />

      {linesError ? (
        <AipPanelError message={linesError} />
      ) : lines === null ? (
        <div className="space-y-2 px-1 py-3">
          {[0, 1].map((i) => <div key={i} className="h-4 w-full animate-pulse bg-slate-100" />)}
        </div>
      ) : (
        <AipExpenditureTable
          activityId={activity.id} lines={lines} accounts={accounts} fundingSources={funds}
          canEdit={canEdit} generalFundId={generalFundId}
          priceIndex={priceIndex} priceIndexLoading={priceIndexLoading}
          onChanged={(result) => {
            // Refetch just this activity's lines and hand the recomputed totals upward — the
            // record is never reloaded, so the panel stays where it is.
            //
            // ⚠️ `lines` is NOT cleared first: the table stays on screen while this lands, which is
            // the whole reason it is a separate fetch rather than a re-run of the effect above.
            // The id is re-checked on arrival instead, so a response that outlives the selection
            // cannot paint one activity's expenditures under another's name.
            const forActivity = activity.id;
            void listAipExpenditures(forActivity)
              .then((l) => { if (showing.current === forActivity) setLines(l); })
              .catch(() => undefined);
            onTotals(result);
          }} />
      )}

      {/* ⚠️ The same three exits as the WFP entry page, in the same order and wording (RAL-140):
          activity only · project and activity · everything. Encoders move between the two pages
          in one sitting, and a footer that agreed on two of the three would be worse than one
          that agreed on none. */}
      <div className="flex flex-wrap justify-end gap-3 border-t border-slate-200 pt-3">
        <button type="button" onClick={onChangeActivity}
          className="text-sm text-slate-600 hover:underline">Change activity</button>
        <button type="button" onClick={onChangeProject}
          className="text-sm text-slate-600 hover:underline">Change project</button>
        <button type="button" onClick={onDone}
          className="text-sm text-slate-600 hover:underline">Done</button>
      </div>
    </AipPanel>
  );
}
