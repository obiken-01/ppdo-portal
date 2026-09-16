"use client";

/**
 * Delete for a program, project or activity on AIP Entry (PPDO-88 backend, PPDO-91 project and
 * activity controls, PPDO-101 program).
 *
 * Hard delete: the server also removes the node's descendants, their ledger rows and comments, and
 * renumbers the later siblings on an entered year (`aip/entry` is FY2028+ only, so that is always).
 *
 * ⚠️ **Except programs.** A program's ref code is the LDIP's (`aip-program-refcodes-match-ldip`, spec
 * §2 decision 11), so deleting one leaves a gap on purpose and nothing is renumbered. The program
 * also becomes addable again — `GetAddableProgramsAsync` derives `AlreadyAdded` from the group's live
 * programs — which is the one thing that makes a program delete recoverable without the audit log.
 */

import { useState } from "react";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import { aipErrorMessage, deleteAipActivity, deleteAipProgram, deleteAipProject } from "@/lib/aip";
import { useAipUnresolvedCount, useReloadAipComments } from "@/components/aip/entry/AipComments";
import type {
  AipActivityDetail, AipDeleteResult, AipProgramDetail, AipProjectDetail, AipRecordDetail,
} from "@/types";

type DeleteTarget =
  // ⚠️ No `isLastSibling` on a program: there is no renumbering to promise or withhold.
  | { kind: "Program"; program: AipProgramDetail }
  | { kind: "Project"; project: AipProjectDetail; isLastSibling: boolean }
  | { kind: "Activity"; activity: AipActivityDetail; isLastSibling: boolean };

/** `["a", "b", "c"]` → `"a, b, and c"` — never a comma before a single item. */
function joinEnglish(parts: string[]): string {
  if (parts.length <= 1) return parts.join("");
  if (parts.length === 2) return parts.join(" and ");
  return `${parts.slice(0, -1).join(", ")}, and ${parts[parts.length - 1]}`;
}

