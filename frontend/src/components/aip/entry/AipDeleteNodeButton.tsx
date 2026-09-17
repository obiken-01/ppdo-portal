"use client";

/**
 * Delete for a project or activity on AIP Entry (PPDO-88 backend, PPDO-91 controls).
 *
 * Hard delete: the server also removes the node's activities, their ledger rows and comments, and
 * renumbers the later siblings on an entered year (`aip/entry` is FY2028+ only, so that is always).
 */

import { useState } from "react";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { useToast } from "@/components/ui/Toast";
import { aipErrorMessage, deleteAipActivity, deleteAipProject } from "@/lib/aip";
import { useAipUnresolvedCount, useReloadAipComments } from "@/components/aip/entry/AipComments";
import type { AipActivityDetail, AipDeleteResult, AipProjectDetail, AipRecordDetail } from "@/types";

type DeleteTarget =
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
  /**
   * Who holds the work, when this office cannot edit — shown as the disabled reason (spec §6). Null
   * on AIP Review (PPDO-94 spec decision 5): the control is omitted entirely rather than disabled
   * with a reason, because there is no holder to name — the reviewer was never going to delete.
   */
  lockedReason: string | null;
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

  const isProject = target.kind === "Project";
  const node = isProject ? target.project : target.activity;
  const noun = isProject ? "project" : "activity";
  const activityCount = isProject ? target.project.activities.length : null;

  // Decision 10 — everything the delete takes with it: the node's own comments plus, for a
  // project, every activity's. Unresolved only — a resolved thread is not what the dialog warns
  // about clearing.
  const unresolved = isProject
    ? unresolvedCount("Project", target.project.id)
      + target.project.activities.reduce((sum, a) => sum + unresolvedCount("Activity", a.id), 0)
    : unresolvedCount("Activity", target.activity.id);

  const scopeParts: string[] = [];
  if (isProject && activityCount! > 0) {
    scopeParts.push(`its ${activityCount} ${activityCount === 1 ? "activity" : "activities"} and their costing`);
  } else if (!isProject) {
    scopeParts.push("its own costing");
  }
  if (unresolved > 0) {
    scopeParts.push(`${unresolved} unresolved ${unresolved === 1 ? "comment" : "comments"}`);
  }
  const scopeText = scopeParts.length > 0 ? `, ${joinEnglish(scopeParts)}` : "";

  // ⚠️ Omitted, not just falsy-rendered, when nothing follows the deleted node (spec §6 "Delete
  // confirm") — the last sibling leaves a gap today (allocator behaviour), not a renumber.
  const renumberSentence = target.isLastSibling
    ? ""
    : ` Later ${isProject ? "projects in this program" : "activities in this project"} will be renumbered.`;

  const message = `This removes the ${noun}${scopeText}.${renumberSentence} This cannot be undone.`;

  async function run() {
    setBusy(true);
    setError(null);
    setConflict(false);
    try {
      const result = isProject ? await deleteAipProject(node.id) : await deleteAipActivity(node.id);
      toast.success(isProject ? "Project deleted" : "Activity deleted");
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
    // PPDO-94 — no holder to name on the review screen, so the control is omitted rather than
    // disabled with a reason (spec decision 5).
    if (lockedReason == null) return null;
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
 */
export function applyAipDeleteToTree(record: AipRecordDetail, result: AipDeleteResult): AipRecordDetail {
  const codes = new Map(result.renumbered.map((r) => [`${r.nodeType}:${r.id}`, r.refCode]));
  const isDeleted = (kind: AipDeleteResult["deletedNodeType"], id: number) =>
    result.deletedNodeType === kind && result.deletedId === id;

  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs.map((program) => ({
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
