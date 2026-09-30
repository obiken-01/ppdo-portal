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
import { useAipUnsavedChange } from "./AipUnsavedChanges";
import type {
  AipActivityDetail, AipCommentNodeType, AipDeleteResult, AipProjectDetail,
} from "@/types";
import { addAipActivity, aipErrorMessage, updateAipProject } from "@/lib/aip";
import { inputCls } from "@/components/aip/AipTreeCells";
import { AipCommentAnchor } from "./AipComments";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "./AipHierarchy";
import { AipFigureStrip, sumActivityAmounts } from "./AipRowFigures";
import AipDeleteNodeButton from "./AipDeleteNodeButton";
import {
  AipDivisionPill, AipDivisionSelect, activityDivisionLock, addActivityBlockedReason,
  divisionChoices, type AipDivisionView,
} from "./AipDivisionParts";
import {
  AipChildList, AipChildRow, AipInlineAdd, AipPanel,
} from "./AipEntryPanelParts";

export default function AipProjectPanel({
  project, canEdit, lockedReason, isLastSibling, proponentOfficeCode, unresolvedCount,
  onSelectActivity, onActivityAdded, onDeleted, onUpdated, divisionView = null,
}: {
  project: AipProjectDetail;
  canEdit: boolean;
  /** Null (PPDO-94) omits the add/delete/edit controls entirely — no holder to name on review. */
  lockedReason: string | null;
  /** Whether this is the last project in its program — omits the renumber sentence (spec §6). */
  isLastSibling: boolean;
  /** This office’s own code, written onto a new activity so it is saved even if nobody opens it. */
  proponentOfficeCode: string | null;
  unresolvedCount: (nodeType: AipCommentNodeType, nodeId: number) => number;
  onSelectActivity: (activityId: number) => void;
  onActivityAdded: (activity: AipActivityDetail) => void;
  onDeleted: (result: AipDeleteResult) => void;
  /** The project's own fields after a save — never its activities, which the endpoint omits. */
  onUpdated: (patch: Pick<AipProjectDetail, "id" | "name" | "description" | "objective">) => void;
  /** PPDO-151 — the caller's place in the division flow; null outside it (no visual change). */
  divisionView?: AipDivisionView | null;
}) {
  // ⚠️ Checked BEFORE the office lock below, but only ever narrows: an office that cannot be
  // edited already disables the control through `canEdit`.
  const addBlocked = canEdit ? addActivityBlockedReason(divisionView) : null;
  // PPDO-152 — a department head or Admin must name the division a new activity belongs to in a
  // divisioned office (§3.1); an encoder's is always their own, set by the server. Defaults to the
  // head's own division when they have one, else the first active one.
  const pickDivision = divisionView?.isHead === true && canEdit;
  const [newDivisionId, setNewDivisionId] = useState<number | null>(() =>
    divisionView ? divisionView.mine?.divisionId ?? divisionChoices(divisionView)[0]?.divisionId ?? null : null
  );
  const amounts = useMemo(() => sumActivityAmounts(project.activities), [project]);
  // The delete cascades to every activity, so it needs all of them writable — read off the
  // server's per-activity `canEdit`, not re-derived. Outside the division flow this never fires.
  const deleteBlocked =
    divisionView && project.activities.some((a) => !a.canEdit)
      ? "it holds activities you cannot edit."
      : null;

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
              blockedReason={deleteBlocked}
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
          canEdit || lockedReason != null ? (
            <div className="flex flex-wrap items-center gap-3">
            {pickDivision && divisionView && (
              <AipDivisionSelect
                view={divisionView}
                label="New activities go to"
                value={newDivisionId}
                onChange={setNewDivisionId}
              />
            )}
            <AipInlineAdd
              label="+ Add activity"
              placeholder="Activity description"
              disabled={!canEdit || addBlocked != null}
              disabledReason={
                addBlocked
                  ?? (lockedReason != null ? `With ${lockedReason} — activities cannot be added here.` : undefined)
              }
              onAdd={async (name) => {
                // ⚠️ The created node is USED, not discarded — the tree absorbs it rather than reloading.
                //
                // ⚠️ The office is stamped at CREATE, not only prefilled in the edit form — an
                // activity nobody opens afterwards still has to print one (PPDO-80's reason, which
                // still holds).
                //
                // ↩️ What changed in PPDO-100 is what it means. It is the PROPONENT office, which is
                // always part of the saved value and always prints first; the edit form strips it from
                // the chips and puts it back on save. It is no longer a guess at who implements.
                onActivityAdded(await addAipActivity(project.id, {
                  name, esreCode: null, implementingOffice: proponentOfficeCode,
                  startDate: null, endDate: null, expectedOutputs: null,
                  fundingSourceRaw: null, ps: null, mooe: null, co: null,
                  ccAdaptation: null, ccMitigation: null, ccTypologyCode: null,
                  // Ignored by the server for an encoder (always their own division).
                  divisionId: pickDivision ? newDivisionId : null,
                }));
              }}
            />
            </div>
          ) : undefined
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
            // ⚠️ Still selectable when locked — the row opens read-only (spec §6.1: disabled, not
            // hidden). Only a DIVISION lock earns the glyph; an office lock is said once, above.
            tag={
              <AipDivisionPill
                activity={activity}
                view={divisionView}
                locked={activityDivisionLock(activity, divisionView) != null}
              />
            }
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
 * ⚠️ **Deliberately the same shape as `AipActivityFields`** (Ralph, 2026-09-16): read view with an
 * "Edit details" button top-right, amber edit surface, shared `inputCls`, and the two buttons bottom
 * RIGHT — outline Cancel, solid green Save details. The first cut of this section used text links
 * bottom-left on a slate panel, so the two forms on the same drill-down disagreed about what a form
 * looks like. If one of them changes, change both.
 */
function AipProjectDetails({
  project, canEdit, lockedReason, onSaved,
}: {
  project: AipProjectDetail;
  canEdit: boolean;
  lockedReason: string | null;
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

  // PPDO-166 — unsaved = the open form differs from the saved project.
  useAipUnsavedChange(
    editing && (
      name !== project.name
      || description !== (project.description ?? "")
      || objective !== (project.objective ?? "")
    ),
    "the project details",
    () => { setEditing(false); setError(null); },
  );

  // ── Read view ───────────────────────────────────────────────────────────
  if (!editing) {
    return (
      <div className="border-b border-slate-200 px-4 py-3">
        <div className="flex items-start justify-between gap-3">
          <dl className="grid flex-1 grid-cols-1 gap-x-6 gap-y-2 sm:grid-cols-2">
            <ProjectField label="Description" value={project.description} />
            <ProjectField label="Objective" value={project.objective} />
          </dl>
          {canEdit ? (
            <button type="button" onClick={beginEdit}
              className="whitespace-nowrap text-xs font-medium text-green-700 hover:underline">
              Edit details
            </button>
          ) : lockedReason != null ? (
            // ↩️ `AipActivityFields` renders nothing here. Kept, because every other disabled control
            // on this panel names its holder (`AipInlineAdd`'s `disabledReason`, the delete button),
            // and a control that simply vanishes reads as a feature that broke.
            <span className="whitespace-nowrap text-xs text-slate-600">
              With {lockedReason} — cannot edit
            </span>
          ) : null /* PPDO-94 — no holder to name on review; omitted, not disabled (spec decision 5). */}
        </div>
      </div>
    );
  }

  // ── Edit view ───────────────────────────────────────────────────────────
  return (
    <div className="border-b border-slate-200 bg-amber-50 px-4 py-3">
      <div className="grid grid-cols-1 gap-3">
        <div>
          <ProjectLabel>Title</ProjectLabel>
          <input value={name} onChange={(e) => setName(e.target.value)} className={inputCls} />
        </div>
        <div>
          <ProjectLabel>Description</ProjectLabel>
          <textarea value={description} onChange={(e) => setDescription(e.target.value)} rows={3}
            className={`${inputCls} resize-vertical`} />
        </div>
        <div>
          <ProjectLabel>Objective</ProjectLabel>
          <textarea value={objective} onChange={(e) => setObjective(e.target.value)} rows={3}
            className={`${inputCls} resize-vertical`} />
        </div>
      </div>

      {error && (
        <p role="alert" className="mt-2 border border-red-200 bg-red-50 px-3 py-2 text-xs text-red-700">
          {error}
        </p>
      )}

      <div className="mt-3 flex justify-end gap-2">
        <button type="button" onClick={() => setEditing(false)} disabled={saving}
          className="border border-slate-300 bg-white px-3 py-1.5 text-xs text-slate-600 hover:bg-slate-50 disabled:opacity-50">
          Cancel
        </button>
        <button type="button" onClick={() => void save()} disabled={saving}
          className="bg-green-700 px-3 py-1.5 text-xs font-medium text-white hover:bg-green-800 disabled:bg-slate-300">
          {saving ? "Saving…" : "Save details"}
        </button>
      </div>
    </div>
  );
}

/** The label used by both AIP detail forms — see `AipActivityFields`' own `Label`. */
function ProjectLabel({ children }: { children: React.ReactNode }) {
  return (
    <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
      {children}
    </label>
  );
}

/**
 * One read-only project field.
 *
 * ⚠️ `whitespace-pre-line` on a filled value — these are the only multi-line free-text fields on the
 * panel, and collapsing an encoder's paragraphs into one run is the bug PPDO-85 fixed for activity
 * names. Neither field is `required`, so an empty one is a neutral em dash, not a warning.
 */
function ProjectField({ label, value }: { label: string; value: string | null }) {
  return (
    <div>
      <dt className="text-xs font-medium uppercase tracking-wide text-slate-600">{label}</dt>
      <dd className={`text-sm ${value ? "whitespace-pre-line text-slate-800" : "text-slate-800"}`}>
        {value || "—"}
      </dd>
    </div>
  );
}
