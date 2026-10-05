"use client";

import Link from "next/link";
import StatusPill from "@/components/ui/StatusPill";
import { formatMoneyShort } from "@/lib/money";
import { FIRST_ENTERED_FISCAL_YEAR } from "@/lib/aip-fiscal-years";
import type { OfficeSummary, ReadinessColumn } from "@/types";

/**
 * OfficeBoard — the readiness board (PPDO-78, `docs/v1.8/AIP_Review_Spec.md` §6.3). The Offices
 * band's second view, beside `OfficeTable`, drawn from the same rows.
 *
 * Wireframe: `docs/v1.8/wireframes/readiness-board/`. Things that are deliberate:
 *
 *   1. **The column comes from the server** (`readinessColumn`). This component only groups by it.
 *      Working it out here from activity counts and workflow state would be a second copy of the
 *      rule, and the table's Submission column beside it reads the first.
 *   2. **No "% complete", anywhere.** There is a denominator for programs but none for activities —
 *      nobody knows how many activities an office will create. Column position is the signal and
 *      the counts are context. Do not let a card grow a progress bar.
 *   3. **Not Started is a compact list, not cards.** Early in the season nearly every office sits
 *      there; as cards they would turn the board into one tall column. PPDO-179 (F10): it is one
 *      wrapped run of chips, with "N have no reviewer · Assign reviewers" at the TOP — next to the
 *      red dots it explains, not under fifteen rows. The PPDO review lane is tinted: it is the one
 *      the reader of this board acts on.
 *   4. **Money is abbreviated** (`₱1.12M of ₱10M`) — the table keeps exact pesos.
 *   5. **Every office opens the AIP Review search filtered to it**, Not Started ones included: their
 *      LDIP programs are already in the AIP, so the search has rows to show.
 */

export const BOARD_COLUMNS: { key: ReadinessColumn; title: string }[] = [
  { key: "NotStarted", title: "Not started" },
  { key: "InProgress", title: "In progress" },
  { key: "OfficeReview", title: "Office review" },
  { key: "PpdoReview", title: "PPDO review" },
  { key: "Done", title: "Done" },
];

const HEADING = "text-xs font-semibold uppercase tracking-wide text-slate-600";
const COUNT = "rounded-full bg-slate-100 px-2 text-xs font-medium leading-[18px] text-slate-600 tabular-nums";
const LANE = "flex min-h-[96px] flex-col gap-1.5 border border-slate-100 bg-slate-50 p-2";
// PPDO-179 (F10) — the reviewer's own column, tinted with the PPDO green tokens.
const LANE_PPDO = "flex min-h-[96px] flex-col gap-1.5 border border-green-200 bg-green-50 p-2";

function plural(n: number, one: string, many: string): string {
  return `${n.toLocaleString("en-PH")} ${n === 1 ? one : many}`;
}

export default function OfficeBoard({
  offices,
  fiscalYear,
  canAssignReviewers = false,
}: {
  offices: OfficeSummary[];
  fiscalYear: number | null;
  /** Whether the reader may open User Management, where a reviewer is assigned. Without it the
   *  "no reviewer" line is a statement, not a link. */
  canAssignReviewers?: boolean;
}) {
  // The search only offers the entered years; below the break year it opens on its own default.
  const searchHref = (officeId: number) =>
    `/budget-planning/aip/review/search?officeId=${officeId}${
      fiscalYear != null && fiscalYear >= FIRST_ENTERED_FISCAL_YEAR ? `&fiscalYear=${fiscalYear}` : ""
    }`;

  return (
    // Five lanes need room; below that they scroll inside the band rather than the page.
    <div className="overflow-x-auto px-5 py-4">
      <div className="grid min-w-[960px] grid-cols-5 items-start gap-3">
        {BOARD_COLUMNS.map((col) => {
          const items = offices.filter((o) => o.readinessColumn === col.key);
          const compact = col.key === "NotStarted";
          return (
            <div key={col.key} className="flex min-w-0 flex-col gap-2">
              <div className="flex items-center justify-between gap-2">
                <span className={HEADING}>{col.title}</span>
                <span className={COUNT}>{items.length}</span>
              </div>

              <div className={col.key === "PpdoReview" ? LANE_PPDO : LANE}>
                {items.length === 0 ? (
                  // The lane stays, so the board keeps its shape.
                  <p className="my-auto text-center text-xs text-slate-500">No offices</p>
                ) : compact ? (
                  <NotStartedList
                    items={items}
                    searchHref={searchHref}
                    canAssignReviewers={canAssignReviewers}
                  />
                ) : (
                  items.map((o) => (
                    <OfficeCard key={o.officeId} office={o} href={searchHref(o.officeId)} />
                  ))
                )}
              </div>
            </div>
          );
        })}
      </div>
    </div>
  );
}

/** The Not started lane: the no-reviewer line first, then every office as one wrapped run of chips. */
function NotStartedList({
  items,
  searchHref,
  canAssignReviewers,
}: {
  items: OfficeSummary[];
  searchHref: (officeId: number) => string;
  canAssignReviewers: boolean;
}) {
  const withoutReviewer = items.filter((o) => o.reviewerName == null).length;
  return (
    <>
      {withoutReviewer > 0 && (
        // One inline run, so the dot stays with the words and the line wraps as text does in a
        // narrow lane — a flex row left the dot alone on its own line.
        <p className="text-[11px] leading-4 text-slate-600">
          <span className="mr-1.5 inline-block h-1.5 w-1.5 rounded-full bg-danger-500 align-middle" aria-hidden />
          {withoutReviewer.toLocaleString("en-PH")} {withoutReviewer === 1 ? "has" : "have"} no reviewer
          {" — cannot submit"}
          {canAssignReviewers && (
            <>
              {" · "}
              <Link href="/admin/users" className="whitespace-nowrap font-medium text-green-600 hover:text-green-700">
                Assign reviewers
              </Link>
            </>
          )}
        </p>
      )}
      <div className="flex flex-wrap gap-1">
        {items.map((o) => (
          <NotStartedChip key={o.officeId} office={o} href={searchHref(o.officeId)} />
        ))}
      </div>
    </>
  );
}