export default function AipDeleteNodeButton({
  target, canEdit, lockedReason, onDeleted,
}: {
  target: DeleteTarget;
  canEdit: boolean;
  /** Who holds the work, when this office cannot edit — shown as the disabled reason (spec §6). */
  lockedReason: string;
  onDeleted: (result: AipDeleteResult) => void;
}) {
  const reloadComments = useReloadAipComments();
  // ⚠️ Read from the provider this button already sits inside — a second fetch here would be the
  // N+1 `AipComments.tsx` exists to prevent (PPDO-89 learning).
  const unresolvedCount = useAipUnresolvedCount();
  const { toast } = useToast();
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [conflict, setConflict] = useState(false);

  const node = target.kind === "Program" ? target.program
    : target.kind === "Project" ? target.project
    : target.activity;
  const noun = target.kind.toLowerCase();

  // The projects and activities this delete takes with it — the whole subtree, which is also what
  // the comment counts have to walk.
  const projects = target.kind === "Program" ? target.program.projects
    : target.kind === "Project" ? [target.project]
    : [];
  const activities = target.kind === "Activity" ? [target.activity] : projects.flatMap((p) => p.activities);

  // Decision 10 — everything the delete takes with it, the node's own comments included. Unresolved
  // only: a resolved thread is not what the dialog warns about clearing.
  const unresolved =
    (target.kind === "Program" ? unresolvedCount("Program", target.program.id) : 0)
    + (target.kind === "Activity" ? 0 : projects.reduce((sum, p) => sum + unresolvedCount("Project", p.id), 0))
    + activities.reduce((sum, a) => sum + unresolvedCount("Activity", a.id), 0);

  const scopeParts: string[] = [];
  if (target.kind === "Program" && projects.length > 0) {
    scopeParts.push(`its ${projects.length} ${projects.length === 1 ? "project" : "projects"}`);
  }
  if (target.kind === "Activity") {
    scopeParts.push("its own costing");
  } else if (activities.length > 0) {
    scopeParts.push(`${activities.length} ${activities.length === 1 ? "activity" : "activities"} and their costing`);
  }
  if (unresolved > 0) {
    scopeParts.push(`${unresolved} unresolved ${unresolved === 1 ? "comment" : "comments"}`);
  }
  const scopeText = scopeParts.length > 0 ? `, ${joinEnglish(scopeParts)}` : "";

  // ⚠️ Omitted, not just falsy-rendered, when nothing follows the deleted node (spec §6 "Delete
  // confirm") — the last sibling leaves a gap today (allocator behaviour), not a renumber. A program
  // never renumbers at all: its code is the LDIP's.
  const renumberSentence = target.kind === "Program" || target.isLastSibling
    ? ""
    : ` Later ${target.kind === "Project" ? "projects in this program" : "activities in this project"} will be renumbered.`;

  // ⚠️ Said out loud for a program, because it is the difference between "gone" and "gone until you
  // pick it again" — and nothing else on the page tells the encoder which one this is.
  const readdSentence = target.kind === "Program"
    ? " Its code stays free, and the program can be added again from Add programs."
    : "";

  const message =
    `This removes the ${noun}${scopeText}.${renumberSentence} This cannot be undone.${readdSentence}`;

  async function run() {
    setBusy(true);
    setError(null);
    setConflict(false);
    try {
      const result = target.kind === "Program" ? await deleteAipProgram(node.id)
        : target.kind === "Project" ? await deleteAipProject(node.id)
        : await deleteAipActivity(node.id);
      toast.success(`${target.kind} deleted`);
      // The row unmounts once the tree drops it, so busy is not reset on success.
      onDeleted(result);
      void reloadComments?.();
    } catch (e) {
      // ⚠️ 409 gets its own banner with a Reload action (spec §6 "Conflict") — a sibling was
      // renumbered by someone else's delete mid-save, and retrying the same click will not help.
      if ((e as { response?: { status?: number } })?.response?.status === 409) {
        setConflict(true);
      } else {
        setError(aipErrorMessage(e, `Could not delete this ${noun}.`));
      }
      setBusy(false);
    }
  }

  if (!canEdit) {
    return (
      <span className="text-xs text-slate-600" title={`With ${lockedReason} — this cannot be deleted here.`}>
        With {lockedReason} — cannot delete
      </span>
    );
  }

  return (
    <span className="inline-flex items-center gap-2">
      <button
        type="button"
        disabled={busy}
        onClick={() => setConfirming(true)}
        className="text-xs font-medium text-red-700 hover:underline disabled:opacity-50"
      >
        {busy ? "Deleting…" : `Delete ${noun}`}
      </button>
      {conflict ? (
        <span role="alert" className="text-xs text-red-700">
          This list changed while you were saving.{" "}
          <button type="button" onClick={() => window.location.reload()} className="font-medium underline">
            Reload
          </button>
        </span>
      ) : error ? (
        <span role="alert" className="text-xs text-red-700">{error}</span>
      ) : null}
      {confirming && (
        <ConfirmDialog
          title={`Delete ${noun} ${node.refCode}?`}
          message={message}
          confirmLabel="Delete"
          variant="danger"
          onConfirm={() => void run()}
          onClose={() => setConfirming(false)}
        />
      )}
    </span>
  );
}

/**
 * Splices a delete result into the tree without a reload: drops the deleted node and applies the
 * server's renumbered codes (a renumbered project's activities arrive in the same list).
 *
 * ⚠️ A deleted **program** is dropped here too (PPDO-101). It was not, before there was a control to
 * delete one — the node stayed in the tree and the panel kept rendering it until the next reload.
 */
export function applyAipDeleteToTree(record: AipRecordDetail, result: AipDeleteResult): AipRecordDetail {
  const codes = new Map(result.renumbered.map((r) => [`${r.nodeType}:${r.id}`, r.refCode]));
  const isDeleted = (kind: AipDeleteResult["deletedNodeType"], id: number) =>
    result.deletedNodeType === kind && result.deletedId === id;

  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs
        .filter((program) => !isDeleted("Program", program.id))
        .map((program) => ({
          ...program,
          projects: program.projects
            .filter((project) => !isDeleted("Project", project.id))
            .map((project) => ({
              ...project,
              refCode: codes.get(`Project:${project.id}`) ?? project.refCode,
              activities: project.activities
                .filter((activity) => !isDeleted("Activity", activity.id))
                .map((activity) => ({
                  ...activity,
                  refCode: codes.get(`Activity:${activity.id}`) ?? activity.refCode,
                })),
            })),
        })),
    })),
  };
}
