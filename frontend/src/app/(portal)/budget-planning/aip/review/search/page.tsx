"use client";

/**
 * AIP Review — search (V18-75 / PPDO-76, `docs/v1.8/AIP_Review_Spec.md` §4.1, §6.1).
 * Route: /budget-planning/aip/review/search
 *
 * **The page lists nothing until you ask it something**, and that is the design (decision 15): no
 * useful default list exists across nineteen offices and thousands of rows, so work is found by
 * querying for it rather than scrolled to.
 *
 * ⚠️ **Empty by default is not the same as blank.** The initial state explains how to search; the
 * no-matches state says the filters found nothing. They read differently and they are different
 * components — collapsing them tells a reviewer who has searched that they have not started.
 *
 * ⚠️ **The filter model is the whole model: OR within a field, AND across fields** (decision 16).
 * There is no boolean expression language, no precedence and no nesting. The chip interaction is
 * lifted from PR List (`inventory/pr-register`) rather than invented — counts and the
 * "Select multiple to combine" line included.
 *
 * ⚠️ **The ref-code box and the office filter are not interchangeable.** Sector is segment 1 of the
 * code and office is segment 5, so "one office, all sectors" cannot be written as a prefix — it is
 * `office = X, sector = blank`. Eleven offices really do span more than one sector. The helper text
 * under each field says so, because the instinct is to type a code for everything.
 */

import { useCallback, useEffect, useState } from "react";
import Link from "next/link";
import { useMe } from "@/lib/me-cache";
import { searchAipReview } from "@/lib/aip-review";
import { aipErrorMessage } from "@/lib/aip";
import { listOffices } from "@/lib/config";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import { AIP_SECTOR_OPTIONS } from "@/lib/aipConstants";
import { AIP_WORKFLOW, describeAipHolderForReviewer } from "@/lib/aip-workflow";
import { AipLevelChip } from "@/components/aip/entry/AipHierarchy";
import type {
  AipReviewSearchResult, AipReviewSearchRow, AipCommentNodeType, OfficeResponse,
} from "@/types";

const YEARS = [0, 1, 2].map((n) => FIRST_ENTERED_FISCAL_YEAR + n);

/** The five workflow states, in ladder order — the order the work actually moves through. */
const STATUSES: string[] = [
  AIP_WORKFLOW.draft,
  AIP_WORKFLOW.departmentReview,
  AIP_WORKFLOW.submittedToPpdo,
  AIP_WORKFLOW.returnedByPpdo,
  AIP_WORKFLOW.consolidated,
];

const STATUS_LABEL: Record<string, string> = {
  [AIP_WORKFLOW.draft]: "Draft",
  [AIP_WORKFLOW.departmentReview]: "Office review",
  [AIP_WORKFLOW.submittedToPpdo]: "With PPDO",
  [AIP_WORKFLOW.returnedByPpdo]: "Returned",
  [AIP_WORKFLOW.consolidated]: "Done",
};

interface Filters {
  officeIds: number[];
  sectors: string[];
  statuses: string[];
  refCode: string;
  title: string;
  mine: boolean;
}

const EMPTY: Filters = {
  officeIds: [], sectors: [], statuses: [], refCode: "", title: "", mine: false,
};

const PAGE_SIZE = 25;