function NotStartedChip({ office, href }: { office: OfficeSummary; href: string }) {
  const cannotSubmit = office.reviewerName == null;
  return (
    <Link
      href={href}
      title={`${office.officeName} · ${plural(office.assignedProgramCount, "program", "programs")}${
        cannotSubmit ? " · no reviewer — cannot submit" : ""
      } — open in AIP Review`}
      className="inline-flex items-center gap-1.5 border border-slate-200 bg-white px-2 py-1 text-xs font-medium text-slate-800 hover:border-green-600"
    >
      {office.officeCode}
      {office.isHostOffice && <HostBadge />}
      {cannotSubmit && (
        <span className="inline-block h-1.5 w-1.5 rounded-full bg-danger-500" aria-label="No reviewer — cannot submit" />
      )}
    </Link>
  );
}

function OfficeCard({ office, href }: { office: OfficeSummary; href: string }) {
  const cannotSubmit = office.reviewerName == null;
  const hasRisk = office.isOverCeiling || cannotSubmit;

  return (
    <Link
      href={href}
      className="flex min-w-0 flex-col gap-1 border border-slate-200 bg-white px-3 py-2.5 hover:border-green-600"
    >
      <span className="flex flex-wrap items-center gap-1.5">
        <span className="text-sm font-medium text-slate-800">{office.officeCode}</span>
        {office.isHostOffice && <HostBadge />}
        {office.isReturned && (
          <span className="rounded-full bg-amber-100 px-2 py-0.5 text-xs font-medium text-amber-500">
            Returned
          </span>
        )}
      </span>

      <span className="truncate text-xs text-slate-500" title={office.officeName}>
        {office.officeName}
      </span>

      <span className="text-xs text-slate-600 tabular-nums">
        {plural(office.assignedProgramCount, "program", "programs")} ·{" "}
        {plural(office.activityCount, "activity", "activities")}
      </span>

      {/* PPDO-152 — while the office is still with its divisions, how far the division hop has
          got: In progress (Draft), and Returned, since a PPDO return reopens every division
          (decision 11). Counted server-side by the submit gate's own "required" rule; absent for an
          office outside the division flow. */}
      {(office.readinessColumn === "InProgress" || office.isReturned) &&
        office.divisionsRequired != null && office.divisionsRequired > 0 && (
        <span
          className={`w-fit rounded-full px-2 py-0.5 text-xs font-medium tabular-nums ${
            office.divisionsSubmitted === office.divisionsRequired
              ? "bg-green-100 text-green-800"
              : "bg-slate-100 text-slate-600"
          }`}
        >
          {office.divisionsSubmitted ?? 0} of {plural(office.divisionsRequired, "division", "divisions")} submitted
        </span>
      )}

      {/* Null is "not published", which is not the same as ₱0 and must not read as it. */}
      {office.ceilingAmount == null ? (
        <span className="text-xs text-slate-500">No ceiling published</span>
      ) : (
        <span
          className={`text-xs tabular-nums ${
            office.isOverCeiling ? "font-medium text-danger-500" : "text-slate-600"
          }`}
        >
          ₱{formatMoneyShort(office.costedInAip)} of ₱{formatMoneyShort(office.ceilingAmount)}
        </span>
      )}

      {hasRisk && (
        <span className="flex flex-wrap gap-1">
          {office.isOverCeiling && <StatusPill risk="Over ceiling" />}
          {cannotSubmit && <StatusPill risk="Cannot submit" />}
        </span>
      )}

      <span className="truncate border-t border-slate-100 pt-1 text-xs text-slate-500">
        {office.reviewerName ? `Reviewer: ${office.reviewerName}` : "No reviewer — assign"}
      </span>
    </Link>
  );
}

function HostBadge() {
  return (
    <span className="rounded-full bg-green-100 px-1.5 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-green-700">
      Host
    </span>
  );
}

/** Five lane headers over grey cards — the board's own shape, so switching views never jumps. */
export function OfficeBoardSkeleton() {
  return (
    <div className="overflow-x-auto px-5 py-4">
      <div className="grid min-w-[960px] grid-cols-5 items-start gap-3">
        {BOARD_COLUMNS.map((col, i) => (
          <div key={col.key} className="flex flex-col gap-2">
            <span className={HEADING}>{col.title}</span>
            <div className={col.key === "PpdoReview" ? LANE_PPDO : LANE}>
              {i === 0 ? (
                // The wrapped chip run: a no-reviewer line, then a handful of chips.
                <>
                  <div className="h-4 w-3/4 animate-pulse bg-white" />
                  <div className="flex flex-wrap gap-1">
                    {Array.from({ length: 8 }).map((_, r) => (
                      <div key={r} className="h-7 w-14 animate-pulse border border-slate-100 bg-white" />
                    ))}
                  </div>
                </>
              ) : (
                Array.from({ length: 2 }).map((_, r) => (
                  <div key={r} className="h-[118px] animate-pulse border border-slate-100 bg-white" />
                ))
              )}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
