"use client";

/**
 * AIP Review — one office (V18-56 / PPDO-74, `docs/v1.8/AIP_Review_Spec.md` §6.2).
 * Route: /budget-planning/aip/review?officeId=<id>&fiscalYear=<yyyy>
 *
 * The PPDO consolidated reviewer's actual workplace: one office's whole AIP, read-only, with a
 * comment gutter on every row and the two decisions they hold — send it back, or take it into the
 * consolidated AIP.
 *
 * ⚠️ **This is the reviewer who may NOT edit.** There are two reviewers in this phase and they
 * differ on exactly that point: the *department-head* reviewer edits their own office's values
 * during review; the *PPDO* reviewer never edits anyone's. Two roles, one word — and this is the
 * half that is easy to get backwards. Every component below is passed `canEdit={false}`, and
 * `ReviewerWriteGuard` refuses the write server-side even if one of them ever forgets.
 *
 * ⚠️ **Not built on `aip/detail/page.tsx`.** PPDO-64 extracted that page into components precisely
 * so this screen could reuse the pieces rather than fork two thousand lines of them.
 *
 * ⚠️ **How a reviewer gets here is not this page's job.** PPDO-76's query-first search and
 * PPDO-78's kanban are what will link in; until they land the office is named in the URL, and this
 * page says so plainly rather than growing a search panel that PPDO-76 would then replace.
 */

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import { listAip, listAipExpenditures, aipErrorMessage } from "@/lib/aip";
import { listAccounts, listFundingSources } from "@/lib/config";
import {
  getAipOfficeReview, returnAipToOffice, acceptAipOffice,
} from "@/lib/aip-review";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import { AIP_WORKFLOW, describeAipHolderForReviewer } from "@/lib/aip-workflow";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import AipActivityFields from "@/components/aip/entry/AipActivityFields";
import AipExpenditureTable from "@/components/aip/entry/AipExpenditureTable";
import {
  AipCommentsProvider, AipCommentFilterBar, AipCommentAnchor,
} from "@/components/aip/entry/AipComments";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "@/components/aip/entry/AipHierarchy";
import {
  AipFigureStrip, AipFundPill, AipUnitCaption, activityFundLabel, sumActivityAmounts,
  type AipRowAmounts,
} from "@/components/aip/entry/AipRowFigures";
import type {
  AipOfficeReview, AipOfficeDetail, AipActivityDetail, AipExpenditure,
  AccountResponse, FundingSourceResponse,
} from "@/types";

/** FY2028 onward. There is no workflow, and so nothing to review, below the break year. */
const YEARS = [0, 1, 2].map((n) => FIRST_ENTERED_FISCAL_YEAR + n);