export default function AipReviewSearchPage() {
  // ⚠️ Same guard as the review screen this page links into. The endpoint is deliberately more
  // permissive — it clamps a guest office rather than refusing it — but every result here opens a
  // reviewer-only screen, so a page full of dead ends is worse than no page.
  useMe((m) => m.canReviewAllOffices);

  const [fiscalYear, setFiscalYear] = useState(FIRST_ENTERED_FISCAL_YEAR);
  const [draft, setDraft] = useState<Filters>(EMPTY);

  // ⚠️ Two states, not one. `draft` is what the panel shows; `applied` is what produced the results
  // on screen. Searching on every keystroke would fire a query per character typed into the title
  // box, and the ref-code box is only ever meaningful once it is finished.
  const [applied, setApplied] = useState<Filters | null>(null);
  const [page, setPage] = useState(1);

  const [result, setResult] = useState<AipReviewSearchResult | null>(null);
  const [offices, setOffices] = useState<OfficeResponse[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    void listOffices({ active: "true" }).then(setOffices).catch(() => setOffices([]));
  }, []);

  const run = useCallback(async (filters: Filters, year: number, pageNo: number) => {
    setLoading(true);
    setError(null);
    try {
      setResult(await searchAipReview({
        fiscalYear: year,
        officeIds: filters.officeIds,
        sectors: filters.sectors,
        workflowStatuses: filters.statuses,
        refCode: filters.refCode,
        title: filters.title,
        mine: filters.mine,
        page: pageNo,
        pageSize: PAGE_SIZE,
      }));
    } catch (e) {
      setResult(null);
      setError(aipErrorMessage(e, "Could not run this search."));
    } finally {
      setLoading(false);
    }
  }, []);

  // Re-runs when the applied filters, the year or the page changes — never on a draft edit.
  useEffect(() => {
    if (applied === null) return;
    void run(applied, fiscalYear, page);
  }, [applied, fiscalYear, page, run]);

  function search() {
    setPage(1);
    // A new object each time, so pressing Search again with identical filters still re-runs —
    // a reviewer pressing it twice is asking whether anything has changed since.
    setApplied({ ...draft });
  }

  function clear() {
    setDraft(EMPTY);
    setApplied(null);
    setResult(null);
    setPage(1);
  }

  function toggle<K extends "sectors" | "statuses">(key: K, value: string) {
    setDraft((prev) => ({
      ...prev,
      [key]: prev[key].includes(value)
        ? prev[key].filter((v) => v !== value)
        : [...prev[key], value],
    }));
  }

  const totalPages = result ? Math.max(1, Math.ceil(result.totalCount / result.pageSize)) : 1;

  return (
    <div className="p-4 sm:p-6">
      <div className="mb-4 flex flex-wrap items-end justify-between gap-3">
        <div>
          <h1 className="text-xl font-semibold text-slate-800">AIP Review</h1>
          <p className="mt-0.5 text-sm text-slate-600">
            Find the programs, projects and activities you need to review.
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

      {/* ── The filter panel ─────────────────────────────────────────────── */}
      <div className="mb-4 border border-slate-200 bg-white">
        <div className="grid gap-4 p-4 sm:grid-cols-2">
          <div>
            <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
              Reference code
            </label>
            <input
              value={draft.refCode}
              onChange={(e) => setDraft((p) => ({ ...p, refCode: e.target.value }))}
              onKeyDown={(e) => { if (e.key === "Enter") search(); }}
              placeholder="3000-  or  1000-000-1-01-010 OR 3000-000-1-01-010"
              className="w-full border border-slate-300 bg-white px-2.5 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600"
            />
            {/* ⚠️ Says what the box is FOR, because the instinct is to type a code for everything —
                and the one query it cannot express is the most common one. */}
            <p className="mt-1 text-xs text-slate-600">
              Matches from the start of the code. Combine several with <strong>OR</strong> or a comma.
              For one office across all its sectors, use the office filter instead.
            </p>
          </div>

          <div>
            <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
              Title
            </label>
            <input
              value={draft.title}
              onChange={(e) => setDraft((p) => ({ ...p, title: e.target.value }))}
              onKeyDown={(e) => { if (e.key === "Enter") search(); }}
              placeholder="Part of a program, project or activity name"
              className="w-full border border-slate-300 bg-white px-2.5 py-1.5 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600"
            />
            <p className="mt-1 text-xs text-slate-600">
              Matched as typed — a title containing the word &ldquo;or&rdquo; is searched literally.
            </p>
          </div>

          <div>
            <label className="mb-1 block text-xs font-semibold uppercase tracking-wide text-slate-600">
              Office
            </label>
            <select
              multiple
              value={draft.officeIds.map(String)}
              onChange={(e) => setDraft((p) => ({
                ...p,
                officeIds: Array.from(e.target.selectedOptions, (o) => Number(o.value)),
              }))}
              className="h-28 w-full border border-slate-300 bg-white px-2 py-1 text-sm text-slate-800 focus:outline-none focus:ring-1 focus:ring-green-600"
            >
              {offices.map((o) => (
                <option key={o.id} value={o.id}>{o.officeCode} — {o.officeName}</option>
              ))}
            </select>
            <p className="mt-1 text-xs text-slate-600">
              Leave the sector chips blank to see all of an office&rsquo;s sectors.
            </p>
          </div>

          <div className="space-y-3">
            <div>
              <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-600">Sector</p>
              <div className="flex flex-wrap gap-2">
                {AIP_SECTOR_OPTIONS.map((s) => (
                  <Chip key={s} label={s} active={draft.sectors.includes(s)}
                    count={countOf(result?.sectorCounts, s, result != null)}
                    onClick={() => toggle("sectors", s)} />
                ))}
              </div>
            </div>

            <div>
              <p className="mb-1 text-xs font-semibold uppercase tracking-wide text-slate-600">Status</p>
              <div className="flex flex-wrap gap-2">
                {STATUSES.map((s) => (
                  <Chip key={s} label={STATUS_LABEL[s]} active={draft.statuses.includes(s)}
                    count={countOf(result?.workflowStatusCounts, s, result != null)}
                    onClick={() => toggle("statuses", s)} />
                ))}
              </div>
            </div>

            <p className="text-xs text-slate-600">Select multiple to combine.</p>
          </div>
        </div>

        <div className="flex flex-wrap items-center gap-3 border-t border-slate-200 px-4 py-3">
          <button type="button" onClick={search} disabled={loading}
            className="bg-green-700 px-4 py-1.5 text-sm font-medium text-white hover:bg-green-800 disabled:bg-slate-300">
            {loading ? "Searching…" : "Search"}
          </button>
          <button type="button" onClick={clear}
            className="px-3 py-1.5 text-sm text-slate-600 underline hover:text-slate-800">
            Clear
          </button>
          <label className="ml-auto flex items-center gap-2 text-sm text-slate-800">
            <input type="checkbox" checked={draft.mine}
              onChange={(e) => setDraft((p) => ({ ...p, mine: e.target.checked }))}
              className="h-4 w-4 accent-green-700" />
            {/* Resolved server-side from the caller's own permissions — for a PPDO reviewer this is
                the offices actually waiting on them. */}
            Only what&rsquo;s waiting on me
          </label>
        </div>
      </div>

      {/* ── Results ──────────────────────────────────────────────────────── */}
      {loading ? (
        <ResultSkeleton />
      ) : applied === null ? (
        // ⚠️ The default, and deliberate. Not a blank panel and not a spinner.
        <EmptyState
          title="Search to begin"
          body="This page shows nothing until you ask it for something — there is no useful default across every office. Filter by office, sector, status or reference code, or tick “Only what’s waiting on me”."
        />
      ) : result && result.items.length > 0 ? (
        <>
          <ResultTable rows={result.items} aipRecordId={result.aipRecordId} fiscalYear={fiscalYear} />
          <div className="mt-3 flex flex-wrap items-center justify-between gap-3 text-sm text-slate-600">
            <span>
              {result.totalCount} match{result.totalCount === 1 ? "" : "es"} · page {result.page} of {totalPages}
            </span>
            <div className="flex gap-2">
              <button type="button" disabled={result.page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="border border-slate-300 px-3 py-1 disabled:opacity-50">Previous</button>
              <button type="button" disabled={result.page >= totalPages}
                onClick={() => setPage((p) => p + 1)}
                className="border border-slate-300 px-3 py-1 disabled:opacity-50">Next</button>
            </div>
          </div>
        </>
      ) : (
        // ⚠️ A different sentence from the initial state. "No matches" and "start here" are not the
        // same message, and sharing one component tells a reviewer who has searched that they
        // haven't.
        <EmptyState
          title="No rows match these filters"
          body="Nothing in this fiscal year matches. Widen the filters — or clear them and start again."
          action={
            <button type="button" onClick={clear}
              className="border border-slate-300 px-3 py-1.5 text-sm text-slate-800 hover:bg-slate-50">
              Clear filters
            </button>
          }
        />
      )}
    </div>
  );
}

