"use client";

/**
 * AIP Review's whole-office tree (PPDO-94) — extracted from `aip/review/page.tsx` unchanged
 * (spec decision 4: "today's tree, moved, not rewritten"). This is the **Full office** view; the
 * **Drill-down** view is the default and reuses AIP Entry's picker and panels instead.
 *
 * ⚠️ Kept structurally identical to AIP Entry's group block on purpose — same levels, same tints,
 * same figure strip. A reviewer and an encoder discussing "the second project" must be looking at
 * the same thing, and the comment anchors are attached to the same rows in both.
 */

import { useEffect, useMemo, useState } from "react";
import { listAipExpenditures } from "@/lib/aip";
import { AipCommentAnchor } from "@/components/aip/entry/AipComments";
import AipActivityFields from "@/components/aip/entry/AipActivityFields";
import AipExpenditureTable from "@/components/aip/entry/AipExpenditureTable";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "@/components/aip/entry/AipHierarchy";
import {
  AipFigureStrip, AipFundPill, AipUnitCaption, activityFundLabel, sumActivityAmounts,
  type AipRowAmounts,
} from "@/components/aip/entry/AipRowFigures";
import type {
  AipActivityDetail, AipExpenditure, AipOfficeDetail, AccountResponse, FundingSourceResponse,
} from "@/types";

export default function AipReviewFullTree({
  groups, accounts, funds,
}: {
  groups: AipOfficeDetail[];
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
}) {
  return (
    <div className="space-y-4">
      {groups.map((group) => (
        <GroupBlock key={group.id} group={group} accounts={accounts} funds={funds} />
      ))}
    </div>
  );
}

// ── One sub-office group, read-only ───────────────────────────────────────

function GroupBlock({
  group, accounts, funds,
}: {
  group: AipOfficeDetail;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
}) {
  const amounts: AipRowAmounts = useMemo(
    () => sumActivityAmounts(
      group.programs.flatMap((p) => p.projects.flatMap((j) => j.activities))
    ),
    [group]
  );

  return (
    <div className="border border-slate-200 bg-white">
      <div className={`border-b border-b-slate-200 px-4 py-3 ${aipHeaderRow("office")}`}>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
          <div>
            <div className="flex items-center gap-2">
              <AipLevelChip level="office" />
              <AipRefCode code={group.refCode} />
            </div>
            <h2 className="mt-1 text-sm font-semibold uppercase tracking-wide text-slate-800">{group.name}</h2>
            <p className="mt-0.5 text-xs text-slate-600">
              {group.sector} · {group.programs.length} program{group.programs.length === 1 ? "" : "s"}
            </p>
          </div>
          <div className="flex flex-col items-end gap-1">
            <AipFigureStrip amounts={amounts} emphasis="strong" />
            <AipUnitCaption />
          </div>
        </div>
      </div>

      <div className="divide-y divide-slate-200">
        {group.programs.map((program) => (
          <div key={program.id} className="ml-3">
            <div className={`px-4 py-2 ${aipHeaderRow("program")}`}>
              <div className="flex items-center gap-2">
                <AipLevelChip level="program" />
                <AipRefCode code={program.refCode} />
              </div>
              <p className="mt-0.5 text-sm font-semibold text-slate-800">{program.name}</p>
              <AipCommentAnchor nodeType="Program" nodeId={program.id} />
            </div>

            <div className="mt-2 space-y-3 px-4 pb-3 pl-4">
              {program.projects.map((project) => (
                <div key={project.id}>
                  <div className={`flex flex-wrap items-center gap-2 px-3 py-1.5 ${aipHeaderRow("project")}`}>
                    <AipLevelChip level="project" />
                    <AipRefCode code={project.refCode} />
                    <span className="text-sm font-medium text-slate-800">{project.name}</span>
                  </div>
                  <div className="px-3">
                    <AipCommentAnchor nodeType="Project" nodeId={project.id} />
                  </div>
                  <div className="mt-2 space-y-2 pl-4">
                    {project.activities.map((activity) => (
                      <ActivityBlock key={activity.id} activity={activity}
                        accounts={accounts} funds={funds} />
                    ))}
                    {project.activities.length === 0 && (
                      <p className="text-xs text-slate-600">No activities under this project.</p>
                    )}
                  </div>
                </div>
              ))}
              {program.projects.length === 0 && (
                <p className="text-xs text-slate-600">No projects under this program.</p>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── One activity, with its expenditure lines ──────────────────────────────

function ActivityBlock({
  activity, accounts, funds,
}: {
  activity: AipActivityDetail;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
}) {
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState<AipExpenditure[] | null>(null);

  // Lazily, and once: a reviewer opens a handful of activities out of an office's hundreds, and
  // fetching every activity's lines up front would be the N+1 in a different costume.
  useEffect(() => {
    if (!open || lines !== null) return;
    void listAipExpenditures(activity.id).then(setLines).catch(() => setLines([]));
  }, [open, lines, activity.id]);

  return (
    <div className="border border-slate-200 bg-white">
      <button type="button" onClick={() => setOpen((v) => !v)}
        className={`flex w-full flex-wrap items-start justify-between gap-x-4 gap-y-2 px-3 py-2 text-left hover:bg-green-25 ${aipHeaderRow("activity")}`}>
        <span className="flex flex-1 flex-wrap items-center gap-2">
          <span aria-hidden className="text-slate-300">{open ? "▾" : "▸"}</span>
          <AipLevelChip level="activity" />
          <AipRefCode code={activity.refCode} />
          <span className="whitespace-pre-line text-sm text-slate-800">{activity.name}</span>
          <AipFundPill label={activityFundLabel(activity)} />
        </span>
        <AipFigureStrip amounts={activity} />
      </button>

      {/* ⚠️ Outside the disclosure <button>: nesting a button in a button is invalid HTML, and the
          click would toggle the activity instead of the comment thread. */}
      <div className="px-3 pb-1">
        <AipCommentAnchor nodeType="Activity" nodeId={activity.id} />
      </div>

      {open && (
        <>
          {/* ⚠️ canEdit={false} on both. The PPDO reviewer never edits — decision 2, permanently.
              The components already render a read view in that mode, and the server refuses the
              write anyway; this is what keeps the control off the screen in the first place. */}
          {/* ⚠️ `offices={[]}` and that is correct, not a stub: the picker only renders in the edit
              view, and this call site is permanently read-only, so the read view prints the stored
              `PEO/PGSO` string as it stands. The prop stays REQUIRED rather than defaulting to []
              so an editable call site cannot forget it and get a silently empty picker. */}
          <AipActivityFields activity={activity} canEdit={false} onSaved={() => undefined} offices={[]} proponentOfficeCode={null} />

          {lines === null ? (
            <div className="space-y-2 px-4 py-3">
              {[0, 1].map((i) => <div key={i} className="h-4 w-full animate-pulse bg-slate-100" />)}
            </div>
          ) : (
            <AipExpenditureTable
              activityId={activity.id} lines={lines} accounts={accounts} fundingSources={funds}
              canEdit={false} generalFundId={null}
              // The picker only exists inside the editors, which cannot open here.
              priceIndex={[]} priceIndexLoading={false}
              onChanged={() => undefined} />
          )}
        </>
      )}
    </div>
  );
}
