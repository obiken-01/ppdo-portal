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
import { aipConflict, aipErrorMessage, deleteAipActivity, deleteAipProject } from "@/lib/aip";
import AipConflictPanel, { type AipConflictField } from "@/components/aip/AipConflictPanel";
import { fmtPesos } from "@/lib/aip-units";
import { useAipUnresolvedCount, useReloadAipComments } from "@/components/aip/entry/AipComments";
import type {
  AipActivityDetail, AipConflict, AipDeleteResult, AipProjectDetail, AipRecordDetail,
} from "@/types";

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
  target, canEdit, lockedReason, onDeleted, onKept, blockedReason = null,
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
  /**
   * PPDO-193 — an activity delete refused because someone saved the activity after it was loaded,
   * and the user chose to keep it. Called with the activity as it now stands (the conflict
   * payload), so the tree shows their change without a refetch.
   */
  onKept?: (current: AipActivityDetail) => void;
  /**
   * PPDO-151 — why the delete is refused while the office itself is still editable: a project
   * holding activities this reader cannot write (another division's, untagged, or submitted). The
   * server refuses the same delete (`RefuseContainerDelete`); saying so beats a 400 on click.
   */
  blockedReason?: string | null;
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
  // PPDO-193 (V18-71) — the activity changed since it was loaded. A choice, not a message: held
  // apart from `conflict`, which is the renumber race and has nothing to choose.
  const [versionConflict, setVersionConflict] = useState<AipConflict<AipActivityDetail> | null>(null);

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

  /**
   * @param rowVersion for an activity: the version to delete against. Defaults to the one the tree
   *   holds; "Delete it" on the conflict panel passes the payload's, so the retry is one request.
   */
  async function run(rowVersion: string | null = isProject ? null : target.activity.rowVersion ?? null) {
    setBusy(true);
    setError(null);
    setConflict(false);
    try {
      const result = isProject
        ? await deleteAipProject(node.id)
        : await deleteAipActivity(node.id, rowVersion);
      setVersionConflict(null);
      toast.success(isProject ? "Project deleted" : "Activity deleted");
      // The row unmounts once the tree drops it, so busy is not reset on success.
      onDeleted(result);
      void reloadComments?.();
    } catch (e) {
      // ⚠️ A version conflict first: it is also a 409, but it carries the row as it now stands and
      // must reach the panel, not the renumber banner below.
      const clash = isProject ? null : aipConflict<AipActivityDetail>(e);
      if (clash) {
        setVersionConflict(clash);
        setBusy(false);
        return;
      }
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

  if (blockedReason) {
    return (
      <span className="text-xs text-slate-600" title={blockedReason}>
        Cannot delete — {blockedReason}
      </span>
    );
  }

  return (
    <span className={`inline-flex items-center gap-2 ${versionConflict ? "flex-wrap" : ""}`}>
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
      {/* PPDO-193 — full width under the header row, so the comparison is readable. */}
      {versionConflict && (
        <span className="mt-1 block basis-full text-left">
          <AipConflictPanel
            conflict={versionConflict}
            noun={noun}
            fields={deleteConflictFields(versionConflict.current)}
            busy={busy}
            onOverwrite={() => void run(versionConflict.currentRowVersion)}
            onDiscard={() => {
              const current = versionConflict.current;
              setVersionConflict(null);
              onKept?.(current);
            }}
          />
        </span>
      )}
    </span>
  );
}

/**
 * What the delete-conflict panel lists. The panel's buttons are shared with the edit conflict
 * ("Overwrite with mine" / "Discard mine"), so the first row says what each means here, as the
 * expenditure-line delete does.
 */
/** "Keep it (₱550.00)", or just "Keep it" for an uncosted activity rather than "Keep it (—)". */
function keepLabel(total: number | null | undefined): string {
  return total ? `Keep it (${fmtPesos(total)})` : "Keep it";
}

function deleteConflictFields(theirs: AipActivityDetail): AipConflictField[] {
  return [
    { label: "This activity", mine: "Delete it", theirs: keepLabel(theirs.total) },
  ];
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
