"use client";

/**
 * "Edit amounts" — a PPDO reviewer's edit to one activity's expenditure lines on an office that has
 * reached PPDO (v1.9.0 — PPDO-214, `docs/v1.9/AIP_Reviewer_Edit_Spec.md` §6).
 *
 * The story it serves: an activity scheduled for only part of the year carries only those months'
 * amounts, and during the LFC adjustment PPDO brings it up to the full year. The reviewer may change
 * a line's amounts or add a line; they cannot delete one, change a line's account or fund, or touch
 * the activity's details. Every save carries a reason, and the office sees each change with it.
 *
 * ⚠️ **Built from `AipExpenditureTable`, in its reviewer mode, not a new editor.** The encoder's
 * table already knows the account classes, the procurement items and the conflict panel; a second
 * editor would drift from it in exactly the rules that matter. Reviewer mode only narrows it.
 *
 * ⚠️ **Rendered inside the review page's `AipCommentsProvider`**, so the comment each save writes
 * shows up in the thread beside the table, which is the reviewer's confirmation that the office
 * will see it.
 */

import { useCallback, useEffect, useState } from "react";
import Modal from "@/components/ui/Modal";
import AipExpenditureTable from "@/components/aip/entry/AipExpenditureTable";
import { AipCommentPanel, useReloadAipComments } from "@/components/aip/entry/AipComments";
import { AipLevelChip, AipRefCode } from "@/components/aip/entry/AipHierarchy";
import { aipErrorMessage, listAipExpenditures } from "@/lib/aip";
import { listPriceIndexForPicker } from "@/lib/config";
import { REVIEWER_REASON_MAX, reviewerReasonProblem } from "@/lib/aip-reviewer-edit";
import type {
  AccountResponse, AipActivityDetail, AipExpenditure, FundingSourceResponse, PriceIndexPickerItem,
} from "@/types";

export interface AipReviewerEditModalProps {
  activity: AipActivityDetail;
  /** The office being edited, as the banner names it. */
  officeName: string;
  accounts: AccountResponse[];
  /** The edited office's funds (PPDO-109), not the reviewer's. */
  funds: FundingSourceResponse[];
  onClose: () => void;
  /** Called as the modal closes when anything was saved, so the page re-reads the office's figures. */
  onChanged: () => void;
}

