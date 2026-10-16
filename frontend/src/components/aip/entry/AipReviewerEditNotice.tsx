"use client";

/**
 * "PPDO edited your AIP" — the edited office's notice, with Mark as seen (v1.9.0 — PPDO-214,
 * `docs/v1.9/AIP_Reviewer_Edit_Spec.md` decision 7).
 *
 * ⚠️ **Marking as seen is not resolving.** Each edit's comment was created resolved (PPDO-212), so
 * nothing here touches the re-submit gate. This only stamps that someone in the office has read what
 * PPDO changed, which is what clears the notice for the whole office.
 *
 * Info blue, the same as the "Edited by PPDO" tag: it reports changes already made, it is not a
 * request the office has to act on.
 */

import { useState } from "react";
import { acknowledgeAipReviewerEdits } from "@/lib/aip-comments";
import { refreshAipNotifications } from "@/lib/aip-notifications";
import { useReloadAipComments } from "./AipComments";

export default function AipReviewerEditNotice({
  count, aipRecordId, officeId,
}: {
  count: number;
  aipRecordId: number;
  officeId: number;
}) {
  const [busy, setBusy] = useState(false);
  const [failed, setFailed] = useState(false);
  // Null outside an AipCommentsProvider; AIP Entry renders this inside one.
  const reloadComments = useReloadAipComments();

  async function markSeen() {
    setBusy(true);
    setFailed(false);
    try {
      await acknowledgeAipReviewerEdits(aipRecordId, officeId);
      // The notice is driven by the shared store, so it disappears once this lands — here and in the
      // sidebar together.
      await refreshAipNotifications();
      // ...and the comments' "Not yet marked as seen" lines turn into "Seen by …" without a reload.
      await reloadComments?.();
    } catch {
      setFailed(true);
    } finally {
      setBusy(false);
    }
  }

  return (
    <div role="status" className="mb-4 flex flex-wrap items-center justify-between gap-3 border border-slate-200 bg-info-100 px-4 py-3 text-sm text-slate-800">
      <p>
        {/* "changes", not "lines": one line edited twice is two changes, each with its own comment. */}
        <strong className="font-semibold">PPDO made {count} change{count === 1 ? "" : "s"} to your AIP.</strong>{" "}
        Each change is on its activity as an &ldquo;Edited by PPDO&rdquo; comment, with the reason.
        {failed && <span className="mt-1 block text-red-700">Couldn&rsquo;t mark as seen. Try again.</span>}
      </p>
      <button
        type="button"
        onClick={() => void markSeen()}
        disabled={busy}
        className="inline-flex items-center gap-2 border border-slate-200 bg-white px-3 py-2 text-sm font-medium text-slate-800 transition-colors hover:bg-slate-50 disabled:cursor-not-allowed disabled:opacity-50"
      >
        {busy && <span aria-hidden className="h-3 w-3 animate-spin rounded-full border-2 border-slate-300 border-t-slate-600" />}
        {busy ? "Marking…" : "Mark as seen"}
      </button>
    </div>
  );
}
