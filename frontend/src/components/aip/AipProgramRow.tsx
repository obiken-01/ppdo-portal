"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import { useState } from "react";
import { aipErrorMessage, deleteAipProgram, updateAipProgram } from "@/lib/aip";
import { AIP_FUNCTION_BANDS } from "@/lib/aipConstants";
import { Chevron, inputCls, selectCls } from "@/components/aip/AipTreeCells";
import ProjectRow from "@/components/aip/AipProjectRow";
import AddProjectRow from "@/components/aip/AipAddProjectRow";
import type { TreeActions } from "@/components/aip/AipTreeActions";
import type { AipProgramDetail } from "@/types";

// ── Program row — Edit/Delete for itself, renders its projects + Add Project ────

export default function ProgramRow({
  prog, open, onToggle, collapsedProjects, onToggleProject, actions,
}: {
  prog: AipProgramDetail;
  open: boolean;
  onToggle: () => void;
  collapsedProjects: Set<number>;
  onToggleProject: (id: number) => void;
  actions: TreeActions;
}) {
  const [editing, setEditing]           = useState(false);
  const [name, setName]                 = useState(prog.name);
  const [functionBand, setFunctionBand] = useState(prog.functionBand ?? "CORE");
  const [saving, setSaving]             = useState(false);
  const [error, setError]               = useState<string | null>(null);

  function startEdit() {
    setName(prog.name);
    setFunctionBand(prog.functionBand ?? "CORE");
    setError(null);
    setEditing(true);
  }

  async function handleSave() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipProgram(prog.id, { name: name.trim(), functionBand });
      // Server response omits Projects by convention — merge onto the existing node.
      actions.onProgramUpdated({ ...prog, name: updated.name, functionBand: updated.functionBand });
      setEditing(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not save changes."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDelete() {
    const activityCount = prog.projects.reduce((n, j) => n + j.activities.length, 0);
    actions.onRequestConfirm({
      title: "Delete Program?",
      message: `This removes "${prog.name}" and everything under it (${prog.projects.length} project${prog.projects.length !== 1 ? "s" : ""}, ${activityCount} activit${activityCount !== 1 ? "ies" : "y"}). This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteAipProgram(prog.id);
          actions.onProgramDeleted(prog.officeId, prog.id);
        } catch (err) {
          setError(aipErrorMessage(err, "Could not delete program."));
        }
      },
      onClose: () => {},
    });
  }

  return (
    <>
      <tr className="bg-white border-t-2 border-slate-200 align-top">
        <td className="px-2 py-1.5 pl-5 font-mono text-xs text-slate-600 border-l-4 border-green-400">
          <button onClick={onToggle} className="flex items-center gap-1.5 text-left">
            <Chevron open={open} className="text-green-500" />
            {prog.refCode}
          </button>
        </td>
        {editing ? (
          <td colSpan={14} className="px-2 py-1">
            <div className="flex items-center gap-2 flex-wrap">
              <input value={name} onChange={(e) => setName(e.target.value)} className={`${inputCls} max-w-md`} />
              <select value={functionBand} onChange={(e) => setFunctionBand(e.target.value)} className={`${selectCls} w-32`}>
                {AIP_FUNCTION_BANDS.map((b) => <option key={b} value={b}>{b}</option>)}
              </select>
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
          <td colSpan={14} className="px-2 py-1.5 font-semibold text-xs italic text-slate-600 uppercase tracking-wide">
            {prog.name}
            {!open && (
              <span className="ml-2 font-normal not-italic text-[10px] text-slate-600">
                {prog.projects.length} projects ·{" "}
                {prog.projects.flatMap((p) => p.activities).length} activities
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

      {open && prog.projects.map((proj) => (
        <ProjectRow
          key={`proj-${proj.id}`}
          proj={proj}
          open={!collapsedProjects.has(proj.id)}
          onToggle={() => onToggleProject(proj.id)}
          actions={actions}
        />
      ))}

      {open && actions.canEdit && (
        <AddProjectRow programId={prog.id} onAdded={(newProj) => actions.onProjectAdded(prog.id, newProj)} />
      )}
    </>
  );
}
