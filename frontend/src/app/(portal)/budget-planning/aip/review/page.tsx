"use client";

/**
 * AIP Review — one office (V18-56 / PPDO-74, PPDO-94, `docs/v1.8/AIP_Review_Layout_Spec.md`).
 * Route: /budget-planning/aip/review?officeId=<id>&fiscalYear=<yyyy>&view=&programId=&projectId=&activityId=
 *
 * The PPDO consolidated reviewer's actual workplace: one office's whole AIP, read-only, with a
 * comment gutter on every row and the two decisions they hold — send it back, or take it into the
 * consolidated AIP.
 *
 * ⚠️ **Two views, one screen (PPDO-94).** A reviewer usually arrives targeting a node — a search
 * result, a kanban card, an unresolved comment — so **Drill-down** (AIP Entry's Program → Project →
 * Activity picker, reused unchanged) is the default. But Accept and Send back are decisions about
 * the whole office, so today's tree survives as **Full office**, one click away via the segmented
 * control in the sticky header. Neither view replaces the other (spec §2).
 *
 * ⚠️ **This is the reviewer who may NOT edit.** There are two reviewers in this phase and they
 * differ on exactly that point: the *department-head* reviewer edits their own office's values
 * during review; the *PPDO* reviewer never edits anyone's. Every panel gets `canEdit={false}` and
 * `lockedReason={null}` — null, not a holder name, because there is no holder to name here (spec
 * decision 5). `ReviewerWriteGuard` refuses the write server-side even if this page ever forgot.
 *
 * ⚠️ **Not built on `aip/detail/page.tsx`.** PPDO-64 extracted that page into components precisely
 * so this screen could reuse the pieces rather than fork two thousand lines of them.
 *
 * ⚠️ **Selection lives in the URL**, same shape as AIP Entry (`resolveAipSelection`,
 * `AipEntrySelection`) — a reload, a shared link, and a search or kanban deep-link all land on the
 * same node (spec decision 7, 9).
 */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import { canOpenAipOfficeReview, budgetPlanningFallback } from "@/lib/budget-planning-access";
import { listAip, aipErrorMessage } from "@/lib/aip";
import { listAccounts, listFundingSources } from "@/lib/config";
import {
  getAipOfficeReview, returnAipToOffice, acceptAipOffice, reopenAipOffice,
} from "@/lib/aip-review";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import { AIP_WORKFLOW, describeAipHolderForReviewer } from "@/lib/aip-workflow";
import { refreshAipNotifications } from "@/lib/aip-notifications";
import ConfirmDialog, { type ConfirmDialogProps } from "@/components/ui/ConfirmDialog";
import AipHistoryButton from "@/components/aip/review/AipHistoryButton";
import AipReviewFullTree from "@/components/aip/review/AipReviewFullTree";
import AipReviewViewSwitch, { type AipReviewView } from "@/components/aip/review/AipReviewViewSwitch";
import AipEntryPicker from "@/components/aip/entry/AipEntryPicker";
import AipSelectedPanel from "@/components/aip/entry/AipSelectedPanel";
import { AipOfficeHeader } from "@/components/aip/entry/AipEntryPanelParts";
import { AipCommentsProvider, AipCommentFilterBar } from "@/components/aip/entry/AipComments";
import { sumActivityAmounts } from "@/components/aip/entry/AipRowFigures";
import {
  idsForAipNode, listAipProgramOptions, resolveAipSelection,
  type AipSelectionIds,
} from "@/components/aip/entry/AipEntrySelection";
import type {
  AipOfficeReview, AccountResponse, FundingSourceResponse, AipCommentNodeType,
} from "@/types";

/** FY2028 onward. There is no workflow, and so nothing to review, below the break year. */
const YEARS = [0, 1, 2].map((n) => FIRST_ENTERED_FISCAL_YEAR + n);

