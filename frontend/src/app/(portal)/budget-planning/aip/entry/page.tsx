"use client";

/**
 * AIP Entry — the encoder's own tab (V18-42 / PPDO-52).
 * Route: /budget-planning/aip/entry
 *
 * Three stages, in this order, because each needs the one before it:
 *   1. Sub-office group + programs (from the LDIP)  — AipAddProgramsPanel
 *   2. Project, then activity
 *   3. Expenditure lines against the activity        — AipExpenditureTable
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

import { useCallback, useEffect, useMemo, useState } from "react";
import Link from "next/link";
import { useMe } from "@/lib/me-cache";
import {
  listAip, getAipById, getAipReadiness, submitAip, submitAipToPpdo,
  addAipProject, addAipActivity, aipErrorMessage,
} from "@/lib/aip";
import { listAccounts, listFundingSources, listPriceIndexForPicker } from "@/lib/config";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import { fmtThousands } from "@/lib/aip-units";
import AipAddProgramsPanel from "@/components/aip/entry/AipAddProgramsPanel";
import AipExpenditureTable from "@/components/aip/entry/AipExpenditureTable";
import AipActivityFields from "@/components/aip/entry/AipActivityFields";
import AipSubmitChecklist, { type AipSubmitStage } from "@/components/aip/entry/AipSubmitChecklist";
import { AipLevelChip, AipRefCode, aipHeaderRow } from "@/components/aip/entry/AipHierarchy";
import { listAipExpenditures } from "@/lib/aip";
import type {
  AipRecordDetail, AipOfficeDetail, AipProjectDetail, AipActivityDetail, AipExpenditure,
  AipExpenditureWriteResult,
  AccountResponse, FundingSourceResponse, AipReadiness, PriceIndexPickerItem,
} from "@/types";

/** FY2028 onward. The entry process does not exist below the break year. */
const YEARS = [0, 1, 2].map((n) => FIRST_ENTERED_FISCAL_YEAR + n);