// ── Pieces ────────────────────────────────────────────────────────────────

/**
 * A chip's count, or `undefined` when there is nothing to say yet.
 *
 * ⚠️ **An absent key after a search means zero, and must render as "0".** The server groups the
 * matched rows, so a value nothing matched simply is not in the dictionary — and a chip showing no
 * badge beside a sibling showing "62" reads as "count unknown" rather than "this combination is
 * empty". Before the first search there is genuinely nothing to report, and no badge is right.
 */
function countOf(
  counts: Record<string, number> | undefined,
  key: string,
  searched: boolean
): number | undefined {
  if (!searched) return undefined;
  return counts?.[key] ?? 0;
}

/**
 * A multi-select toggle chip with its count — the PR List interaction, reused rather than
 * redesigned so the two filter panels behave the same way.
 *
 * ⚠️ The count is rendered when the server sent one, including **zero**. A chip that disappears at
 * zero would make the panel jump around as filters change, and "0" is information: it says that
 * combination is empty rather than unavailable.
 */
function Chip({
  label, active, count, onClick,
}: { label: string; active: boolean; count?: number; onClick: () => void }) {
  return (
    <button type="button" onClick={onClick} aria-pressed={active}
      className={`inline-flex items-center gap-1.5 border px-3 py-1.5 text-xs font-medium transition-colors ${
        active
          ? "border-transparent bg-green-700 text-white"
          : "border-slate-300 bg-slate-50 text-slate-800 hover:bg-slate-100"
      }`}>
      {label}
      {count != null && (
        <span className={`rounded-full px-1 text-xs ${
          active ? "bg-white/30" : "bg-slate-200/60 text-slate-600"
        }`}>{count}</span>
      )}
    </button>
  );
}

