"use client";

/**
 * The activity modal on the AIP Review search (PPDO-79, `docs/v1.8/AIP_Review_Spec.md` §6.1a).
 *
 * ⚠️ **One modal, two readers, and they differ only in what they may do.** The PPDO consolidated
 * reviewer reads and comments. The department head of the activity's own office may also edit it
 * while the work is in the office's hands. Whether THIS reader may edit comes from the server's
 * `canEdit`, never from the reader's flags: a person holding both flags is denied content writes even
 * on their own office, and nothing on `/auth/me` says so.
 *
 * ⚠️ **Composed from the entry page's own pieces, not re-drawn.** `AipActivityFields` and
 * `AipExpenditureTable` are what a department head edits with on AIP Entry — same field order, same
 * Edit / Save behaviour. Someone who learns one surface knows the other, and the two cannot drift in
 * what they accept.
 *
 * ⚠️ **Two columns, by decision (2026-09-13):** the row on the left, the comment thread on the right,
 * where it stays in view beside the figures. Below `lg` it stacks — a side rail cannot survive a
 * phone-width dialog. The expenditure lines are in by decision too; `Modal` scrolls its body under a
 * fixed header and footer, so they cost a scroll rather than a dialog taller than the screen.
 */

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import Modal from "@/components/ui/Modal";
import AipActivityFields from "@/components/aip/entry/AipActivityFields";
import AipExpenditureTable from "@/components/aip/entry/AipExpenditureTable";
import { AipCommentsProvider, AipCommentPanel } from "@/components/aip/entry/AipComments";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "@/components/aip/entry/AipHierarchy";
import {
  AipFigureStrip, AipFundPill, AipUnitCaption, activityFundLabel,
} from "@/components/aip/entry/AipRowFigures";
import { getAipActivityReview } from "@/lib/aip-review";
import { aipErrorMessage } from "@/lib/aip";
import { listAccounts, listFundingSources, listPriceIndexForPicker } from "@/lib/config";
import {
  describeAipHolderForDepartmentHead, describeAipHolderForReviewer,
} from "@/lib/aip-workflow";
import type {
  AipActivityReview, AipActivityDetail, AccountResponse, FundingSourceResponse,
  PriceIndexPickerItem,
} from "@/types";

export interface AipActivityReviewModalProps {
  activityId: number;
  /** The reader holds the cross-office grant. */
  crossOffice: boolean;
  /** The reader's own office — tells "reviewing somebody else's office" from "my own". */
  readerOfficeId: number | null;
  onClose: () => void;
  /**
   * Called as the modal closes when anything was saved inside it, so the search can re-run — a
   * renamed activity would otherwise keep its old name in the result row it was opened from.
   */
  onChanged: () => void;
}

