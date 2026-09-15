"use client";

/**
 * Delete for a project or activity on AIP Entry (PPDO-88) — interim, until PPDO-91 puts delete on
 * the drill-down panels. Hard delete: the server also removes the node's activities, their ledger
 * rows and comments, and renumbers the later siblings on an entered year.
 */

import { useState } from "react";
import ConfirmDialog from "@/components/ui/ConfirmDialog";
import { aipErrorMessage, deleteAipActivity, deleteAipProject } from "@/lib/aip";
import { useReloadAipComments } from "@/components/aip/entry/AipComments";
import type { AipActivityDetail, AipDeleteResult, AipProjectDetail, AipRecordDetail } from "@/types";

type DeleteTarget =
  | { kind: "Project"; project: AipProjectDetail }
  | { kind: "Activity"; activity: AipActivityDetail };

export default function AipDeleteNodeButton({
  target, onDeleted,
}: {
  target: DeleteTarget;
  onDeleted: (result: AipDeleteResult) => void;
}) {
  const reloadComments = useReloadAipComments();
  const [confirming, setConfirming] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const isProject = target.kind === "Project";
  const node = isProject ? target.project : target.activity;
  const noun = isProject ? "project" : "activity";
  const activityCount = isProject ? target.project.activities.length : 0;

  const removes = isProject && activityCount > 0
    ? `its ${activityCount} ${activityCount === 1 ? "activity" : "activities"}, their expenditure lines and every comment on them`
    : "its expenditure lines and every comment on it";
  const message =
    `This removes the ${noun}, ${removes}. Later ${isProject ? "projects" : "activities"} are renumbered. ` +
    "This cannot be undone.";

  async function run() {
    setBusy(true);
    setError(null);
    try {
      const result = isProject ? await deleteAipProject(node.id) : await deleteAipActivity(node.id);
      // The row unmounts once the tree drops it, so busy is not reset on success.
      onDeleted(result);
      void reloadComments?.();
    } catch (e) {
      setError(aipErrorMessage(e, `Could not delete this ${noun}.`));
      setBusy(false);
    }
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
      {error && <span role="alert" className="text-xs text-red-700">{error}</span>}
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
