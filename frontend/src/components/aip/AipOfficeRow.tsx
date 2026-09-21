"use client";

/**
 * Extracted verbatim from `aip/detail/page.tsx` (PPDO-64). No behaviour change.
 */

import { useState } from "react";
import { aipErrorMessage, deleteAipOffice, updateAipOffice } from "@/lib/aip";
import { sumActivities } from "@/lib/aip-tree";
import { AmtTD, Chevron } from "@/components/aip/AipTreeCells";
import ProgramRow from "@/components/aip/AipProgramRow";
import AddProgramRow from "@/components/aip/AipAddProgramRow";
import type { TreeActions } from "@/components/aip/AipTreeActions";
import type { AipOfficeDetail } from "@/types";

// ── Office row — Edit/Delete for itself, renders its programs + Add Program ─────

export default function OfficeRow({
  office, open, onToggle, collapsedPrograms, onToggleProgram, collapsedProjects, onToggleProject, actions,
}: {
  office: AipOfficeDetail;
  open: boolean;
  onToggle: () => void;
  collapsedPrograms: Set<number>;
  onToggleProgram: (id: number) => void;
  collapsedProjects: Set<number>;
  onToggleProject: (id: number) => void;
  actions: TreeActions;
}) {
  const [editing, setEditing] = useState(false);
  const [name, setName]       = useState(office.name);
  const [saving, setSaving]   = useState(false);
  const [error, setError]     = useState<string | null>(null);

  const officePs    = sumActivities(office, "ps");
  const officeMooe  = sumActivities(office, "mooe");
  const officeCo    = sumActivities(office, "co");
  const officeTotal = sumActivities(office, "total");
  const actCount    = office.programs.flatMap((p) => p.projects).flatMap((p) => p.activities).length;

  function startEdit() { setName(office.name); setError(null); setEditing(true); }

  async function handleSave() {
    if (!name.trim()) { setError("Name is required."); return; }
    setSaving(true);
    setError(null);
    try {
      const updated = await updateAipOffice(office.id, { name: name.trim() });
      // Server response omits Programs by convention — merge onto the existing node.
      actions.onOfficeUpdated({ ...office, name: updated.name });
      setEditing(false);
    } catch (err) {
      setError(aipErrorMessage(err, "Could not save changes."));
    } finally {
      setSaving(false);
    }
  }

  function confirmDelete() {
    actions.onRequestConfirm({
      title: "Delete Office?",
      message: `This removes "${office.name}" and everything under it (${office.programs.length} programs, ${actCount} activities). This cannot be undone.`,
      confirmLabel: "Delete",
      variant: "danger",
      onConfirm: async () => {
        try {
          await deleteAipOffice(office.id);
          actions.onOfficeDeleted(office.id);
        } catch (err) {
          setError(aipErrorMessage(err, "Could not delete office."));
        }
      },
      onClose: () => {},
    });
  }

  return (
    <>
      <tr className="bg-green-700 border-t-2 border-green-800 align-top">
        <td className="px-2 py-2 font-mono text-xs text-slate-600 align-top">
          <button onClick={onToggle} className="flex items-center gap-1.5 text-left">
            <Chevron open={open} className="text-green-200" />
            <span className="text-green-100">{office.refCode}</span>
          </button>
        </td>
        {editing ? (
          <td colSpan={5} className="px-2 py-1.5">
            <div className="flex items-center gap-2 flex-wrap">
              <input value={name} onChange={(e) => setName(e.target.value)} className="border border-slate-300 bg-white text-xs px-1.5 py-1 text-slate-800 w-full max-w-xs focus:outline-none focus:ring-1 focus:ring-green-600" />
              <button onClick={handleSave} disabled={saving} className={`text-xs font-medium ${saving ? "text-green-200" : "text-white hover:underline"}`}>
                {saving ? "Saving…" : "Save"}
              </button>
              <button onClick={() => setEditing(false)} disabled={saving} className="text-xs text-green-100 hover:underline disabled:opacity-50">
                Cancel
              </button>
              {error && <span className="text-[10px] text-amber-200">{error}</span>}
            </div>
          </td>
        ) : (
          <td className="px-2 py-2 align-top">
            <span className="font-bold text-sm uppercase text-white">{office.name}</span>
            {!open && (
              <span className="ml-2 text-[10px] text-green-200">
                {office.programs.length} programs · {actCount} activities
              </span>
            )}
          </td>
        )}
        {!editing && (
          <>
            <td /><td /><td /><td />
            <AmtTD value={officePs}    white />
            <AmtTD value={officeMooe}  white />
            <AmtTD value={officeCo}    white />
            <AmtTD value={officeTotal} white />
            <td /><td /><td />
          </>
        )}
        <td className="px-2 py-2 text-center whitespace-nowrap">
          {actions.canEdit && !editing && (
            <span className="inline-flex gap-2">
              <button onClick={startEdit} className="text-xs text-green-100 hover:underline">Edit</button>
              <button onClick={confirmDelete} className="text-xs text-amber-200 hover:underline">Delete</button>
            </span>
          )}
        </td>
      </tr>

      {open && office.programs.map((prog) => (
        <ProgramRow
          key={`prog-${prog.id}`}
          prog={prog}
          open={!collapsedPrograms.has(prog.id)}
          onToggle={() => onToggleProgram(prog.id)}
          collapsedProjects={collapsedProjects}
          onToggleProject={onToggleProject}
          actions={actions}
        />
      ))}

      {open && actions.canEdit && (
        <AddProgramRow
          officeId={office.id}
          onAdded={(newProg) => actions.onProgramAdded(office.id, newProg)}
          ldipOnly={actions.programsLdipOnly}
        />
      )}
    </>
  );
}