export default function AipActivityReviewModal({
  activityId, crossOffice, readerOfficeId, onClose, onChanged,
}: AipActivityReviewModalProps) {
  const [review, setReview] = useState<AipActivityReview | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [changed, setChanged] = useState(false);

  const [accounts, setAccounts] = useState<AccountResponse[]>([]);
  const [funds, setFunds] = useState<FundingSourceResponse[]>([]);
  const [priceIndex, setPriceIndex] = useState<PriceIndexPickerItem[]>([]);
  const [priceIndexLoading, setPriceIndexLoading] = useState(true);

  // ⚠️ Does not blank the modal. It also runs after every save, and dropping back to a skeleton
  // would throw away the reader's scroll position in the middle of the edit they just made.
  const refresh = useCallback(async () => {
    setError(null);
    try {
      setReview(await getAipActivityReview(activityId));
    } catch (e) {
      setError(aipErrorMessage(e, "Could not load this activity."));
    }
  }, [activityId]);

  useEffect(() => {
    setReview(null);
    void refresh();
  }, [refresh]);

  // Account and fund names for the lines. Off the critical path — the modal renders without them.
  useEffect(() => {
    void listAccounts().then(setAccounts).catch(() => setAccounts([]));
    void listFundingSources({ active: "true" }).then(setFunds).catch(() => setFunds([]));
  }, []);

  // ⚠️ ~6,400 rows feeding only the line editor's item PICKER, so it is fetched only for a reader
  // who can open that editor — never for the PPDO reviewer (RAL-231).
  const canEdit = review?.canEdit === true;
  useEffect(() => {
    if (!canEdit) return;
    void listPriceIndexForPicker({ active: "true" })
      .then(setPriceIndex)
      .catch(() => setPriceIndex([]))
      .finally(() => setPriceIndexLoading(false));
  }, [canEdit]);

  function close() {
    if (changed) onChanged();
    onClose();
  }

  function onDetailsSaved(updated: AipActivityDetail) {
    setReview((r) => (r ? { ...r, activity: updated } : r));
    setChanged(true);
  }

  // The PPDO voice, and the link to the one-office review screen, apply only when the reader is
  // looking at somebody ELSE's office. ⚠️ Own office wins for a holder of both flags — the same rule
  // that files their comments under the department-head side.
  const ppdoView = review != null && crossOffice && readerOfficeId !== review.officeId;

  return (
    <Modal
      title="Activity"
      size="xl"
      onClose={close}
      footer={
        <>
          {review && (
            // ⚠️ Program and project rows open these same two surfaces from the search (decided
            // 2026-09-13), so the footer offers the reader the one that is theirs — Return and Accept
            // live on the review screen; the department head's whole-office surface is AIP Entry.
            <Link
              href={
                ppdoView
                  ? `/budget-planning/aip/review?officeId=${review.officeId}&fiscalYear=${review.fiscalYear}`
                  : `/budget-planning/aip/entry?fiscalYear=${review.fiscalYear}`
              }
              className="mr-auto text-sm text-green-700 underline underline-offset-2 hover:text-green-800"
            >
              {ppdoView ? `Open ${review.officeCode || review.officeName}'s full AIP →` : "Open in AIP Entry →"}
            </Link>
          )}
          <Modal.SecondaryButton onClick={close}>Close</Modal.SecondaryButton>
        </>
      }
    >
      {!review ? (
        error ? (
          <div className="space-y-3">
            <p className="border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
            <button
              type="button"
              onClick={() => void refresh()}
              className="border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-800 hover:bg-slate-50"
            >
              Try again
            </button>
          </div>
        ) : (
          <ModalSkeleton />
        )
      ) : (
        <AipCommentsProvider aipRecordId={review.aipRecordId} officeId={review.officeId}>
          {/* A failed refresh AFTER a save keeps the last good read on screen, and says so. */}
          {error && (
            <p className="mb-4 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
          )}

          {/* ⚠️ Names who holds the work, never a bare "read-only" — a reader told only that has no
              idea why, or who to ask. */}
          <p
            className={`mb-4 border border-slate-200 px-4 py-2.5 text-sm text-slate-800 ${
              ppdoView ? "bg-info-100" : "bg-amber-100"
            }`}
          >
            {ppdoView ? (
              <>
                You are reading {review.officeName}&rsquo;s AIP, which is with{" "}
                {describeAipHolderForReviewer(review.workflowStatus)}. Ask for changes in the comments
                &mdash; only the office can edit it. Return and Accept are on the office&rsquo;s review
                screen.
              </>
            ) : (
              <>
                This is your office&rsquo;s AIP, with{" "}
                {describeAipHolderForDepartmentHead(review.workflowStatus)}.{" "}
                {review.canEdit
                  ? "You can edit this activity here."
                  : "You can read it and comment on it here."}
              </>
            )}
          </p>

          {/* Where the activity sits. A search row is flat, so without this the reader has no idea
              which program it belongs to. Same three-channel level encoding as the tree. */}
          <div className="mb-4">
            <div className={`flex flex-wrap items-center gap-2 px-3 py-1.5 ${aipHeaderRow("office")}`}>
              <AipLevelChip level="office" />
              <span className="text-sm font-semibold uppercase tracking-wide text-slate-800">
                {review.officeCode ? `${review.officeCode} — ${review.officeName}` : review.officeName}
              </span>
              <AipRefCode code={review.officeRefCode} />
              <span className="ml-auto text-xs text-slate-600">{review.sector}</span>
            </div>
            <div className={`flex flex-wrap items-center gap-2 py-1.5 pl-7 pr-3 ${aipHeaderRow("program")}`}>
              <AipLevelChip level="program" />
              <span className="text-sm font-semibold text-slate-800">{review.program.name}</span>
              <AipRefCode code={review.program.refCode} />
            </div>
            <div className={`flex flex-wrap items-center gap-2 py-1.5 pl-11 pr-3 ${aipHeaderRow("project")}`}>
              <AipLevelChip level="project" />
              <span className="text-sm font-medium text-slate-800">{review.project.name}</span>
              <AipRefCode code={review.project.refCode} />
            </div>
          </div>

          <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20.75rem] lg:items-start">
            {/* ── The row ─────────────────────────────────────────────────────── */}
            <div className="min-w-0 space-y-4">
              <div className="border border-slate-200 px-4 py-3">
                <div className="mb-1 flex flex-wrap items-center gap-2">
                  <AipLevelChip level="activity" />
                  <AipRefCode code={review.activity.refCode} />
                  <AipFundPill label={activityFundLabel(review.activity)} />
                </div>
                <p className="whitespace-pre-line text-base text-slate-800">{review.activity.name}</p>
              </div>

              <section>
                <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
                  <h3 className="text-xs font-semibold uppercase tracking-wide text-slate-600">Amounts</h3>
                  <AipUnitCaption />
                </div>
                <div className="border border-slate-200 px-4 py-3">
                  <AipFigureStrip amounts={review.activity} />
                </div>
                {/* ⚠️ Said only to the reader who can edit: they are the one who would look for a
                    field to type these into, and there is none — the lines below own them. */}
                {review.canEdit && (
                  <p className="mt-1 text-xs text-slate-600">
                    Recomputed from the expenditure lines below &mdash; not typed here.
                  </p>
                )}
              </section>

              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-600">Details</h3>
                {/* The component draws its own bottom rule; the wrapper supplies the other three. */}
                <div className="border-x border-t border-slate-200">
                  <AipActivityFields
                    activity={review.activity}
                    canEdit={review.canEdit}
                    onSaved={onDetailsSaved}
                    defaultImplementingOffice={review.officeCode || null}
                  />
                </div>
              </section>

              <section>
                <h3 className="mb-2 text-xs font-semibold uppercase tracking-wide text-slate-600">
                  Expenditure lines
                </h3>
                <div className="border border-slate-200">
                  <AipExpenditureTable
                    activityId={review.activity.id}
                    lines={review.expenditures}
                    accounts={accounts}
                    fundingSources={funds}
                    canEdit={review.canEdit}
                    // ⚠️ Null: this surface does not load the office's ceiling, so the fund picker
                    // cannot mark which fund the ceiling applies to. The server still enforces it.
                    generalFundId={null}
                    priceIndex={priceIndex}
                    priceIndexLoading={canEdit && priceIndexLoading}
                    // Re-read the whole activity: a line write changes the figures and fund above it.
                    onChanged={() => {
                      setChanged(true);
                      void refresh();
                    }}
                  />
                </div>
              </section>
            </div>

            {/* ── The conversation ────────────────────────────────────────────── */}
            <AipCommentPanel activityId={review.activity.id} />
          </div>
        </AipCommentsProvider>
      )}
    </Modal>
  );
}

/**
 * ⚠️ Shaped like the loaded modal — banner, three path rows, then the two columns — so the dialog
 * does not jump when the read lands (`PERFORMANCE_GUIDELINES.md` §6).
 */
function ModalSkeleton() {
  return (
    <div aria-hidden className="space-y-4">
      <div className="h-10 animate-pulse bg-slate-100" />
      <div className="space-y-1">
        {[0, 1, 2].map((i) => <div key={i} className="h-8 animate-pulse bg-slate-100" />)}
      </div>
      <div className="grid gap-6 lg:grid-cols-[minmax(0,1fr)_20.75rem]">
        <div className="space-y-4">
          <div className="h-16 animate-pulse bg-slate-100" />
          <div className="h-14 animate-pulse bg-slate-100" />
          <div className="h-32 animate-pulse bg-slate-100" />
        </div>
        <div className="h-40 animate-pulse bg-slate-100" />
      </div>
    </div>
  );
}
