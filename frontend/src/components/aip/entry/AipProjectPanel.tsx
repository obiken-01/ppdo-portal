"use client";

/**
 * The Project level of AIP Entry's drill-down (PPDO-89, spec decision 3).
 *
 * Header, a reserved **Project details** section, then the project's activities and + Add activity.
 *
 * ⚠️ The delete control is the one PPDO-88 shipped, moved here from the tree it was an interim
 * home for. PPDO-91 owns its final copy and the renumber notice; carrying it across rather than
 * dropping it keeps a capability the office already has.
 */

import { useMemo } from "react";
import type {
  AipActivityDetail, AipCommentNodeType, AipDeleteResult, AipProjectDetail,
} from "@/types";
import { addAipActivity } from "@/lib/aip";
import { AipCommentAnchor } from "./AipComments";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "./AipHierarchy";
import { AipFigureStrip, sumActivityAmounts } from "./AipRowFigures";
import AipDeleteNodeButton from "./AipDeleteNodeButton";
import {
  AipChildList, AipChildRow, AipInlineAdd, AipPanel,
} from "./AipEntryPanelParts";

export default function AipProjectPanel({
  project, canEdit, lockedReason, defaultImplementingOffice, unresolvedCount,
  onSelectActivity, onActivityAdded, onDeleted,
}: {
  project: AipProjectDetail;
  canEdit: boolean;
  lockedReason: string;
  /** The encoder's own office code, written onto a new activity at create (PPDO-80). */
  defaultImplementingOffice: string | null;
  unresolvedCount: (nodeType: AipCommentNodeType, nodeId: number) => number;
  onSelectActivity: (activityId: number) => void;
  onActivityAdded: (activity: AipActivityDetail) => void;
  onDeleted: (result: AipDeleteResult) => void;
}) {
  const amounts = useMemo(() => sumActivityAmounts(project.activities), [project]);

  return (
    <AipPanel>
      <div className={`px-3 py-2 ${aipHeaderRow("project")}`}>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
          <div className="min-w-0">
            <div className="flex items-center gap-2">
              <AipLevelChip level="project" />
              <AipRefCode code={project.refCode} />
            </div>
            <p className="mt-0.5 text-sm font-medium text-slate-800">{project.name}</p>
          </div>
          <div className="flex flex-wrap items-center justify-end gap-x-6 gap-y-2">
            <AipFigureStrip amounts={amounts} />
            {canEdit && (
              <AipDeleteNodeButton target={{ kind: "Project", project }} onDeleted={onDeleted} />
            )}
          </div>
        </div>
        <AipCommentAnchor nodeType="Project" nodeId={project.id} />
      </div>

      {/* ⚠️ Reserved, and said out loud rather than left as a blank box (spec decision 3 / §7).
          The PDC asked for project-level fields on 2026-09-15; the field list is not agreed yet,
          so the section holds its place and tells the encoder there is nothing for them to do in
          it. An unexplained empty panel reads as a page that failed to load. */}
      <section className="border border-slate-200 bg-slate-50 px-3 py-2">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-800">
          Project details
        </h3>
        <p className="mt-0.5 text-xs text-slate-600">
          Nothing to fill in here yet — the project-level fields are still being agreed with the PDC.
        </p>
      </section>

      <AipChildList
        title="Activities"
        count={project.activities.length}
        emptyText="No activities yet."
        footer={
          <AipInlineAdd
            label="+ Add activity"
            placeholder="Activity description"
            disabled={!canEdit}
            disabledReason={`With ${lockedReason} — activities cannot be added here.`}
            onAdd={async (name) => {
              // ⚠️ The created node is USED, not discarded, and the implementing office is written
              // at CREATE rather than only prefilled in the edit form — an activity nobody opens
              // afterwards still has to print one, and it is the encoder's own office in all but
              // the joint case (PPDO-80).
              onActivityAdded(await addAipActivity(project.id, {
                name, esreCode: null, implementingOffice: defaultImplementingOffice,
                startDate: null, endDate: null, expectedOutputs: null,
                fundingSourceRaw: null, ps: null, mooe: null, co: null,
                ccAdaptation: null, ccMitigation: null, ccTypologyCode: null,
              }));
            }}
          />
        }
      >
        {project.activities.map((activity) => (
          <AipChildRow
            key={activity.id}
            refCode={activity.refCode}
            name={activity.name}
            total={activity.total}
            unresolved={unresolvedCount("Activity", activity.id)}
            onSelect={() => onSelectActivity(activity.id)}
          />
        ))}
      </AipChildList>
    </AipPanel>
  );
}
