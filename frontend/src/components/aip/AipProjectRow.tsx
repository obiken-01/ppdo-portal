"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import { useState } from "react";
import { aipErrorMessage, deleteAipProject, updateAipProject } from "@/lib/aip";
import { Chevron, inputCls } from "@/components/aip/AipTreeCells";
import ActivityRow from "@/components/aip/AipActivityRow";
import AddActivityRow from "@/components/aip/AipAddActivityRow";
import type { TreeActions } from "@/components/aip/AipTreeActions";
import type { AipProjectDetail } from "@/types";

// ── Project row — Edit/Delete for itself, renders its activities + Add Activity ──

export default function ProjectRow({
  proj, open, onToggle, actions,
}: {
  proj: AipProjectDetail;
  open: boolean;
  onToggle: () => void;
  actions: TreeActions;
}) {
  const [editing, setEditing] = useState(false);
  const [name, setName]       = useState(proj.name);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  function startEdit() { setName(proj.name); setError(null); setEditing(true); }

  async function handleSave() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipProject(proj.id, { name: name.trim() });
      // Server response omits Activities by convention — merge onto the existing node.
      actions.onProjectUpdated({ ...proj, name: updated.name });
      setEditing(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not save changes."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDelete() {
    actions.onRequestConfirm({
      title: "Delete Project?",
      message: `This removes "${proj.name}" and its ${proj.activities.length} activit${proj.activities.length !== 1 ? "ies" : "y"}. This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteAipProject(proj.id);
          actions.onProjectDeleted(proj.programId, proj.id);
        } catch (err) {
          setError(aipErrorMessage(err, "Could not delete project."));
        }
      },
      onClose: () => {},
    });
  }

  return (
    <>
      <tr className="bg-slate-50 border-t border-slate-200 align-top">
        <td className="px-2 py-1.5 pl-9 font-mono text-xs text-slate-600 border-l-4 border-slate-300">
          <button onClick={onToggle} className="flex items-center gap-1.5 text-left">
            <Chevron open={open} className="text-slate-400" />
            {proj.refCode}
          </button>
        </td>
        {editing ? (
          <td colSpan={14} className="px-2 py-1">
            <div className="flex items-center gap-2 flex-wrap">
              <input value={name} onChange={(e) => setName(e.target.value)} className={`${inputCls} max-w-md`} />
              <button onClick={handleSave} disabled={saving} className={`text-xs font-medium ${saving ? "text-green-300" : "text-green-700 hover:underline"}`}>
                {saving ? "Saving…" : "Save"}
              </button>
              <button onClick={() => setEditing(false)} disabled={saving} className="text-xs text-slate-600 hover:underline disabled:opacity-50">
                Cancel
              </button>
              {error && <span className="text-[10px] text-danger-600">{error}</span>}
            </div>
          </td>
        ) : (
          <td colSpan={14} className="px-2 py-1.5 text-xs font-medium text-slate-600">
            {proj.name}
            {proj.isSynthetic && (
              <span
                className="ml-2 px-1.5 py-0.5 text-[10px] font-normal bg-amber-100 text-amber-700"
                title="This project does not exist in the source file — it was created to hold a line item recorded directly on the parent program row."
              >
                program-level entry
              </span>
            )}
            {!open && (
              <span className="ml-2 font-normal text-[10px] text-slate-600">
                {proj.activities.length} activities
              </span>
            )}
          </td>
        )}
        <td className="px-2 py-1.5 text-center whitespace-nowrap">
          {actions.canEdit && !editing && (
            <span className="inline-flex gap-2">
              <button onClick={startEdit} className="text-xs text-green-700 hover:underline">Edit</button>
              <button onClick={confirmDelete} className="text-xs text-danger-500 hover:underline">Delete</button>
            </span>
          )}
        </td>
      </tr>

      {open && proj.activities.map((act) => (
        <ActivityRow
          key={`act-${act.id}`}
          act={act}
          aipRecordId={actions.aipRecordId}
          canEdit={actions.canEdit}
          fundingSources={actions.fundingSources}
          onSaved={actions.onActivitySaved}
          onDeleted={(activityId) => actions.onActivityDeleted(proj.id, activityId)}
          onRequestConfirm={actions.onRequestConfirm}
        />
      ))}

      {open && actions.canEdit && (
        <AddActivityRow
          projectId={proj.id}
          fundingSources={actions.fundingSources}
          onAdded={(newAct) => actions.onActivityAdded(proj.id, newAct)}
        />
      )}
    </>
  );
}