export default function AipEntryPage() {
  const me = useMe((m) => m.canAccessBudgetPlanning);
  // The department-head reviewer's grant. Office scoping is not in the flag — the server narrows
  // to the caller's own office — so this only decides whether the second submit is offered here.
  const canReview = me?.canReviewBudgetPlanning === true;

  const [fiscalYear, setFiscalYear] = useState(FIRST_ENTERED_FISCAL_YEAR);
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
  // The readiness endpoint narrows the same way (AipSubmitService.ResolveAsync), so the tree and
  // the checklist now describe the same set of work.
  const myGroups: AipOfficeDetail[] = useMemo(
    () => (record?.offices ?? []).filter((o) => officeId != null && o.officeId === officeId),
    [record, officeId]
  );

  const workflowStatus = readiness?.workflowStatus ?? "Draft";
  const canEdit = workflowStatus === "Draft";

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

  async function doSubmit() {
    if (!record) return;
    setSubmitting(true);
    setError(null);
    try {
      await submitAip(record.id);
      await load();
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
    } catch (e) {
      setError(aipErrorMessage(e, "Could not send this AIP to PPDO."));
    } finally {
      setSubmitting(false);
    }
  }

  /**
   * Which of the two submits this reader is standing at, if either.
   *
   * ⚠️ Deliberately independent of `canEdit`. A department head whose office is in department
   * review cannot edit the tree (that lock is PPDO-70's to move) but must still be able to send
   * the work on — those are different permissions and conflating them strands the office.
   */
  const submitStage: AipSubmitStage =
    workflowStatus === "Draft"
      ? { kind: "encoder", onSubmit: doSubmit }
      : canReview && (workflowStatus === "DepartmentReview" || workflowStatus === "ReturnedByPpdo")
        ? {
            kind: "toPpdo",
            resubmit: workflowStatus === "ReturnedByPpdo",
            onSubmit: doSubmitToPpdo,
          }
        : { kind: "readOnly", holder: describeStatus(workflowStatus) };

  // ── Shell ───────────────────────────────────────────────────────────────
  // ⚠️ The header and the year picker render immediately, in every state. Gating the whole page on
  // a spinner and then swapping in a full-height tree is the CLS failure PERFORMANCE_GUIDELINES
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
          <select value={fiscalYear} onChange={(e) => setFiscalYear(Number(e.target.value))}
            className="border border-slate-300 bg-white px-3 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600">
            {YEARS.map((y) => <option key={y} value={y}>FY {y}</option>)}
          </select>
        </div>
      </div>

      {error && (
        <p className="mb-4 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">{error}</p>
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
        <div className="space-y-4">
          {readiness && (
            <AipSubmitChecklist
              readiness={readiness}
              stage={submitStage}
              submitting={submitting}
            />
          )}

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
              {myGroups.map((group) => (
                <GroupBlock
                  key={group.id} group={group} canEdit={canEdit}
                  accounts={accounts} funds={funds}
                  priceIndex={priceIndex} priceIndexLoading={priceIndexLoading}
                  // Which fund the ceiling actually checks (V18-46 is General Fund only), so the
                  // picker can mark it. An encoder otherwise has no way to tell why GF behaves
                  // differently from every other fund at submit.
                  generalFundId={readiness?.ceiling?.generalFundId ?? null}
                  divisionFiltered={divisionFiltered}
                  // ⚠️ A new project or activity is SPLICED in, not reloaded. `load()` here was
                  // reported as "the page reloads when a user creates a new activity or project":
                  // it clears `record`, so the skeleton flashes, the scroll jumps to the top and
                  // every expanded activity closes — right after an action taken deep in the tree.
                  //
                  // Readiness is refreshed for both. A new activity adds a "not costed" issue, and
                  // a new project can clear the office-level "empty" one.
                  onProjectAdded={(project) => {
                    setRecord((prev) => prev && addProjectToTree(prev, project));
                    void refreshReadiness();
                  }}
                  onActivityAdded={(activity) => {
                    setRecord((prev) => prev && addActivityToTree(prev, activity));
                    void refreshReadiness();
                  }}
                  // ⚠️ An expenditure change must NOT reload the record. Doing so remounts the
                  // whole tree and the activity the encoder is working inside snaps shut — found
                  // by live-testing. The write endpoint returns the recomputed activity precisely
                  // so the row can update in place, which is also one fewer round trip per line.
                  onActivityTotals={(r) => {
                    setRecord((prev) => prev && applyActivityTotals(prev, r));
                    void refreshReadiness();
                  }}
                  // ⚠️ Readiness is refreshed too, not just the tree: eSRE and CC typology are
                  // two of the checks the submit gate blocks on, so saving them has to move the
                  // checklist at the top of the page or the encoder fixes something and sees no
                  // change.
                  onActivityDetails={(updated) => {
                    setRecord((prev) => prev && patchActivity(prev, updated.id, updated));
                    void refreshReadiness();
                  }}
                />
              ))}

              {canEdit && (
                <AipAddProgramsPanel
                  aipRecordId={record.id} officeConfigId={officeId}
                  onAdded={() => void load()} />
              )}
            </>
          )}
        </div>
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

// ── One sub-office group ──────────────────────────────────────────────────

function GroupBlock({
  group, canEdit, accounts, funds, generalFundId, divisionFiltered,
  priceIndex, priceIndexLoading,
  onProjectAdded, onActivityAdded, onActivityTotals, onActivityDetails,
}: {
  group: AipOfficeDetail;
  canEdit: boolean;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
  generalFundId: number | null;
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  /** True when this user only sees their own division's programs — changes what "0" means. */
  divisionFiltered: boolean;
  onProjectAdded: (project: AipProjectDetail) => void;
  onActivityAdded: (activity: AipActivityDetail) => void;
  onActivityTotals: (result: AipExpenditureWriteResult) => void;
  onActivityDetails: (updated: AipActivityDetail) => void;
}) {
  return (
    <div className="border border-slate-200 bg-white">
      <div className={`border-b border-b-slate-200 px-4 py-3 ${aipHeaderRow("office")}`}>
        <div className="flex items-center gap-2">
          <AipLevelChip level="office" />
          <AipRefCode code={group.refCode} />
        </div>
        <h2 className="mt-1 text-sm font-semibold uppercase tracking-wide text-slate-800">{group.name}</h2>
        <p className="mt-0.5 text-xs text-slate-600">
          {group.sector} · {group.programs.length} program{group.programs.length === 1 ? "" : "s"}
        </p>
        {/* ⚠️ "0 programs" on its own is indistinguishable from an empty group. When the division
            filter is on, an empty block far more often means "assigned elsewhere" than "nothing
            here" — so say which. */}
        {group.programs.length === 0 && divisionFiltered && (
          <p className="mt-1 text-xs text-slate-600">
            None of this group&rsquo;s programs are assigned to your division. They exist — they are
            just someone else&rsquo;s to encode.
          </p>
        )}
      </div>

      <div className="divide-y divide-slate-200">
        {group.programs.map((program) => (
          <div key={program.id} className="ml-3">
            {/* ⚠️ The tint is on the HEADER strip, not the block. Tinting the whole block would
                make each nested level sit on its parent's colour, and the ladder would read as
                one wash instead of four steps. */}
            <div className={`px-4 py-2 ${aipHeaderRow("program")}`}>
              <div className="flex items-center gap-2">
                <AipLevelChip level="program" />
                <AipRefCode code={program.refCode} />
              </div>
              {/* Semibold, a step below the office's uppercase heading and a step above the
                  project's medium — the type carries the level even without the chip. */}
              <p className="mt-0.5 text-sm font-semibold text-slate-800">{program.name}</p>
            </div>

            <div className="mt-2 space-y-3 px-4 pb-3 pl-4">
              {program.projects.map((project) => (
                <div key={project.id}>
                  <div className={`flex flex-wrap items-center gap-2 px-3 py-1.5 ${aipHeaderRow("project")}`}>
                    <AipLevelChip level="project" />
                    <AipRefCode code={project.refCode} />
                    <span className="text-sm font-medium text-slate-800">{project.name}</span>
                  </div>
                  <div className="mt-2 space-y-2 pl-4">
                    {project.activities.map((activity) => (
                      <ActivityBlock key={activity.id} activity={activity} canEdit={canEdit}
                        accounts={accounts} funds={funds} generalFundId={generalFundId}
                        priceIndex={priceIndex} priceIndexLoading={priceIndexLoading}
                        onTotals={onActivityTotals} onDetails={onActivityDetails} />
                    ))}
                    {canEdit && (
                      <InlineAdd label="+ Add activity" placeholder="Activity description"
                        onAdd={async (name) => {
                          // ⚠️ The created node is USED, not discarded. Discarding it is what
                          // forced the reload that made the page appear to refresh.
                          onActivityAdded(await addAipActivity(project.id, {
                            name, esreCode: null, implementingOffice: null,
                            startDate: null, endDate: null, expectedOutputs: null,
                            fundingSourceRaw: null, ps: null, mooe: null, co: null,
                            ccAdaptation: null, ccMitigation: null, ccTypologyCode: null,
                          }));
                        }} />
                    )}
                  </div>
                </div>
              ))}
              {canEdit && (
                <InlineAdd label="+ Add project" placeholder="Project name"
                  onAdd={async (name) => onProjectAdded(await addAipProject(program.id, { name }))} />
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
  activity, canEdit, accounts, funds, generalFundId, priceIndex, priceIndexLoading,
  onTotals, onDetails,
}: {
  activity: AipActivityDetail;
  canEdit: boolean;
  accounts: AccountResponse[];
  funds: FundingSourceResponse[];
  generalFundId: number | null;
  priceIndex: PriceIndexPickerItem[];
  priceIndexLoading: boolean;
  onTotals: (result: AipExpenditureWriteResult) => void;
  onDetails: (updated: AipActivityDetail) => void;
}) {
  const [open, setOpen] = useState(false);
  const [lines, setLines] = useState<AipExpenditure[] | null>(null);

  useEffect(() => {
    if (!open || lines !== null) return;
    void listAipExpenditures(activity.id).then(setLines).catch(() => setLines([]));
  }, [open, lines, activity.id]);

  return (
    <div className="border border-slate-200 bg-white">
      <button type="button" onClick={() => setOpen((v) => !v)}
        className={`flex w-full items-start justify-between gap-3 px-3 py-2 text-left hover:bg-green-25 ${aipHeaderRow("activity")}`}>
        <span className="flex flex-wrap items-center gap-2">
          {/* A disclosure caret, because this is the one level that opens. Decorative, so
              slate-300 is the right token; the chip beside it carries the meaning. */}
          <span aria-hidden className="text-slate-300">{open ? "▾" : "▸"}</span>
          <AipLevelChip level="activity" />
          <AipRefCode code={activity.refCode} />
          {/* Normal weight — the leaf. Every level above it is heavier, so depth reads downward. */}
          <span className="text-sm text-slate-800">{activity.name}</span>
        </span>
        <span className="whitespace-nowrap text-sm tabular-nums text-slate-800">
          {/* ⚠️ null and 0 are different states here — never costed vs costed at zero (V18-34). */}
          {activity.total == null ? "—" : fmtThousands(activity.total)}
        </span>
      </button>

      {open && (
        <>
          {/* ⚠️ Above the lines, not below. eSRE and CC typology block submit just as hard as a
              missing costing does, and an encoder who opens an activity to cost it should see
              what else it still needs in the same glance. */}
          <AipActivityFields activity={activity} canEdit={canEdit} onSaved={onDetails} />

          {lines === null ? (
            <div className="space-y-2 px-4 py-3">
              {[0, 1].map((i) => <div key={i} className="h-4 w-full animate-pulse bg-slate-100" />)}
            </div>
          ) : (
          <AipExpenditureTable
            activityId={activity.id} lines={lines} accounts={accounts} fundingSources={funds}
            canEdit={canEdit} generalFundId={generalFundId}
            priceIndex={priceIndex} priceIndexLoading={priceIndexLoading}
            onChanged={(result) => {
              // Refetch just this activity's lines, and hand the recomputed totals upward. The
              // record is NOT reloaded, so this row stays open and stays where it is.
              void listAipExpenditures(activity.id).then(setLines).catch(() => undefined);
              onTotals(result);
            }} />
          )}
        </>
      )}
    </div>
  );
}

/**
 * Replaces one activity in the tree, immutably, merging `patch` over it.
 *
 * ⚠️ Exists so a save does not have to reload the record. Reloading remounts every ActivityBlock,
 * and each keeps its own open/closed state — so the row the encoder is working in closes under
 * them. Found by live-testing.
 */
function patchActivity(
  record: AipRecordDetail, activityId: number, patch: Partial<AipActivityDetail>
): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs.map((program) => ({
        ...program,
        projects: program.projects.map((project) => ({
          ...project,
          activities: project.activities.map((activity) =>
            activity.id === activityId ? { ...activity, ...patch } : activity
          ),
        })),
      })),
    })),
  };
}

/**
 * Appends a newly created project to its program, immutably.
 *
 * ⚠️ Exists for the same reason `patchActivity` does. `addAipProject` returns the created node
 * carrying its own `programId`, so the tree can absorb it directly — calling `load()` instead
 * tears the whole tree down and rebuilds it, which flashes the skeleton, scrolls to the top and
 * closes every expanded activity. It reads as the page reloading, and that is what it was
 * reported as.
 */
function addProjectToTree(record: AipRecordDetail, project: AipProjectDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs.map((program) =>
        program.id === project.programId
          ? { ...program, projects: [...program.projects, project] }
          : program
      ),
    })),
  };
}

