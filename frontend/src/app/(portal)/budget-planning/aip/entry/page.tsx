"use client";

/**
 * AIP Entry — the encoder's own tab (V18-42 / PPDO-52).
 * Route: /budget-planning/aip/entry
 *
 * ⚠️ **One node at a time (PPDO-89).** ↩️ This page used to render the office's whole tree — every
 * program, project and activity — with an activity's fields opening inline in the middle of it. At
 * the 2026-09-15 PDC demo that read as overwhelming: while entering one activity the encoder is
 * looking at every other PPA in the office. It now picks **Program → Project → Activity** first and
 * shows only the selected level, which is the shape the PDC asked for by name and the one the
 * encoders already know from the WFP entry page.
 *
 * What stays at the top, always visible (spec decision 4): the office figure header, the submit
 * checklist and the comment filter bar. Submit is an office-level action — hiding the gate inside
 * a drill-down would hide it.
 *
 * ⚠️ **Everything that names a node selects it** (decision 6). A checklist issue, an unresolved
 * comment and a child-list row all set the selection, because there is no longer a tree to scroll
 * to. Without that, a returned office cannot find the rows PPDO flagged.
 *
 * ⚠️ **The encoder never creates the record or the office row.** An Admin opens the fiscal year,
 * which creates the one base record and populates every office's programs from its LDIP (PPDO-62).
 * So the "no record" state offers **no create action** — it says who can open the year. Offering a
 * button here would put a second, side-effect create path back into a model that was rewritten
 * precisely to remove them.
 *
 * ⚠️ **Separate from `aip/detail`, deliberately.** That page is the read/edit view of any AIP,
 * including the FY≤2027 uploaded ones. This is the FY2028+ entry surface, and V18-83 splits the
 * sidebar into AIP Entry and AIP Review as separately gated siblings.
 */

import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { useMe } from "@/lib/me-cache";
import {
  listAip, getAipById, getAipReadiness, submitAip, submitAipToPpdo, returnAipToEncoder,
  aipErrorMessage,
} from "@/lib/aip";
import { listAccounts, listFundingSources, listPriceIndexForPicker } from "@/lib/config";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import { AIP_WORKFLOW, isOfficeEditable, describeAipHolder } from "@/lib/aip-workflow";
import AipAddProgramsPanel from "@/components/aip/entry/AipAddProgramsPanel";
import AipSubmitChecklist, { type AipSubmitStage } from "@/components/aip/entry/AipSubmitChecklist";
import { refreshAipNotifications, useAipNotifications } from "@/lib/aip-notifications";
import { AipCommentsProvider, AipCommentFilterBar } from "@/components/aip/entry/AipComments";
import { applyAipDeleteToTree } from "@/components/aip/entry/AipDeleteNodeButton";
import { sumActivityAmounts } from "@/components/aip/entry/AipRowFigures";
import AipEntryPicker from "@/components/aip/entry/AipEntryPicker";
import { AipOfficeHeader } from "@/components/aip/entry/AipEntryPanelParts";
import AipSelectedPanel from "@/components/aip/entry/AipSelectedPanel";
import {
  addActivityToTree, addProjectToTree, applyActivityTotals, patchActivity, patchProject,
} from "@/components/aip/entry/AipEntryTree";
import {
  EMPTY_SELECTION_IDS, idsForAipNode, listAipProgramOptions, resolveAipSelection,
  type AipSelectionIds,
} from "@/components/aip/entry/AipEntrySelection";
import type {
  AipRecordDetail, AipOfficeDetail, AipProjectDetail, AipActivityDetail,
  AipDeleteResult, AipCommentNodeType,
  AccountResponse, FundingSourceResponse, AipReadiness, PriceIndexPickerItem,
} from "@/types";

/** FY2028 onward. The entry process does not exist below the break year. */
const YEARS = [0, 1, 2].map((n) => FIRST_ENTERED_FISCAL_YEAR + n);