export default function AipReviewerEditModal({
  activity, officeName, accounts, funds, onClose, onChanged,
}: AipReviewerEditModalProps) {
  const [lines, setLines] = useState<AipExpenditure[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [reason, setReason] = useState("");
  const [reasonTouched, setReasonTouched] = useState(false);
  const [changed, setChanged] = useState(false);
  const [priceIndex, setPriceIndex] = useState<PriceIndexPickerItem[]>([]);
  const [priceIndexLoading, setPriceIndexLoading] = useState(true);
  const reloadComments = useReloadAipComments();

  // ⚠️ Does not blank the table after the first read: it also runs after every save, and dropping
  // back to a skeleton would lose the reviewer's place between two edits.
  const loadLines = useCallback(async () => {
    setError(null);
    try {
      setLines(await listAipExpenditures(activity.id));
    } catch (e) {
      setError(aipErrorMessage(e, "Could not load this activity's expenditure lines."));
    }
  }, [activity.id]);

  useEffect(() => { void loadLines(); }, [loadLines]);

  // The ~6,400-row item catalogue, for the line editor's picker only — fetched here, never by the
  // review page itself (RAL-231).
  useEffect(() => {
    void listPriceIndexForPicker({ active: "true" })
      .then(setPriceIndex)
      .catch(() => setPriceIndex([]))
      .finally(() => setPriceIndexLoading(false));
  }, []);

  function close() {
    if (changed) onChanged();
    onClose();
  }

  // ⚠️ The reason is kept after a save on purpose: annualising an activity is usually several lines
  // for the same reason, and retyping it per line invites a vaguer one each time.
  async function afterSave() {
    setChanged(true);
    await loadLines();
    await reloadComments?.();
  }

  const reasonProblem = reviewerReasonProblem(reason);

  return (
    <Modal
      title="Edit amounts"
      size="xl"
      onClose={close}
      footer={<Modal.SecondaryButton onClick={close}>Close</Modal.SecondaryButton>}
    >
      <p className="mb-4 border border-slate-200 bg-info-100 px-4 py-2.5 text-sm text-slate-800">
        Editing <strong className="font-semibold">{officeName}</strong>&rsquo;s AIP as PPDO reviewer.
        The office will see each change and your reason. You can change a line&rsquo;s amounts or add
        a line; deleting a line, changing its account or fund, and the activity&rsquo;s details stay
        with the office.
      </p>

      <div className="mb-4 border border-slate-200 px-4 py-3">
        <div className="mb-1 flex flex-wrap items-center gap-2">
          <AipLevelChip level="activity" />
          <AipRefCode code={activity.refCode} />
        </div>
        <p className="whitespace-pre-line text-base text-slate-800">{activity.name}</p>
      </div>

      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20.75rem] lg:items-start">
        <div className="min-w-0 space-y-4">
          <section>
            <label htmlFor="ppdo-edit-reason" className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
              Reason for the change <span className="text-red-700">*</span>
            </label>
            <textarea
              id="ppdo-edit-reason"
              value={reason}
              onChange={(e) => setReason(e.target.value)}
              onBlur={() => setReasonTouched(true)}
              maxLength={REVIEWER_REASON_MAX + 50}
              rows={2}
              placeholder="e.g. Activity covers Jan–Jun only; LFC annualised it to the full year."
              aria-invalid={reasonTouched && reasonProblem !== null}
              aria-describedby="ppdo-edit-reason-help"
              className="min-h-[44px] w-full resize-y border border-slate-300 px-2 py-1.5 text-sm text-slate-800"
            />
            <div id="ppdo-edit-reason-help" className="mt-1 flex items-start justify-between gap-2 text-[11px]">
              <span className={reasonTouched && reasonProblem ? "text-red-700" : "text-slate-600"}>
                {reasonTouched && reasonProblem ? reasonProblem : "Sent with every line you save below."}
              </span>
              <span className={`tabular-nums ${reason.trim().length > REVIEWER_REASON_MAX ? "text-red-700" : "text-slate-600"}`}>
                {reason.trim().length}/{REVIEWER_REASON_MAX}
              </span>
            </div>
          </section>

          <section>
            <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-600">Expenditure lines</h3>
            {error && (
              <div className="mb-2 flex flex-wrap items-center gap-3 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
                <span>{error}</span>
                <button type="button" onClick={() => void loadLines()}
                  className="border border-slate-200 bg-white px-3 py-1.5 text-sm font-medium text-slate-800 transition-colors hover:bg-slate-50">
                  Try again
                </button>
              </div>
            )}
            {lines === null ? (
              !error && <LinesSkeleton />
            ) : (
              <div className="border border-slate-200">
                <AipExpenditureTable
                  activityId={activity.id}
                  lines={lines}
                  accounts={accounts}
                  fundingSources={funds}
                  canEdit
                  generalFundId={null}
                  priceIndex={priceIndex}
                  priceIndexLoading={priceIndexLoading}
                  reviewerReason={reason}
                  onChanged={() => void afterSave()}
                  onReload={() => void loadLines()}
                />
              </div>
            )}
          </section>
        </div>

        <AipCommentPanel activityId={activity.id} />
      </div>
    </Modal>
  );
}

/** Shaped like the table: header, then three rows, so the dialog does not jump when lines land. */
function LinesSkeleton() {
  return (
    <div aria-hidden className="space-y-1 border border-slate-200 px-4 py-3">
      <div className="h-4 w-1/3 animate-pulse bg-slate-100" />
      {[0, 1, 2].map((i) => <div key={i} className="h-7 animate-pulse bg-slate-100" />)}
    </div>
  );
}