/**
 * Appends a newly created activity to its project, immutably.
 *
 * ℹ️ Appended, not inserted by ref code. A new node always takes the next code in its parent's
 * sequence (`RefCodeAllocator`), so the end of the list is its sorted position — the same order a
 * reload would produce.
 */
function addActivityToTree(record: AipRecordDetail, activity: AipActivityDetail): AipRecordDetail {
  return {
    ...record,
    offices: record.offices.map((office) => ({
      ...office,
      programs: office.programs.map((program) => ({
        ...program,
        projects: program.projects.map((project) =>
          project.id === activity.projectId
            ? { ...project, activities: [...project.activities, activity] }
            : project
        ),
      })),
    })),
  };
}

/** The totals half of the above — what an expenditure write hands back. */
function applyActivityTotals(
  record: AipRecordDetail, r: AipExpenditureWriteResult
): AipRecordDetail {
  return patchActivity(record, r.activityId, {
    ps: r.activityPs, mooe: r.activityMooe, co: r.activityCo, total: r.activityTotal,
  });
}

// ── Small pieces ──────────────────────────────────────────────────────────

function InlineAdd({
  label, placeholder, onAdd,
}: { label: string; placeholder: string; onAdd: (name: string) => Promise<void> }) {
  const [open, setOpen] = useState(false);
  const [name, setName] = useState("");
  const [busy, setBusy] = useState(false);

  if (!open) {
    return (
      <button type="button" onClick={() => setOpen(true)}
        className="text-xs font-medium text-green-700 hover:underline">{label}</button>
    );
  }
  return (
    <div className="flex gap-2">
      <input value={name} onChange={(e) => setName(e.target.value)} placeholder={placeholder}
        className="flex-1 border border-slate-300 bg-white px-2 py-1 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600" />
      <button type="button" disabled={busy || !name.trim()}
        onClick={async () => {
          setBusy(true);
          try { await onAdd(name.trim()); setName(""); setOpen(false); } finally { setBusy(false); }
        }}
        className="bg-green-700 px-3 py-1 text-sm text-white disabled:bg-slate-300">Add</button>
      <button type="button" onClick={() => setOpen(false)}
        className="px-2 py-1 text-sm text-slate-600 hover:underline">Cancel</button>
    </div>
  );
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
 * ⚠️ A skeleton shaped like the loaded page, not a spinner. Same header, same block heights — a
 * tiny centred spinner replaced by a full-height tree is the layout shift
 * `docs/PERFORMANCE_GUIDELINES.md` names.
 */
function EntrySkeleton() {
  return (
    <div className="space-y-4">
      <div className="h-28 w-full animate-pulse border border-slate-200 bg-white" />
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

function describeStatus(status: string): string {
  switch (status) {
    case "DepartmentReview": return "your department head";
    case "SubmittedToPpdo":  return "PPDO";
    case "ReturnedByPpdo":   return "you — returned by PPDO";
    case "Consolidated":     return "the consolidated AIP";
    default:                 return status;
  }
}
