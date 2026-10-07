"use client";

import Link from "next/link";
import { proposalEditorHref } from "@/lib/investment-proposals";
import type { OfficeProposalSummary, OfficeSummary, ProposalCounts } from "@/types";

/**
 * The dashboard's Investment proposals band and the reviewer's all-offices card (PPDO-180, design
 * pass F11, wireframe boards 1 and 2).
 *
 * Investment proposals came in with v1.8.0 and AIP submit warns about projects without a Final one,
 * but the dashboard never mentioned them. The band says how many projects have a Final proposal, a
 * Draft, or none, and names the few that still need one.
 *
 * Every figure comes from the server, counted in the Investment Proposals list's own scope, so the
 * band and the list cannot disagree. Nothing here filters or scopes.
 */

const SEGMENTS: { key: keyof ProposalCounts; label: string; bar: string; dot: string }[] = [
  { key: "final", label: "final", bar: "bg-green-600", dot: "bg-green-600" },
  { key: "draft", label: "draft", bar: "bg-amber-500", dot: "bg-amber-500" },
  { key: "none", label: "no proposal", bar: "bg-slate-200", dot: "bg-slate-300" },
];

export const total = (c: ProposalCounts): number => c.final + c.draft + c.none;

/** One stacked bar of final / draft / none, with the counts under it. */
export function ProposalBar({ counts }: { counts: ProposalCounts }) {
  const all = total(counts);
  const label = `${counts.final} final, ${counts.draft} draft, ${counts.none} with no proposal, of ${all} projects`;
  return (
    <div className="flex flex-col gap-2">
      <div role="img" aria-label={label} className="flex h-2.5 bg-slate-100">
        {all > 0 &&
          SEGMENTS.map((s) =>
            counts[s.key] > 0 ? (
              <span key={s.key} className={s.bar} style={{ width: `${(counts[s.key] / all) * 100}%` }} />
            ) : null
          )}
      </div>
      <p className="flex flex-wrap items-center gap-x-4 gap-y-1 text-sm text-slate-600" aria-hidden>
        {SEGMENTS.map((s) => (
          <span key={s.key} className="inline-flex items-center gap-1.5">
            <span className={`inline-block h-2 w-2 rounded-full ${s.dot}`} />
            <span className="font-semibold text-slate-800 tabular-nums">{counts[s.key].toLocaleString("en-PH")}</span>
            {s.label}
          </span>
        ))}
        <span className="text-slate-500">of {all.toLocaleString("en-PH")} projects</span>
      </p>
    </div>
  );
}

const ROW_LINK =
  "shrink-0 px-2.5 py-1 border border-slate-200 bg-white hover:bg-slate-50 text-xs font-medium text-slate-800 transition-colors";

/** The own-office band's body: the bar, then the projects that still need a proposal. */
export function OfficeProposals({
  summary,
  fiscalYear,
  officeId,
  listHref,
  canCreate,
}: {
  summary: OfficeProposalSummary;
  fiscalYear: number;
  officeId: number | null;
  listHref: string;
  /** A cross-office reviewer reads other offices' proposals only; the band never offers them Create. */
  canCreate: boolean;
}) {
  const all = total(summary.counts);
  if (all === 0) {
    return <p className="px-5 py-6 text-sm text-slate-600">No projects in this year&apos;s AIP yet.</p>;
  }
  const waiting = summary.counts.draft + summary.counts.none;
  const more = waiting - summary.needsAttention.length;

  return (
    <div className="px-5 py-4 flex flex-col gap-4">
      <ProposalBar counts={summary.counts} />

      {waiting === 0 ? (
        <p className="text-sm text-slate-600">Every project has a final proposal.</p>
      ) : (
        <div>
          <h3 className="text-xs font-semibold text-slate-600">Needs a proposal</h3>
          <ul className="mt-1 divide-y divide-slate-100">
            {summary.needsAttention.map((p) => {
              const createHref = `/budget-planning/proposals/edit?${new URLSearchParams({
                projectId: String(p.aipProjectId),
                fiscalYear: String(fiscalYear),
                ...(officeId != null ? { officeId: String(officeId) } : {}),
              }).toString()}`;
              return (
                <li key={p.aipProjectId} className="flex items-center gap-3 py-2">
                  <div className="min-w-0 flex-1">
                    <p className="text-sm text-slate-800 truncate">{p.projectName}</p>
                    <p className="text-xs text-slate-500">
                      {p.projectRefCode} · {p.status === "Draft" ? "Draft" : "No proposal"}
                    </p>
                  </div>
                  {p.proposalId != null ? (
                    <Link href={proposalEditorHref(p.proposalId)} className={ROW_LINK}>
                      Open
                    </Link>
                  ) : canCreate ? (
                    <Link href={createHref} className={ROW_LINK}>
                      Create
                    </Link>
                  ) : null}
                </li>
              );
            })}
          </ul>
          {more > 0 && (
            <Link href={listHref} className="mt-1 inline-block text-xs font-medium text-green-600 hover:text-green-700">
              {more.toLocaleString("en-PH")} more on the Investment Proposals page
            </Link>
          )}
        </div>
      )}
    </div>
  );
}

