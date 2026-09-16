"use client";

/**
 * AIP Entry's context picker — Program → Project → Activity (PPDO-89, spec decision 1).
 *
 * ⚠️ **The WFP entry page's picker, deliberately the same.** At the 2026-09-15 PDC demo the AIP
 * tree read as overwhelming: entering one activity meant looking at every other PPA in the office.
 * WFP never had that problem because it picks a node first, and the encoders already know that
 * shape from using it — so this mirrors `wfp/entry/page.tsx`'s picker rather than inventing a
 * second way to do the same thing.
 *
 * ⚠️ **Each lookup clears its children.** Changing the program while a project and activity from
 * the old program are still selected would leave the panel showing a node that is no longer under
 * the picker describing it.
 *
 * ⚠️ **No sub-office group picker** (decision 2). The Program lookup lists every group's programs
 * and names the group on the option when the office has more than one — most offices have exactly
 * one, and a fourth step for all of them to disambiguate for the few is the wrong trade.
 */

import Lookup from "@/components/ui/Lookup";
import type { AipActivityDetail, AipProjectDetail } from "@/types";
import { useAipUnresolvedCount } from "./AipComments";
import { AipUnresolvedBadge } from "./AipEntryPanelParts";
import { aipRefSegment, type AipProgramOption, type AipSelectionIds } from "./AipEntrySelection";

export default function AipEntryPicker({
  programOptions, severalGroups, ids, projects, activities, onChange, activityInputRef,
}: {
  programOptions: AipProgramOption[];
  /** Show the group name on each program option — only worth the row height when there are two. */
  severalGroups: boolean;
  ids: AipSelectionIds;
  /** The selected program's projects, empty when no program is picked. */
  projects: AipProjectDetail[];
  /** The selected project's activities, empty when no project is picked. */
  activities: AipActivityDetail[];
  onChange: (ids: AipSelectionIds) => void;
  /** So "Change activity" can put the cursor back in the box it just cleared. */
  activityInputRef?: React.Ref<HTMLInputElement>;
}) {
  // ⚠️ Read HERE rather than passed in. The page renders the comments provider, so a count read in
  // the page body would sit outside its own provider and every badge would read zero — silently,
  // because zero is also what "no comments" looks like.
  const unresolvedCount = useAipUnresolvedCount();

  return (
    <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
      <Field label="Program">
        <Lookup
          items={programOptions}
          value={ids.programId}
          onChange={(programId) => onChange({ programId, projectId: null, activityId: null })}
          getId={(o) => o.program.id}
          getLabel={(o) => `${o.program.refCode} — ${o.program.name}`}
          // ⚠️ The group is searchable even though it is not in the label: in an office with two
          // groups the encoder thinks in groups ("the PDC one"), and a name they can see on the
          // option but cannot type is a dead end.
          getSearchText={(o) => `${o.program.name} ${o.program.refCode} ${o.group.name}`}
          renderOption={(o) => (
            <span className="flex flex-col gap-0.5">
              <span className="flex items-center gap-2">
                <span className="font-mono text-xs text-slate-600">{o.program.refCode}</span>
                <span className="min-w-0 flex-1">{o.program.name}</span>
                <AipUnresolvedBadge count={unresolvedCount("Program", o.program.id)} />
              </span>
              {severalGroups && (
                <span className="text-[11px] text-slate-600">{o.group.name}</span>
              )}
            </span>
          )}
          placeholder="Search program…"
        />
      </Field>

      <Field label="Project">
        <Lookup
          items={projects}
          value={ids.projectId}
          onChange={(projectId) =>
            onChange({ programId: ids.programId, projectId, activityId: null })
          }
          getId={(p) => p.id}
          getLabel={(p) => `${aipRefSegment(p.refCode)} — ${p.name}`}
          getSearchText={(p) => `${p.name} ${p.refCode}`}
          renderOption={(p) => (
            <span className="flex items-center gap-2">
              <span className="font-mono text-xs text-slate-600">{aipRefSegment(p.refCode)}</span>
              <span className="min-w-0 flex-1">{p.name}</span>
              <AipUnresolvedBadge count={unresolvedCount("Project", p.id)} />
            </span>
          )}
          placeholder={ids.programId == null ? "Pick a program first" : "Search project…"}
          disabled={ids.programId == null}
        />
      </Field>

      <Field label="Activity">
        <Lookup
          items={activities}
          value={ids.activityId}
          onChange={(activityId) =>
            onChange({ programId: ids.programId, projectId: ids.projectId, activityId })
          }
          getId={(a) => a.id}
          // ⚠️ Names can carry the encoder's own line breaks (PPDO-85); the closed input is one
          // line, so they are flattened here rather than stretching the picker row.
          getLabel={(a) => `${aipRefSegment(a.refCode)} — ${oneLine(a.name)}`}
          getSearchText={(a) => `${a.name} ${a.refCode}`}
          renderOption={(a) => (
            <span className="flex items-center gap-2">
              <span className="font-mono text-xs text-slate-600">{aipRefSegment(a.refCode)}</span>
              <span className="min-w-0 flex-1">{oneLine(a.name)}</span>
              <AipUnresolvedBadge count={unresolvedCount("Activity", a.id)} />
            </span>
          )}
          placeholder={ids.projectId == null ? "Pick a project first" : "Search activity…"}
          disabled={ids.projectId == null}
          inputRef={activityInputRef}
        />
      </Field>
    </div>
  );
}

function oneLine(name: string): string {
  return name.replace(/\s*\n\s*/g, " ");
}

function Field({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div>
      <label className="mb-1 block text-xs font-medium uppercase tracking-wide text-slate-600">
        {label}
      </label>
      {children}
    </div>
  );
}