export default function AipReviewPage() {
  // ⚠️ **The page guard, and it redirects rather than rendering a refusal.** `AIP Review` is hidden
  // from the sidebar for users without the cross-office grant (spec §6.1: hidden, not disabled), so
  // anyone arriving here without it typed or was sent the URL — which is exactly the negative case
  // this ticket has to hold. This is not the enforcement: every route the page calls is gated on
  // the same flag server-side and answers a 403 regardless of what is rendered.
  useMe((m) => m.canReviewAllOffices);

  const searchParams = useSearchParams();
  const officeIdParam = Number(searchParams.get("officeId"));
  const officeId = Number.isFinite(officeIdParam) && officeIdParam > 0 ? officeIdParam : null;

  const requestedYear = Number(searchParams.get("fiscalYear"));
  const fiscalYear = YEARS.includes(requestedYear) ? requestedYear : FIRST_ENTERED_FISCAL_YEAR;

  const [review, setReview] = useState<AipOfficeReview | null>(null);
  const [accounts, setAccounts] = useState<AccountResponse[]>([]);
  const [funds, setFunds] = useState<FundingSourceResponse[]>([]);

  const [loading, setLoading] = useState(true);
  const [notOpened, setNotOpened] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [acting, setActing] = useState(false);
  const [dialog, setDialog] = useState<ConfirmDialogProps | null>(null);

  const load = useCallback(async () => {
    if (officeId == null) { setLoading(false); return; }
    setLoading(true);
    setError(null);
    setNotOpened(false);
    try {
      const list = await listAip({ fiscalYear });
      const open = list.find((r) => r.status !== "Archived");
      if (!open) { setNotOpened(true); setReview(null); return; }

      // Sequential: the review read needs the record id the list call resolves.
      setReview(await getAipOfficeReview(open.id, officeId));
    } catch (e) {
      setReview(null);
      setError(aipErrorMessage(e, "Could not load this office's AIP for review."));
    } finally {
      setLoading(false);
    }
  }, [fiscalYear, officeId]);

  useEffect(() => { void load(); }, [load]);

  // Reference data for the read-only expenditure rows — account and fund names. Fetched once, off
  // the critical path; the tree renders without it.
  //
  // ⚠️ No price index. It is ~6,400 rows and feeds only the item PICKER, which exists solely inside
  // the editors this page never opens — fetching it here would be a large request for a control
  // that cannot appear.
  useEffect(() => {
    void listAccounts().then(setAccounts).catch(() => setAccounts([]));
    void listFundingSources({ active: "true" }).then(setFunds).catch(() => setFunds([]));
  }, []);

  const atPpdo = review?.workflowStatus === AIP_WORKFLOW.submittedToPpdo;

  /**
   * Both decisions run through here.
   *
   * ⚠️ **The page is re-read after a failure too, not only after a success.** A 409 means another
   * reviewer moved this office while it was open — showing their sentence but leaving the stale
   * tree and stale buttons on screen invites the reader to press the other one and lose a second
   * race for the same reason.
   *
   * ⚠️ **The failure is surfaced AFTER the re-read, not before it.** `load` clears the banner on
   * the way in, so setting the message first and reloading second showed the reader nothing at all
   * — the office silently changed state under them with no explanation. Found by live-testing a
   * 409, which is exactly the case the message exists for.
   */
  async function act(run: () => Promise<unknown>, fallback: string) {
    setActing(true);
    setError(null);
    let failure: string | null = null;
    try {
      await run();
    } catch (e) {
      failure = aipErrorMessage(e, fallback);
    } finally {
      setActing(false);
    }

    await load();
    if (failure !== null) setError(failure);
  }

  function confirmReturn() {
    if (!review) return;
    setDialog({
      title: "Send this AIP back?",
      message:
        `${review.officeName} will be able to edit its AIP again, and your comments stay on it, `
        + "unresolved. The office's department head re-submits it when the changes are made.",
      confirmLabel: "Send back",
      variant: "warning",
      onConfirm: () => void act(
        () => returnAipToOffice(review.aipRecordId, review.officeId),
        "Could not send this AIP back."),
      onClose: () => setDialog(null),
    });
  }

  function confirmAccept() {
    if (!review) return;
    setDialog({
      title: "Accept into the consolidated AIP?",
      // ⚠️ Says the one-way part out loud. There is no un-accept in shipped code — the return path
      // refuses from Consolidated — so a reviewer must not learn that after pressing it.
      message:
        `${review.officeName} will be marked done and counted in the consolidated AIP. `
        + "This cannot be undone from here, and the office can no longer change its figures.",
      confirmLabel: "Accept",
      variant: "primary",
      onConfirm: () => void act(
        () => acceptAipOffice(review.aipRecordId, review.officeId),
        "Could not accept this office."),
      onClose: () => setDialog(null),
    });
  }

  // ── Shell ───────────────────────────────────────────────────────────────
  // The header renders in every state, before the data lands — gating the page on a spinner and
  // then swapping in a full-height tree is the layout shift PERFORMANCE_GUIDELINES names.
  return (
    <div className="p-4 sm:p-6">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">AIP Review</h1>
          <p className="mt-0.5 text-sm text-slate-600">
            {review
              ? <>FY {review.fiscalYear} · <strong className="text-slate-800">{review.officeName}</strong>
                  {review.officeCode ? ` (${review.officeCode})` : ""}</>
              : `FY ${fiscalYear} · read an office's submitted AIP, comment on it, and decide.`}
          </p>
        </div>

        {review && (
          <div className="flex flex-wrap items-center gap-2">
            {/* ⚠️ Names the HOLDER, not a bare "read-only". A reviewer looking at an office that is
                not at PPDO needs to know who has it, or the absent buttons read as a bug. */}
            <StateChip status={review.workflowStatus} />
            {atPpdo && (
              <>
                <button type="button" onClick={confirmReturn} disabled={acting}
                  className="border border-amber-300 bg-amber-50 px-3 py-1.5 text-sm font-medium text-amber-900 hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-60">
                  Send back
                </button>
                <button type="button" onClick={confirmAccept} disabled={acting}
                  className="bg-green-700 px-3 py-1.5 text-sm font-medium text-white hover:bg-green-800 disabled:cursor-not-allowed disabled:bg-slate-300">
                  Accept
                </button>
              </>
            )}
          </div>
        )}
      </div>

      {error && (
        <p className="mb-4 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
      )}

      {officeId == null ? (
        // The interim landing. ⚠️ Deliberately NOT a search panel — PPDO-76 owns finding work, and
        // a second finder here would have to be removed the week it lands.
        <EmptyState
          title="Choose an office to review"
          body="Open an office by adding its id to the address — ?officeId=12. Searching and the readiness board arrive with the AIP Review search page."
        />
      ) : loading ? (
        <ReviewSkeleton />
      ) : notOpened ? (
        <EmptyState
          title={`FY ${fiscalYear} has not been opened yet`}
          body="An administrator opens the fiscal year, which creates the AIP and populates every office's programs from its LDIP. There is nothing to review until then."
        />
      ) : review ? (
        // One comments fetch for the whole tree. A gutter control per row fetching its own would be
        // an N+1 that only shows up on a big office.
        <AipCommentsProvider aipRecordId={review.aipRecordId} officeId={review.officeId}>
          <div className="space-y-4">
            {!atPpdo && (
              // ⚠️ Says why the buttons are absent. A reviewer who opens an office mid-drafting and
              // sees no controls has no way to tell that from the feature being broken.
              //
              // ⚠️ **Two sentences, split on whether the office is still coming or already done.**
              // "…becomes available once the office submits to PPDO" is true of Draft, department
              // review and a return — and flatly wrong of an accepted office, which is never coming
              // back. Found by live-testing an accept, where the page told the reviewer to wait for
              // a submission that will not happen.
              <p className="border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
                This office&rsquo;s AIP is with{" "}
                <strong className="text-slate-800">{describeAipHolderForReviewer(review.workflowStatus)}</strong>.
                {review.workflowStatus === AIP_WORKFLOW.consolidated
                  ? " Its figures are final. You can still read it and comment on it."
                  : " You can read it and comment on it; sending it back or accepting it becomes available once the office submits to PPDO."}
              </p>
            )}

            <AipCommentFilterBar />

            {review.groups.length === 0 ? (
              <EmptyState
                title="Nothing encoded yet"
                body="This office has no programs in this AIP. There is nothing to review until it adds them from its LDIP."
              />
            ) : (
              review.groups.map((group) => (
                <GroupBlock key={group.id} group={group} accounts={accounts} funds={funds} />
              ))
            )}
          </div>
        </AipCommentsProvider>
      ) : (
        // ⚠️ The 404 case, worded so it cannot be used to tell "no such office" from "not yours" —
        // the API answers both identically on purpose (PPDO-46), and so does this.
        <EmptyState
          title="No AIP to review here"
          body={`There is no office ${officeId} in the FY ${fiscalYear} AIP that you can review.`}
        />
      )}

      {dialog && <ConfirmDialog {...dialog} />}
    </div>
  );
}