/** How many offices in PPDO review the card names. */
const REVIEW_ROWS = 5;

/**
 * The reviewer's all-offices card: the bar across every office, then the offices in PPDO review
 * with the fewest final proposals first: the ones whose proposals most need a look before acceptance.
 */
export function AllOfficesProposals({
  offices,
  fiscalYear,
}: {
  offices: OfficeSummary[];
  fiscalYear: number;
}) {
  const counted = offices.filter((o) => o.proposals != null);
  const sum: ProposalCounts = counted.reduce(
    (acc, o) => ({
      final: acc.final + o.proposals!.final,
      draft: acc.draft + o.proposals!.draft,
      none: acc.none + o.proposals!.none,
    }),
    { final: 0, draft: 0, none: 0 }
  );
  const inReview = counted
    .filter((o) => o.readinessColumn === "PpdoReview")
    .sort((a, b) => a.proposals!.final - b.proposals!.final || a.officeCode.localeCompare(b.officeCode));

  return (
    <div className="px-5 py-4 flex flex-col gap-4">
      {total(sum) === 0 ? (
        <p className="text-sm text-slate-600">No office has projects in this year&apos;s AIP yet.</p>
      ) : (
        <ProposalBar counts={sum} />
      )}

      <div>
        <h3 className="text-xs font-semibold text-slate-600">Offices in PPDO review, fewest final first</h3>
        {inReview.length === 0 ? (
          <p className="mt-1 text-sm text-slate-600">No office is in PPDO review.</p>
        ) : (
          <ul className="mt-1 divide-y divide-slate-100">
            {inReview.slice(0, REVIEW_ROWS).map((o) => (
              <li key={o.officeId} className="flex items-center gap-3 py-2">
                <span className="w-16 shrink-0 text-sm font-semibold text-slate-800">{o.officeCode}</span>
                <span className="flex-1 min-w-0 text-sm text-slate-600 truncate">
                  {o.proposals!.final} of {total(o.proposals!)} final
                </span>
                <Link
                  href={`/budget-planning/proposals?fiscalYear=${fiscalYear}&officeId=${o.officeId}`}
                  className={ROW_LINK}
                >
                  Open
                </Link>
              </li>
            ))}
          </ul>
        )}
      </div>

      <p className="text-xs text-slate-500">
        Read and export only. Proposals are written and finalized by each office.
      </p>
    </div>
  );
}

/** Skeleton for either body: a bar, its legend, and three rows. */
export function ProposalsSkeleton() {
  return (
    <div aria-hidden className="px-5 py-4 flex flex-col gap-4">
      <div className="h-2.5 bg-slate-100 animate-pulse" />
      <div className="h-4 w-2/3 bg-slate-100 animate-pulse" />
      {Array.from({ length: 3 }).map((_, i) => (
        <div key={i} className="h-9 bg-slate-100 animate-pulse" />
      ))}
    </div>
  );
}