export default function AipReviewPage() {
  const searchParams = useSearchParams();
  const officeIdParam = Number(searchParams.get("officeId"));
  const officeId = Number.isFinite(officeIdParam) && officeIdParam > 0 ? officeIdParam : null;

  // ⚠️ **The page guard, and it redirects rather than rendering a refusal.** `AIP Review` is hidden
  // from the sidebar for users without the cross-office grant (spec §6.1: hidden, not disabled), so
  // anyone arriving here without it typed or was sent the URL — which is exactly the negative case
  // PPDO-74 had to hold. This is not the enforcement: every route the page calls is gated on the
  // same flag server-side and answers a 403 regardless of what is rendered.
  //
  // ⚠️ Reads the SHARED rule (PPDO-79) so the sidebar and this page cannot disagree, and falls back
  // to the Budget Planning hub rather than `/dashboard` — a guest-office user has no dashboard, so
  // that would be a redirect to another redirect.
  useMe(canOpenAipOfficeReview, budgetPlanningFallback);

  // ⚠️ **No office named means "help me find one", and that page now exists** — PPDO-76's search.
  // Until it shipped this rendered an interim empty state saying office selection would arrive with
  // it; leaving that copy would describe a missing feature that is now one click away.
  const router = useRouter();
  useEffect(() => {
    if (officeIdParam <= 0 || !Number.isFinite(officeIdParam)) {
      router.replace("/budget-planning/aip/review/search");
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [officeIdParam]);


  const requestedYear = Number(searchParams.get("fiscalYear"));
  const fiscalYear = YEARS.includes(requestedYear) ? requestedYear : FIRST_ENTERED_FISCAL_YEAR;

  // ⚠️ **View and selection are read ONCE here, then mirrored back by the effect below** — same
  // pattern as AIP Entry (PPDO-89). This page is not remounted by a query-only navigation within
  // itself, so a second deep-link while already on the screen will not re-seed these; every real
  // arrival (from search, the kanban, a reload, a pasted link) is a fresh mount and reads correctly.
  const [view, setView] = useState<AipReviewView>(searchParams.get("view") === "full" ? "full" : "drilldown");
  const [ids, setIds] = useState<AipSelectionIds>(() => ({
    programId: numberParam(searchParams.get("programId")),
    projectId: numberParam(searchParams.get("projectId")),
    activityId: numberParam(searchParams.get("activityId")),
  }));
  /** Set when an id in the URL no longer resolves, or a named node is out of view. */
  const [selectionNotice, setSelectionNotice] = useState<string | null>(null);

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

  // Already scoped to this one office by the endpoint — no further division/office filter needed
  // (unlike AIP Entry's `myGroups`, which narrows a HOST-office user's whole-record fetch).
  const myGroups = useMemo(() => review?.groups ?? [], [review]);
  const programOptions = useMemo(() => listAipProgramOptions(myGroups), [myGroups]);

  /** Null while still loading — see AIP Entry's identical guard for why that matters. */
  const selection = useMemo(
    () => (review ? resolveAipSelection(myGroups, ids) : null),
    [review, myGroups, ids]
  );

  // A stale id falls back to the deepest ancestor that still resolves, and says which level went.
  useEffect(() => {
    if (!selection?.notice) return;
    setSelectionNotice(selection.notice);
    setIds(selection.ids);
  }, [selection]);

  /**
   * PPDO-94 decision 9 — a search result names a project or activity by its OWN id alone; it has
   * no reason to know that row's ancestors. `resolveAipSelection` needs `programId` to do anything,
   * so a leaf-only deep link is resolved against the loaded tree the same way a comment or checklist
   * issue is (`idsForAipNode`) — once, the first time the tree is available.
   */
  const resolvedDeepLink = useRef(false);
  useEffect(() => {
    if (resolvedDeepLink.current || !review) return;
    resolvedDeepLink.current = true;
    if (ids.programId != null) return;
    const leaf: { type: "Activity" | "Project"; id: number } | null =
      ids.activityId != null ? { type: "Activity", id: ids.activityId }
        : ids.projectId != null ? { type: "Project", id: ids.projectId }
          : null;
    if (!leaf) return;
    const found = idsForAipNode(myGroups, leaf.type, leaf.id);
    if (found) setIds(found);
  }, [review, myGroups, ids]);

  // The view and the selection mirrored back into the URL (spec decision 7). `scroll: false` — a
  // replace that jumped the page on every pick would undo the reason the panel is on screen.
  useEffect(() => {
    if (officeId == null) return;
    const q = new URLSearchParams({ officeId: String(officeId), fiscalYear: String(fiscalYear) });
    if (view === "full") q.set("view", "full");
    if (ids.programId != null) q.set("programId", String(ids.programId));
    if (ids.projectId != null) q.set("projectId", String(ids.projectId));
    if (ids.activityId != null) q.set("activityId", String(ids.activityId));
    router.replace(`/budget-planning/aip/review?${q.toString()}`, { scroll: false });
  }, [router, officeId, fiscalYear, view, ids]);

  // ⚠️ One setter for every way of choosing a node — the picker, a child row, and a comment all go
  // through here, same as AIP Entry.
  const select = useCallback((next: AipSelectionIds) => {
    setSelectionNotice(null);
    setIds(next);
  }, []);

  /** Selects the node an unresolved comment names, wherever it sits in the tree (spec decision 8). */
  const selectNode = useCallback(
    (nodeType: AipCommentNodeType, nodeId: number) => {
      const found = idsForAipNode(myGroups, nodeType, nodeId);
      if (!found) {
        setSelectionNotice("That row is not in the part of this AIP you can see.");
        return;
      }
      select(found);
    },
    [myGroups, select]
  );

  const atPpdo = review?.workflowStatus === AIP_WORKFLOW.submittedToPpdo;
  const accepted = review?.workflowStatus === AIP_WORKFLOW.consolidated;
  const hasGroups = myGroups.length > 0;

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
    // PPDO-75 — the sidebar count moves with the decision, success or 409 alike: either way the
    // office's state may have changed since the count was read.
    void refreshAipNotifications();
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
      // ↩️ Was "this cannot be undone from here" until 2026-09-14, when re-open was added. It still
      // says what accepting does to the office's ability to edit, which is the part that matters.
      message:
        `${review.officeName} will be marked done and counted in the consolidated AIP. `
        + "The office can no longer change its figures unless a reviewer re-opens it.",
      confirmLabel: "Accept",
      variant: "primary",
      onConfirm: () => void act(
        () => acceptAipOffice(review.aipRecordId, review.officeId),
        "Could not accept this office."),
      onClose: () => setDialog(null),
    });
  }

  /** Added 2026-09-14 — an accepted office back into the office's hands. */
  function confirmReopen() {
    if (!review) return;
    setDialog({
      title: "Re-open and send this AIP back?",
      message:
        `${review.officeName} leaves the consolidated AIP and can edit its AIP again. `
        + "Its department head re-submits it when the changes are made.",
      confirmLabel: "Re-open and send back",
      variant: "warning",
      onConfirm: () => void act(
        () => reopenAipOffice(review.aipRecordId, review.officeId),
        "Could not re-open this office."),
      onClose: () => setDialog(null),
    });
  }

  // ── Shell ───────────────────────────────────────────────────────────────
  // The header renders in every state, before the data lands — gating the page on a spinner and
  // then swapping in a full-height tree is the layout shift PERFORMANCE_GUIDELINES names.
  return (
    <div className="p-4 sm:p-6">
      <div className="mb-4">
        <h1 className="text-xl font-semibold text-slate-800">AIP Review</h1>
        <p className="mt-0.5 text-sm text-slate-600">
          {review
            ? <>FY {review.fiscalYear} · <strong className="text-slate-800">{review.officeName}</strong>
                {review.officeCode ? ` (${review.officeCode})` : ""}</>
            : `FY ${fiscalYear} · read an office's submitted AIP, comment on it, and decide.`}
        </p>
      </div>

      {error && (
        <p className="mb-4 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
      )}

      {officeId == null ? (
        // Redirecting to the search (see the effect above). A skeleton rather than a message: the
        // redirect is immediate, and a sentence nobody has time to read is worse than nothing.
        <ReviewSkeleton />
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
            <AipOfficeHeader
              fiscalYear={review.fiscalYear}
              officeName={`${review.officeName}${review.officeCode ? ` (${review.officeCode})` : ""}`}
              groups={myGroups.map((g) => ({
                id: g.id,
                name: g.name,
                amounts: sumActivityAmounts(
                  g.programs.flatMap((p) => p.projects.flatMap((j) => j.activities))
                ),
              }))}
              action={
                <div className="flex flex-wrap items-center gap-2">
                  <StateChip status={review.workflowStatus} />
                  {/* PPDO-77. At every status, not only at PPDO: an accepted or returned office is
                      exactly the one a reviewer wants to trace. */}
                  <AipHistoryButton aipRecordId={review.aipRecordId} officeId={review.officeId} />
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
                  {accepted && (
                    <button type="button" onClick={confirmReopen} disabled={acting}
                      className="border border-amber-300 bg-amber-50 px-3 py-1.5 text-sm font-medium text-amber-900 hover:bg-amber-100 disabled:cursor-not-allowed disabled:opacity-60">
                      Re-open and send back
                    </button>
                  )}
                  {/* Hidden alongside the picker on an unencoded office (spec: "Empty — office not
                      encoded" — the switch is hidden, not rendered empty). */}
                  {hasGroups && <AipReviewViewSwitch view={view} onChange={setView} />}
                </div>
              }
            />

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
                  ? " Its figures are final. You can still read it and comment on it, or re-open it and send it back to the office."
                  : " You can read it and comment on it; sending it back or accepting it becomes available once the office submits to PPDO."}
              </p>
            )}

            {/* ⚠️ The unresolved-comment filter SELECTS the node in Drill-down (spec decision 8),
                same as AIP Entry. In Full office the chips keep today's behaviour — the anchors open
                in place, because the whole tree is on screen. */}
            <AipCommentFilterBar onSelectNode={view === "drilldown" ? selectNode : undefined} />

            {!hasGroups ? (
              <EmptyState
                title="Nothing encoded yet"
                body="This office has no programs in this AIP. There is nothing to review until it adds them from its LDIP."
              />
            ) : view === "full" ? (
              <AipReviewFullTree groups={myGroups} accounts={accounts} funds={funds} />
            ) : (
              <>
                {selectionNotice && (
                  <p role="status" className="border border-slate-200 bg-amber-50 px-4 py-3 text-sm text-amber-900">
                    {selectionNotice}
                  </p>
                )}

                <AipEntryPicker
                  programOptions={programOptions}
                  severalGroups={myGroups.length > 1}
                  ids={ids}
                  projects={selection?.program?.projects ?? []}
                  activities={selection?.project?.activities ?? []}
                  onChange={select}
                />

                <AipSelectedPanel
                  selection={selection}
                  canEdit={false}
                  // ⚠️ Null, not a holder name (spec decision 5) — the PPDO reviewer was never going
                  // to add or delete anything here, so there is nothing to explain.
                  holder={null}
                  accounts={accounts}
                  funds={funds}
                  generalFundId={null}
                  priceIndex={[]}
                  priceIndexLoading={false}
                  // The implementing-office picker only renders in the edit view, which never opens
                  // on a permanently read-only screen.
                  offices={[]}
                  proponentOfficeCode={null}
                  onSelect={select}
                  onChangeActivity={() => select({ programId: ids.programId, projectId: ids.projectId, activityId: null })}
                  // canEdit is always false here, so none of the four writes below can fire — the
                  // props are required by AipSelectedPanel's shape (spec: reused unchanged) and stay
                  // as no-ops rather than forking the component to make them optional.
                  onProjectAdded={() => undefined}
                  onActivityAdded={() => undefined}
                  onDeleted={() => undefined}
                  onProjectUpdated={() => undefined}
                  onActivityTotals={() => undefined}
                  onActivityDetails={() => undefined}
                />
              </>
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

// ── Small pieces ──────────────────────────────────────────────────────────

/** A URL param that must be a positive integer id, or nothing. */
function numberParam(raw: string | null): number | null {
  if (!raw) return null;
  const n = Number(raw);
  return Number.isInteger(n) && n > 0 ? n : null;
}

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
        <Link href="/budget-planning" className="underline">Back to Investment Planning</Link>
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
