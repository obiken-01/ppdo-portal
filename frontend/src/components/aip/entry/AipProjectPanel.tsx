"use client";

/**
 * The Project level of AIP Entry's drill-down (PPDO-89, spec decision 3).
 *
 * Header, the **Project details** form, then the project's activities and + Add activity.
 *
 * ⚠️ The delete control shipped as an interim in PPDO-88, moved here by PPDO-89, and got its final
 * copy — the counts, the conditional renumber sentence, disabled-with-reason — in PPDO-91.
 */

import { useMemo, useState } from "react";
import type {
  AipActivityDetail, AipCommentNodeType, AipDeleteResult, AipProjectDetail,
} from "@/types";
import { addAipActivity, aipErrorMessage, updateAipProject } from "@/lib/aip";
import { AipCommentAnchor } from "./AipComments";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "./AipHierarchy";
import { AipFigureStrip, sumActivityAmounts } from "./AipRowFigures";
import AipDeleteNodeButton from "./AipDeleteNodeButton";
import {
  AipChildList, AipChildRow, AipInlineAdd, AipPanel,
} from "./AipEntryPanelParts";

export default function AipProjectPanel({
  project, canEdit, lockedReason, isLastSibling, defaultImplementingOffice, unresolvedCount,
  onSelectActivity, onActivityAdded, onDeleted, onUpdated,
}: {
  project: AipProjectDetail;
  canEdit: boolean;
  lockedReason: string;
  /** Whether this is the last project in its program — omits the renumber sentence (spec §6). */
  isLastSibling: boolean;
  /** The encoder's own office code, written onto a new activity at create (PPDO-80). */
  defaultImplementingOffice: string | null;
  unresolvedCount: (nodeType: AipCommentNodeType, nodeId: number) => number;
  onSelectActivity: (activityId: number) => void;
  onActivityAdded: (activity: AipActivityDetail) => void;
  onDeleted: (result: AipDeleteResult) => void;
  /** The project's own fields after a save — never its activities, which the endpoint omits. */
  onUpdated: (patch: Pick<AipProjectDetail, "id" | "name" | "description" | "objective">) => void;
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
            <AipDeleteNodeButton
              target={{ kind: "Project", project, isLastSibling }}
              canEdit={canEdit}
              lockedReason={lockedReason}
              onDeleted={onDeleted}
            />
          </div>
        </div>
        <AipCommentAnchor nodeType="Project" nodeId={project.id} />
      </div>

      {/* ↩️ This section was RESERVED and empty from PPDO-89 until PPDO-99: the PDC asked for
          project-level fields on 2026-09-15 without naming them, and the list (title, description,
          objective) arrived on 2026-09-16. */}
      <AipProjectDetails
        project={project}
        canEdit={canEdit}
        lockedReason={lockedReason}
        onSaved={onUpdated}
      />

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

/**
 * The **Project details** form (PPDO-99) — title, description, objective.
 *
 * ⚠️ **None of these print on Annex B.** The form's columns are fixed by the province's template;
 * these feed a separate report the PDC has not specified yet. So there is no submit-gate change and
 * nothing here is required — an unfilled project is not an incomplete one.
 *
 * ⚠️ Read view then edit view, the same shape as `AipActivityFields`, rather than three always-live
 * inputs. An encoder opening a project to reach its activities should not be looking at a form.
 */
function AipProjectDetails({
  project, canEdit, lockedReason, onSaved,
}: {
  project: AipProjectDetail;
  canEdit: boolean;
  lockedReason: string;
  onSaved: (patch: Pick<AipProjectDetail, "id" | "name" | "description" | "objective">) => void;
}) {
  const [editing, setEditing] = useState(false);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [name, setName] = useState(project.name);
  const [description, setDescription] = useState(project.description ?? "");
  const [objective, setObjective] = useState(project.objective ?? "");

  function beginEdit() {
    setName(project.name);
    setDescription(project.description ?? "");
    setObjective(project.objective ?? "");
    setError(null);
    setEditing(true);
  }

  async function save() {
    if (!name.trim()) { setError("The project title is required."); return; }
    setSaving(true);
    setError(null);
    try {
      // ⚠️ All three sent every time — the endpoint is a full replace, so omitting a field clears it.
      const updated = await updateAipProject(project.id, {
        name: name.trim(),
        description: description.trim() || null,
        objective: objective.trim() || null,
      });
      onSaved({
        id: project.id,
        name: updated.name,
        description: updated.description,
        objective: updated.objective,
      });
      setEditing(false);
    } catch (e) {
      setError(aipErrorMessage(e, "Could not save the project details."));
    } finally {
      setSaving(false);
    }
  }

  return (
    <section className="border border-slate-200 bg-slate-50 px-3 py-2">
      <div className="flex items-start justify-between gap-3">
        <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-800">
          Project details
        </h3>
        {!editing && (canEdit ? (
          <button type="button" onClick={beginEdit}
            className="whitespace-nowrap text-xs font-medium text-green-700 hover:underline">
            Edit details
          </button>
        ) : (
          // Says who holds the work rather than vanishing — a missing button reads as a feature
          // that broke, not as one that is someone else's turn.
          <span className="whitespace-nowrap text-xs text-slate-600">
            With {lockedReason} — cannot edit
          </span>
        ))}
      </div>

      {editing ? (
        <div className="mt-2 space-y-2">
          <label className="block">
            <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-600">Title</span>
            <input value={name} onChange={(e) => setName(e.target.value)} disabled={saving}
              className="mt-0.5 w-full border border-slate-300 px-2 py-1 text-xs text-slate-800 disabled:bg-slate-100" />
          </label>
          <DetailTextarea label="Description" value={description} onChange={setDescription} disabled={saving} />
          <DetailTextarea label="Objective" value={objective} onChange={setObjective} disabled={saving} />
          {error && <p role="alert" className="text-xs text-red-700">{error}</p>}
          <div className="flex items-center gap-3">
            <button type="button" onClick={() => void save()} disabled={saving}
              className="text-xs font-medium text-green-700 hover:underline disabled:opacity-50">
              {saving ? "Saving…" : "Save"}
            </button>
            <button type="button" onClick={() => setEditing(false)} disabled={saving}
              className="text-xs text-slate-600 hover:underline disabled:opacity-50">
              Cancel
            </button>
          </div>
        </div>
      ) : (
        <dl className="mt-1 space-y-1.5">
          <DetailField label="Description" value={project.description} />
          <DetailField label="Objective" value={project.objective} />
        </dl>
      )}
    </section>
  );
}

/**
 * ⚠️ A textarea, not an input: these are sentences. Sized like the long AIP text fields
 * (CLAUDE.md — min 44px, resize vertical) so a paragraph does not have to be typed through a slot.
 */
function DetailTextarea({
  label, value, onChange, disabled,
}: { label: string; value: string; onChange: (v: string) => void; disabled: boolean }) {
  return (
    <label className="block">
      <span className="text-[11px] font-semibold uppercase tracking-wide text-slate-600">{label}</span>
      <textarea
        value={value}
        onChange={(e) => onChange(e.target.value)}
        disabled={disabled}
        rows={3}
        className="mt-0.5 min-h-[44px] w-full resize-y border border-slate-300 px-2 py-1 text-xs text-slate-800 disabled:bg-slate-100"
      />
    </label>
  );
}

/**
 * ⚠️ An em dash for an empty field, and `whitespace-pre-line` for a filled one — these are the only
 * multi-line free-text fields on the panel, and collapsing an encoder's paragraphs into one run is
 * the bug PPDO-85 fixed for activity names.
 */
function DetailField({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <dt className="text-[11px] font-semibold uppercase tracking-wide text-slate-600">{label}</dt>
      <dd className={`text-xs ${value ? "whitespace-pre-line text-slate-800" : "text-slate-600"}`}>
        {value || "—"}
      </dd>
    </div>
  );
}