// ── One sub-office group, read-only ───────────────────────────────────────

/**
 * The entry page's group block with every control removed.
 *
 * ⚠️ Kept structurally identical to it on purpose — same levels, same tints, same figure strip. A
 * reviewer and an encoder discussing "the second project" must be looking at the same thing, and
 * the comment anchors are attached to the same rows in both.
 */
function GroupBlock({
  group, accounts, funds,
}: {
  group: AipOfficeDetail;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
}) {
  const amounts: AipRowAmounts = useMemo(
    () => sumActivityAmounts(
      group.programs.flatMap((p) => p.projects.flatMap((j) => j.activities))
    ),
    [group]
  );

  return (
    <div className="border border-slate-200 bg-white">
      <div className={`border-b border-b-slate-200 px-4 py-3 ${aipHeaderRow("office")}`}>
        <div className="flex flex-wrap items-start justify-between gap-x-6 gap-y-2">
          <div>
            <div className="flex items-center gap-2">
              <AipLevelChip level="office" />
              <AipRefCode code={group.refCode} />
            </div>
            <h2 className="mt-1 text-sm font-semibold uppercase tracking-wide text-slate-800">{group.name}</h2>
            <p className="mt-0.5 text-xs text-slate-600">
              {group.sector} · {group.programs.length} program{group.programs.length === 1 ? "" : "s"}
            </p>
          </div>
          <div className="flex flex-col items-end gap-1">
            <AipFigureStrip amounts={amounts} emphasis="strong" />
            <AipUnitCaption />
          </div>
        </div>
      </div>

      <div className="divide-y divide-slate-200">
        {group.programs.map((program) => (
          <div key={program.id} className="ml-3">
            <div className={`px-4 py-2 ${aipHeaderRow("program")}`}>
              <div className="flex items-center gap-2">
                <AipLevelChip level="program" />
                <AipRefCode code={program.refCode} />
              </div>
              <p className="mt-0.5 text-sm font-semibold text-slate-800">{program.name}</p>
              <AipCommentAnchor nodeType="Program" nodeId={program.id} />
            </div>

            <div className="mt-2 space-y-3 px-4 pb-3 pl-4">
              {program.projects.map((project) => (
                <div key={project.id}>
                  <div className={`flex flex-wrap items-center gap-2 px-3 py-1.5 ${aipHeaderRow("project")}`}>
                    <AipLevelChip level="project" />
                    <AipRefCode code={project.refCode} />
                    <span className="text-sm font-medium text-slate-800">{project.name}</span>
                  </div>
                  <div className="px-3">
                    <AipCommentAnchor nodeType="Project" nodeId={project.id} />
                  </div>
                  <div className="mt-2 space-y-2 pl-4">
                    {project.activities.map((activity) => (
                      <ActivityBlock key={activity.id} activity={activity}
                        accounts={accounts} funds={funds} />
                    ))}
                    {project.activities.length === 0 && (
                      <p className="text-xs text-slate-600">No activities under this project.</p>
                    )}
                  </div>
                </div>
              ))}
              {program.projects.length === 0 && (
                <p className="text-xs text-slate-600">No projects under this program.</p>
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}

// ── One activity, with its expenditure lines ──────────────────────────────

function ActivityBlock({
  activity, accounts, funds,
}: {
  activity: AipActivityDetail;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
}) {
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState<AipExpenditure[] | null>(null);

  // Lazily, and once: a reviewer opens a handful of activities out of an office's hundreds, and
  // fetching every activity's lines up front would be the N+1 in a different costume.
  useEffect(() => {
    if (!open || lines !== null) return;
    void listAipExpenditures(activity.id).then(setLines).catch(() => setLines([]));
  }, [open, lines, activity.id]);

  return (
    <div className="border border-slate-200 bg-white">
      <button type="button" onClick={() => setOpen((v) => !v)}
        className={`flex w-full flex-wrap items-start justify-between gap-x-4 gap-y-2 px-3 py-2 text-left hover:bg-green-25 ${aipHeaderRow("activity")}`}>
        <span className="flex flex-1 flex-wrap items-center gap-2">
          <span aria-hidden className="text-slate-300">{open ? "▾" : "▸"}</span>
          <AipLevelChip level="activity" />
          <AipRefCode code={activity.refCode} />
          <span className="text-sm text-slate-800">{activity.name}</span>
          <AipFundPill label={activityFundLabel(activity)} />
        </span>
        <AipFigureStrip amounts={activity} />
      </button>

      {/* ⚠️ Outside the disclosure <button>: nesting a button in a button is invalid HTML, and the
          click would toggle the activity instead of the comment thread. */}
      <div className="px-3 pb-1">
        <AipCommentAnchor nodeType="Activity" nodeId={activity.id} />
      </div>

      {open && (
        <>
          {/* ⚠️ canEdit={false} on both. The PPDO reviewer never edits — decision 2, permanently.
              The components already render a read view in that mode, and the server refuses the
              write anyway; this is what keeps the control off the screen in the first place. */}
          <AipActivityFields activity={activity} canEdit={false} onSaved={() => undefined} />

          {lines === null ? (
            <div className="space-y-2 px-4 py-3">
              {[0, 1].map((i) => <div key={i} className="h-4 w-full animate-pulse bg-slate-100" />)}
            </div>
          ) : (
            <AipExpenditureTable
              activityId={activity.id} lines={lines} accounts={accounts} fundingSources={funds}
              canEdit={false} generalFundId={null}
              // The picker only exists inside the editors, which cannot open here.
              priceIndex={[]} priceIndexLoading={false}
              onChanged={() => undefined} />
          )}
        </>
      )}
    </div>
  );
}

// ── Small pieces ──────────────────────────────────────────────────────────

/**
 * Where the work sits, in the reviewer's language.
 *
 * ⚠️ Amber for the one state that is theirs to act on, slate for every other — a reviewer scanning
 * between offices should be able to tell "mine to decide" from "somebody else's turn" without
 * reading the words.
 */
function StateChip({ status }: { status: string }) {
  const mine = status === AIP_WORKFLOW.submittedToPpdo;
  return (
    <span
      className={`rounded-full px-3 py-1 text-xs font-semibold ${
        mine ? "bg-amber-100 text-amber-900" : "border border-slate-300 bg-slate-50 text-slate-600"
      }`}
    >
      {`With ${describeAipHolderForReviewer(status)}`}
    </span>
  );
}

function EmptyState({ title, body }: { title: string; body: string }) {
  return (
    <div className="border border-slate-200 bg-white px-6 py-10 text-center">
      <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
      <p className="mx-auto mt-2 max-w-xl text-sm text-slate-600">{body}</p>
      <p className="mt-4 text-xs text-slate-600">
        <Link href="/budget-planning" className="underline">Back to Budget Planning</Link>
      </p>
    </div>
  );
}

/**
 * ⚠️ Shaped like the loaded page — same header band, same block heights. A centred spinner replaced
 * by a full-height tree is the layout shift `docs/PERFORMANCE_GUIDELINES.md` names by name.
 */
function ReviewSkeleton() {
  return (
    <div className="space-y-4">
      {[0, 1].map((i) => (
        <div key={i} className="border border-slate-200 bg-white">
          <div className="h-16 border-b border-slate-200 bg-slate-50" />
          <div className="space-y-2 px-4 py-3">
            <div className="h-4 w-1/3 animate-pulse bg-slate-100" />
            <div className="h-4 w-2/3 animate-pulse bg-slate-100" />
          </div>
        </div>
      ))}
    </div>
  );
}
