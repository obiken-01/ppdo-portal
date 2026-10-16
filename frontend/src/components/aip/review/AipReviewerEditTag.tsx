/**
 * The "Edited by PPDO" tag on a comment the system wrote for a PPDO reviewer's edit (v1.9.0 —
 * PPDO-214). Shared by the comment thread and the History modal so the two say it the same way.
 *
 * Info blue, not amber: it reports a change that has already been made, not something the office
 * still has to act on.
 */
export default function AipReviewerEditTag() {
  return (
    <span className="rounded-full bg-info-100 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-slate-800">
      Edited by PPDO
    </span>
  );
}

/** "Seen by Ana Encoder" once the office has marked it, or that it is still waiting. */
export function reviewerEditSeenLine(acknowledgedByName: string | null, acknowledgedAt: string | null): string {
  if (acknowledgedAt == null) return "Not yet marked as seen by the office";
  return acknowledgedByName ? `Seen by ${acknowledgedByName}` : "Seen by the office";
}