/**
 * Whether an office that already HAS programs is offered "+ Add programs" (Ralph, 2026-09-16).
 *
 * ⚠️ **Off, deliberately — flip this one constant to bring it back.** Year-open already populates
 * every office from its LDIP, so on a populated AIP this is only the recovery path for a program
 * the LDIP gained afterwards, and under a picker whose whole job is narrowing down it read as a
 * fourth step for something almost nobody needs.
 *
 * ↩️ **That is now the only reason left.** The other one — that the picker listed programs already
 * in the AIP, which the add endpoint then refused — was real when this was switched off and is
 * fixed: `GetAddableProgramsAsync` flags them and the panel renders them unselectable (PPDO-95).
 * So re-enabling this is a UI judgement call, not a wait on a defect.
 *
 * The **empty state** still renders the panel — an office with no programs has nothing to re-add
 * and no other way to begin.
 */
const SHOW_ADD_PROGRAMS_WHEN_POPULATED = false;

export default function AipEntryPage() {
  const me = useMe((m) => m.canAccessBudgetPlanning);
  // The department-head reviewer's grant. Office scoping is not in the flag — the server narrows
  // to the caller's own office — so this only decides whether the second submit is offered here.
  const canReview = me?.canReviewBudgetPlanning === true;
  // PPDO-75 — the same shared store the sidebar reads; no second fetch.
  const notifications = useAipNotifications(me);

  // ⚠️ The year is read from the URL so the Budget Planning hub can hand off the year it was
  // showing (PPDO-81) — the hub names a fiscal year in every sentence on it, and landing on a
  // different one reads as the link having gone somewhere else.
  //
  // ⚠️ Validated against YEARS rather than trusted: a hand-edited `?fiscalYear=2019` would otherwise
  // put the picker in a state it cannot offer, and every query below would run against a year with
  // no entry process at all. An unusable value falls back to the default rather than erroring —
  // there is nothing the reader could do about it.
  const router = useRouter();
  const searchParams = useSearchParams();
  const requestedYear = Number(searchParams.get("fiscalYear"));
  const [fiscalYear, setFiscalYear] = useState(
    YEARS.includes(requestedYear) ? requestedYear : FIRST_ENTERED_FISCAL_YEAR
  );

  // ⚠️ **The selection lives in the URL** (spec decision 5) — a reload keeps the encoder's place,
  // the returned-work banner and the review search can deep-link to a node, and "Change activity"
  // is just a param change. Read ONCE here, then mirrored back by the effect below; the URL is not
  // re-read on every render, or a `router.replace` would race the reader's own next click.
  const [ids, setIds] = useState<AipSelectionIds>(() => ({
    programId: numberParam(searchParams.get("programId")),
    projectId: numberParam(searchParams.get("projectId")),
    activityId: numberParam(searchParams.get("activityId")),
  }));
  /** Set when an id in the URL no longer resolves, or a named node is out of view. */
  const [selectionNotice, setSelectionNotice] = useState<string | null>(null);
  /**
   * The LDIP add-programs panel, opened from the office header (PPDO-89).
   *
   * ⚠️ Held HERE rather than inside the panel, because its trigger and its body are now in two
   * places: the trigger sits in the sticky header where office-level actions belong, and the body
   * must render below it in normal flow — a scrolling checkbox list pinned to the top of the
   * viewport would cover the work it was opened from.
   */
  const [addProgramsOpen, setAddProgramsOpen] = useState(false);

  const [record, setRecord]   = useState<AipRecordDetail | null>(null);
  const [readiness, setReadiness] = useState<AipReadiness | null>(null);
  const [accounts, setAccounts]   = useState<AccountResponse[]>([]);
  const [funds, setFunds]         = useState<FundingSourceResponse[]>([]);
  // ⚠️ ~6,400 rows, so it is fetched off the critical path with its own loading flag (RAL-231).
  // Without the flag the item picker is indistinguishable from an empty catalogue while it lands.
  const [priceIndex, setPriceIndex] = useState<PriceIndexPickerItem[]>([]);
  const [priceIndexLoading, setPriceIndexLoading] = useState(true);

  const [loading, setLoading]   = useState(true);
  const [notOpened, setNotOpened] = useState(false);
  const [error, setError]       = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);

  const officeId = me?.officeId ?? null;

  // Only the caller's own office's groups.
  //
  // ⚠️ This is NOT a security boundary — AipReadScope already clamps a guest office server-side,
  // and a guest never receives another office's rows. It exists because a HOST-office (PPDO) user
  // legitimately receives every office in the record, and rendering all 25 trees above a checklist
  // that covers only their own office is incoherent: found by live-testing, where an encoder saw
  // another office's programs above a panel reading "0 activities in this office".
  //
  // The readiness endpoint narrows the same way (AipSubmitService.ResolveAsync), so the picker and
  // the checklist now describe the same set of work.
  const myGroups: AipOfficeDetail[] = useMemo(
    () => (record?.offices ?? []).filter((o) => officeId != null && o.officeId === officeId),
    [record, officeId]
  );

  const programOptions = useMemo(() => listAipProgramOptions(myGroups), [myGroups]);

  /**
   * The selected nodes, resolved against the loaded tree.
   *
   * ⚠️ Null while the record is still loading, and that is not the same as "nothing selected": a
   * URL naming a program would otherwise resolve against an empty tree, decide the program is gone
   * and clear the reader's deep link a beat before the data it points at arrives.
   */
  const selection = useMemo(
    () => (record ? resolveAipSelection(myGroups, ids) : null),
    [record, myGroups, ids]
  );

  // A stale id falls back to the deepest ancestor that still resolves, and says which level went.
  useEffect(() => {
    if (!selection?.notice) return;
    setSelectionNotice(selection.notice);
    setIds(selection.ids);
  }, [selection]);

  // The selection mirrored back into the URL. `scroll: false` — a replace that jumped the page to
  // the top on every pick would undo the reason the panel is on screen.
  useEffect(() => {
    const q = new URLSearchParams({ fiscalYear: String(fiscalYear) });
    if (ids.programId != null) q.set("programId", String(ids.programId));
    if (ids.projectId != null) q.set("projectId", String(ids.projectId));
    if (ids.activityId != null) q.set("activityId", String(ids.activityId));
    router.replace(`/budget-planning/aip/entry?${q.toString()}`, { scroll: false });
  }, [router, fiscalYear, ids]);

  const workflowStatus = readiness?.workflowStatus ?? AIP_WORKFLOW.draft;
  // ↩️ Was `=== "Draft"` until PPDO-70. The office keeps editing through department review and
  // after a PPDO return; the lock falls when the work reaches PPDO. Mirrors the server's
  // AipWorkflowStatus.IsOfficeEditable — change both together.
  const canEdit = isOfficeEditable(workflowStatus);
  const holder = describeAipHolder(workflowStatus);

  // The division filter applies to the HOST office only, and only when the user has a division —
  // the same condition AipReadScope uses. A guest office is never division-filtered, so telling
  // them about it would be a lie.
  const divisionFiltered = me?.isHostOffice === true && me.divisionId != null && !!me.division;

  const load = useCallback(async () => {
    setLoading(true);
    setError(null);
    setNotOpened(false);
    setRecord(null);
    setReadiness(null);
    try {
      const list = await listAip({ fiscalYear });
      const open = list.find((r) => r.status !== "Archived");
      if (!open) { setNotOpened(true); return; }

      // Sequential, not Promise.all on the first two: the readiness call needs the record id.
      const detail = await getAipById(open.id);
      setRecord(detail);
      setReadiness(await getAipReadiness(open.id));
    } catch (e) {
      setError(aipErrorMessage(e, "Could not load the AIP for this fiscal year."));
    } finally {
      setLoading(false);
    }
  }, [fiscalYear]);

  useEffect(() => { void load(); }, [load]);

  // Reference data, fetched once and off the critical path — the page renders without it.
  useEffect(() => {
    void listAccounts().then(setAccounts).catch(() => setAccounts([]));
    void listFundingSources({ active: "true" }).then(setFunds).catch(() => setFunds([]));
    void listPriceIndexForPicker({ active: "true" })
      .then(setPriceIndex)
      .catch(() => setPriceIndex([]))
      .finally(() => setPriceIndexLoading(false));
  }, []);

  async function refreshReadiness() {
    if (!record) return;
    try { setReadiness(await getAipReadiness(record.id)); } catch { /* checklist stays stale, page works */ }
  }

  // ── Selection ───────────────────────────────────────────────────────────
  //
  // ⚠️ One setter for every way of choosing a node — the picker, a child row, a checklist issue, a
  // comment, and a freshly created row all go through here. Three of those were added after the
  // picker, and each would otherwise have had to remember to clear the stale-id notice.
  const select = useCallback((next: AipSelectionIds) => {
    setSelectionNotice(null);
    setIds(next);
  }, []);

  /** Selects the node a checklist issue or a comment names, wherever it sits in the tree. */
  const selectNode = useCallback(
    (nodeType: AipCommentNodeType, nodeId: number) => {
      const found = idsForAipNode(myGroups, nodeType, nodeId);
      if (!found) {
        // Possible for a host-office user whose division filter excludes the row, and for a row
        // deleted in another tab. Said, rather than a click that appears to do nothing.
        setSelectionNotice("That row is not in the part of this AIP you can see.");
        return;
      }
      select(found);
    },
    [myGroups, select]
  );

  // "Change activity" clears the box and puts the cursor back in it — the encoder's next move is
  // always to pick another one, and a cleared field they then have to click is a wasted step.
  //
  // ⚠️ Focused from an EFFECT, not from the click handler. ↩️ A `requestAnimationFrame` here left
  // focus on `<body>`: the click unmounts the activity panel the button lives in, and the browser
  // moves focus off a removed element — landing after the frame callback and undoing it. An effect
  // runs after that commit's DOM mutations, so it is the last word. Found by live-testing.
  const activityInputRef = useRef<HTMLInputElement>(null);
  const [focusActivityLookup, setFocusActivityLookup] = useState(false);

  useEffect(() => {
    if (!focusActivityLookup) return;
    setFocusActivityLookup(false);
    activityInputRef.current?.focus();
  }, [focusActivityLookup]);

  function changeActivity() {
    select({ programId: ids.programId, projectId: ids.projectId, activityId: null });
    setFocusActivityLookup(true);
  }

  // ── Submit hops ─────────────────────────────────────────────────────────

  async function doSubmit() {
    if (!record) return;
    setSubmitting(true);
    setError(null);
    try {
      await submitAip(record.id);
      await load();
      void refreshAipNotifications();
    } catch (e) {
      setError(aipErrorMessage(e, "Could not submit this AIP."));
    } finally {
      setSubmitting(false);
    }
  }

  /**
   * The second hop (PPDO-69): the department head sends the reviewed work on to PPDO.
   *
   * ⚠️ A separate call with a separate authority, not a variant of `doSubmit`. The server re-runs
   * the whole completeness and ceiling gate here — the department head may have edited values
   * during review — so this can be refused even though the encoder's submit passed.
   */
  async function doSubmitToPpdo() {
    if (!record || officeId == null) return;
    setSubmitting(true);
    setError(null);
    try {
      await submitAipToPpdo(record.id, officeId);
      await load();
      void refreshAipNotifications();
    } catch (e) {
      setError(aipErrorMessage(e, "Could not send this AIP to PPDO."));
    } finally {
      setSubmitting(false);
    }
  }

  /** The department head hands the work back down to the encoders (added 2026-09-14). */
  async function doReturnToEncoder() {
    if (!record || officeId == null) return;
    setSubmitting(true);
    setError(null);
    try {
      await returnAipToEncoder(record.id, officeId);
      await load();
      void refreshAipNotifications();
    } catch (e) {
      setError(aipErrorMessage(e, "Could not return this AIP to the encoders."));
    } finally {
      setSubmitting(false);
    }
  }

  /**
   * Which of the two submits this reader is standing at, if either.
   *
   * ⚠️ **Deliberately independent of `canEdit`** — they answer different questions. Since PPDO-70
   * an encoder in department review *can* edit and *cannot* send the work on; conflating the two
   * either strands the office or tells them their work is frozen when it is not.
   */
  const submitStage: AipSubmitStage =
    workflowStatus === AIP_WORKFLOW.draft
      ? { kind: "encoder", onSubmit: doSubmit }
      : workflowStatus === AIP_WORKFLOW.departmentReview ||
          workflowStatus === AIP_WORKFLOW.returnedByPpdo
        ? canReview
          ? {
              kind: "toPpdo",
              resubmit: workflowStatus === AIP_WORKFLOW.returnedByPpdo,
              onSubmit: doSubmitToPpdo,
              // Department review only — the one state the server returns work down from.
              onReturnToEncoder:
                workflowStatus === AIP_WORKFLOW.departmentReview ? doReturnToEncoder : undefined,
            }
          // An encoder while their department head holds it: still editable, just not theirs to
          // send on.
          : { kind: "awaitingReviewer", holder }
        : { kind: "locked", holder };

  /**
   * PPDO-75 — the returned banner for the year on screen.
   *
   * ⚠️ Cross-checked against the state this page just loaded, not taken from the store alone: the store
   * refreshes a moment after an action, and a banner saying "returned" over a panel that already reads
   * "sent to PPDO" would contradict itself for that moment.
   */
  const returnedNotice = notifications?.returned.find((r) => r.fiscalYear === fiscalYear) ?? null;
  const returnedBanner =
    returnedNotice?.returnedBy === "Ppdo" && workflowStatus === AIP_WORKFLOW.returnedByPpdo
      ? "PPDO sent this back for changes — see the comments on the activities, then re-submit."
      : returnedNotice?.returnedBy === "DepartmentHead" && workflowStatus === AIP_WORKFLOW.draft
        ? "Your department head returned this to the encoders — see their comments, then submit again."
        : null;

  // ── Tree edits ──────────────────────────────────────────────────────────
  //
  // ⚠️ Every one of these SPLICES the change into `record` rather than reloading it. `load()` here
  // was reported as "the page reloads when a user creates a new activity or project": it clears
  // `record`, so the skeleton flashes and the panel the encoder was working in is torn down and
  // rebuilt — right after an action taken inside it.

  function onProjectAdded(project: AipProjectDetail) {
    setRecord((prev) => prev && addProjectToTree(prev, project));
    // Decision 7 — the new node becomes the selection. Creating a project and then having to find
    // it in the lookup you just created it from is a step that exists only because of the code.
    select({ programId: project.programId, projectId: project.id, activityId: null });
    void refreshReadiness();
  }

  function onActivityAdded(activity: AipActivityDetail) {
    setRecord((prev) => prev && addActivityToTree(prev, activity));
    select({ programId: ids.programId, projectId: activity.projectId, activityId: activity.id });
    void refreshReadiness();
  }

  /** PPDO-88 — the node goes, renumbered codes are patched in, the selection moves to the parent. */
  function onDeleted(result: AipDeleteResult) {
    setRecord((prev) => prev && applyAipDeleteToTree(prev, result));
    if (result.deletedNodeType === "Activity" && result.deletedId === ids.activityId) {
      select({ programId: ids.programId, projectId: ids.projectId, activityId: null });
    } else if (result.deletedNodeType === "Project" && result.deletedId === ids.projectId) {
      select({ programId: ids.programId, projectId: null, activityId: null });
    }
    void refreshReadiness();
  }

  // ── Shell ───────────────────────────────────────────────────────────────
  // ⚠️ The header and the year picker render immediately, in every state. Gating the whole page on
  // a spinner and then swapping in a full-height panel is the CLS failure PERFORMANCE_GUIDELINES
  // calls out by name.
  return (
    <div className="p-4 sm:p-6">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">AIP Entry</h1>
          <p className="mt-0.5 text-sm text-slate-600">
            Build your office&rsquo;s part of the Annual Investment Program.
          </p>
        </div>
        <div>
          <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
            Fiscal year
          </label>
          <select
            value={fiscalYear}
            onChange={(e) => {
              setFiscalYear(Number(e.target.value));
              // ⚠️ The selection is cleared with the year. Ids are per-record, so carrying them
              // across would name rows of the year just left and resolve to a stale-id notice on
              // arrival — a message about nothing the reader did.
              select(EMPTY_SELECTION_IDS);
            }}
            className="border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600">
            {YEARS.map((y) => <option key={y} value={y}>FY {y}</option>)}
          </select>
        </div>
      </div>

      {error && (
        <p className="mb-4 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
      )}

      {!loading && returnedBanner && (
        <p role="status" className="mb-4 border border-slate-200 bg-amber-100 px-4 py-3 text-sm text-amber-900">
          <strong className="font-semibold">Returned.</strong> {returnedBanner}
        </p>
      )}

      {/* ⚠️ Say that a filter is on. AipReadScope shows a HOST-office user only the programs
          assigned to their own division (spec §3.3) — correct, and completely invisible: a group
          whose programs belong to another division renders as "0 programs", which reads as missing
          data. Someone went and checked the LDIP because of it. The plan warned about this
          mechanism in exactly those words: "the failure looks like missing data, not an error". */}
      {divisionFiltered && (
        <p className="mb-4 border border-slate-200 bg-slate-50 px-4 py-3 text-sm text-slate-600">
          Showing the programs assigned to <strong className="text-slate-800">{me!.division}</strong>.
          Your office&rsquo;s other programs are here but belong to other divisions — ask an
          administrator if one should be assigned to yours.
        </p>
      )}

      {loading ? (
        <EntrySkeleton />
      ) : notOpened ? (
        // ⚠️ No create action. Opening a year is an Admin action that populates every office from
        // its LDIP — an encoder cannot do it, and offering a button would restore the side-effect
        // create path PPDO-62 removed.
        <EmptyState
          title={`FY ${fiscalYear} has not been opened yet`}
          body="An administrator opens the fiscal year, which creates the AIP and populates every office's programs from its LDIP. Once that is done, your office's programs will appear here."
        />
      ) : record && officeId != null ? (
        // One fetch of the office's comments for every panel — a control per row fetching its own
        // would be an N+1 that only shows up on a big office.
        <AipCommentsProvider aipRecordId={record.id} officeId={officeId}>
          <div className="space-y-4">
            <AipOfficeHeader
              fiscalYear={fiscalYear}
              officeName={me?.officeName ?? "Your office"}
              groups={myGroups.map((g) => ({
                id: g.id,
                name: g.name,
                amounts: sumActivityAmounts(
                  g.programs.flatMap((p) => p.projects.flatMap((j) => j.activities))
                ),
              }))}
              // ⚠️ Offered only once the office HAS programs — the empty state below renders the
              // panel itself, where adding them is the whole point rather than a recovery path.
              action={
                SHOW_ADD_PROGRAMS_WHEN_POPULATED &&
                canEdit && myGroups.length > 0 && !addProgramsOpen ? (
                  <button
                    type="button"
                    onClick={() => setAddProgramsOpen(true)}
                    className="text-xs font-medium text-green-700 hover:underline"
                  >
                    + Add programs
                  </button>
                ) : undefined
              }
            />

            {/* ⚠️ Below the sticky header, not inside it. Programs are the LDIP's closed list and
                the year-open already populates every office from it — this is the recovery path for
                a program the LDIP gained afterwards, so it stays out of the encoding flow until
                someone asks for it (Ralph, 2026-09-16). */}
            {SHOW_ADD_PROGRAMS_WHEN_POPULATED && canEdit && myGroups.length > 0 && (
              <AipAddProgramsPanel
                aipRecordId={record.id} officeConfigId={officeId}
                open={addProgramsOpen} onOpenChange={setAddProgramsOpen}
                onAdded={() => void load()} />
            )}

            {readiness && (
              <AipSubmitChecklist
                readiness={readiness}
                stage={submitStage}
                submitting={submitting}
                history={{ aipRecordId: record.id, officeId }}
                onSelectActivity={(activityId) => selectNode("Activity", activityId)}
              />
            )}

            {/* Renders nothing when there is nothing outstanding — a permanent "0 unresolved" strip
                would be noise on the page encoders use most. */}
            <AipCommentFilterBar onSelectNode={selectNode} />

            {myGroups.length === 0 ? (
              <EmptyState
                title="Nothing here yet"
                body="Your office has no programs in this AIP. Add them from your LDIP to begin — the AIP cannot contain a program the LDIP does not."
                action={
                  canEdit ? (
                    <AipAddProgramsPanel
                      aipRecordId={record.id} officeConfigId={officeId}
                      onAdded={() => void load()} />
                  ) : undefined
                }
              />
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
                  activityInputRef={activityInputRef}
                />

                <AipSelectedPanel
                  selection={selection}
                  canEdit={canEdit}
                  holder={holder}
                  accounts={accounts}
                  funds={funds}
                  generalFundId={readiness?.ceiling?.generalFundId ?? null}
                  priceIndex={priceIndex}
                  priceIndexLoading={priceIndexLoading}
                  // ⚠️ The CODE, not the office name. The form's Implementing Office column (3)
                  // prints codes — "OPV", and "OPV/LFC/HRMO" where an activity is run jointly — so
                  // a default of the full name would be retyped by every encoder (PPDO-80).
                  defaultImplementingOffice={me?.officeCode ?? null}
                  onSelect={select}
                  onChangeActivity={changeActivity}
                  onProjectAdded={onProjectAdded}
                  onActivityAdded={onActivityAdded}
                  onDeleted={onDeleted}
                  // ⚠️ Patched in place, and `activities` deliberately not in the patch — the
                  // update endpoint returns the project without them, so spreading the whole
                  // response would empty the project the encoder is standing in.
                  // ⚠️ No readiness refresh, unlike the activity handlers: the submit checklist
                  // gates on activities and their costing, and none of the project fields appear
                  // in it — description and objective are optional and do not print on Annex B.
                  onProjectUpdated={(patch) =>
                    setRecord((prev) => prev && patchProject(prev, patch.id, patch))
                  }
                  // ⚠️ An expenditure change must NOT reload the record: the panel would remount
                  // under the encoder mid-edit. The write endpoint returns the recomputed activity
                  // precisely so the figures can update in place.
                  onActivityTotals={(r) => {
                    setRecord((prev) => prev && applyActivityTotals(prev, r));
                    void refreshReadiness();
                  }}
                  // ⚠️ Readiness is refreshed too, not just the tree: eSRE is one of the checks the
                  // submit gate blocks on, so saving it has to move the checklist at the top of the
                  // page or the encoder fixes something and sees no change.
                  onActivityDetails={(updated) => {
                    setRecord((prev) => prev && patchActivity(prev, updated.id, updated));
                    void refreshReadiness();
                  }}
                />

              </>
            )}
          </div>
        </AipCommentsProvider>
      ) : (
        // A user with no office resolves to "sees nothing" rather than "sees everything" —
        // DECISION F. An empty state, not an error.
        <EmptyState
          title="No office assigned"
          body="Your account is not assigned to an office yet, so there is no AIP to build. Ask an administrator to assign one."
        />
      )}
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

function EmptyState({ title, body, action }: { title: string; body: string; action?: React.ReactNode }) {
  return (
    <div className="border border-slate-200 bg-white px-6 py-10 text-center">
      <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
      <p className="mx-auto mt-2 max-w-xl text-sm text-slate-600">{body}</p>
      {action && <div className="mt-4 flex justify-center">{action}</div>}
      <p className="mt-4 text-xs text-slate-600">
        <Link href="/budget-planning/ldip" className="underline">Go to LDIP</Link>
      </p>
    </div>
  );
}

/**
 * ⚠️ A skeleton shaped like the loaded page, not a spinner — the office header, the checklist box,
 * three lookup-shaped bars and one panel, at the heights they load at. A tiny centred spinner
 * replaced by a full-height panel is the layout shift `docs/PERFORMANCE_GUIDELINES.md` names.
 */
function EntrySkeleton() {
  return (
    <div className="space-y-4">
      <div className="h-16 w-full animate-pulse border border-slate-200 bg-white" />
      <div className="h-28 w-full animate-pulse border border-slate-200 bg-white" />
      <div className="grid grid-cols-1 gap-3 sm:grid-cols-3">
        {[0, 1, 2].map((i) => (
          <div key={i}>
            <div className="mb-1 h-3 w-1/3 bg-slate-100" />
            <div className="h-8 w-full animate-pulse bg-slate-100" />
          </div>
        ))}
      </div>
      <div className="border border-slate-200 bg-white p-4">
        <div className="h-10 bg-slate-50" />
        <div className="mt-3 space-y-2">
          <div className="h-4 w-1/3 animate-pulse bg-slate-100" />
          <div className="h-4 w-2/3 animate-pulse bg-slate-100" />
        </div>
      </div>
    </div>
  );
}