function ResultTable({
  rows, aipRecordId, fiscalYear,
}: { rows: AipReviewSearchRow[]; aipRecordId: number; fiscalYear: number }) {
  return (
    <div className="overflow-x-auto border border-slate-200 bg-white">
      <table className="w-full text-sm">
        <thead>
          <tr className="border-b border-slate-200 bg-slate-50 text-left text-xs uppercase tracking-wide text-slate-600">
            <th className="px-3 py-2 font-semibold">Level</th>
            <th className="px-3 py-2 font-semibold">Reference code</th>
            <th className="px-3 py-2 font-semibold">Name</th>
            <th className="px-3 py-2 font-semibold">Office</th>
            <th className="px-3 py-2 font-semibold">Status</th>
          </tr>
        </thead>
        <tbody className="divide-y divide-slate-200">
          {rows.map((r) => (
            <tr key={`${r.level}-${r.nodeId}`} className="hover:bg-green-25">
              <td className="px-3 py-2"><AipLevelChip level={levelOf(r.level)} /></td>
              <td className="whitespace-nowrap px-3 py-2 font-mono text-xs text-slate-600">{r.refCode}</td>
              <td className="px-3 py-2 text-slate-800">
                {/* ⚠️ An unmatched legacy row has no owning office, so it has nothing to open.
                    Rendered as plain text rather than dropped — it exists, and hiding it would look
                    like missing data. */}
                {r.officeId != null ? (
                  <Link
                    href={`/budget-planning/aip/review?officeId=${r.officeId}&fiscalYear=${fiscalYear}`}
                    className="underline decoration-slate-300 underline-offset-2 hover:decoration-green-700"
                  >
                    {r.name}
                  </Link>
                ) : r.name}
              </td>
              <td className="px-3 py-2 text-slate-600">
                {r.officeName} <span className="text-xs">· {r.sector}</span>
              </td>
              <td className="px-3 py-2 text-xs text-slate-600">
                {describeAipHolderForReviewer(r.workflowStatus)}
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      {/* aipRecordId is not rendered — it is carried so a future column (history, comment count)
          can address the record without a second lookup. */}
      <span className="hidden" data-aip-record-id={aipRecordId} />
    </div>
  );
}

/** The wire value is one of three known names; anything else is rendered as a leaf. */
function levelOf(level: AipCommentNodeType): "program" | "project" | "activity" {
  return level === "Program" ? "program" : level === "Project" ? "project" : "activity";
}

function EmptyState({
  title, body, action,
}: { title: string; body: string; action?: React.ReactNode }) {
  return (
    <div className="border border-slate-200 bg-white px-6 py-10 text-center">
      <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
      <p className="mx-auto mt-2 max-w-xl text-sm text-slate-600">{body}</p>
      {action && <div className="mt-4 flex justify-center">{action}</div>}
    </div>
  );
}

/**
 * ⚠️ Shaped like the results table — same header, same row height. A centred spinner replaced by a
 * full table is the layout shift `docs/PERFORMANCE_GUIDELINES.md` names by name.
 */
function ResultSkeleton() {
  return (
    <div className="border border-slate-200 bg-white">
      <div className="h-9 border-b border-slate-200 bg-slate-50" />
      <div className="divide-y divide-slate-200">
        {[0, 1, 2, 3, 4].map((i) => (
          <div key={i} className="flex items-center gap-4 px-3 py-2.5">
            <div className="h-4 w-16 animate-pulse bg-slate-100" />
            <div className="h-4 w-48 animate-pulse bg-slate-100" />
            <div className="h-4 flex-1 animate-pulse bg-slate-100" />
          </div>
        ))}
      </div>
    </div>
  );
}
