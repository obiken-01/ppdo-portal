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
 *      there; as cards they would turn the board into one tall column.
 *   4. **Money is abbreviated** (`₱1.12M of ₱10M`) — the table keeps exact pesos.
 *   5. **Every office opens the AIP Review search filtered to it**, Not Started ones included: their
 *      LDIP programs are already in the AIP, so the search has rows to show.
 */

const COLUMNS: { key: ReadinessColumn; title: string }[] = [
  { key: "NotStarted", title: "Not started" },
  { key: "InProgress", title: "In progress" },
  { key: "OfficeReview", title: "Office review" },
  { key: "PpdoReview", title: "PPDO review" },
  { key: "Done", title: "Done" },
];

const HEADING = "text-xs font-semibold uppercase tracking-wide text-slate-600";
const COUNT = "rounded-full bg-slate-100 px-2 text-xs font-medium leading-[18px] text-slate-600 tabular-nums";
const LANE = "flex min-h-[96px] flex-col gap-1.5 border border-slate-100 bg-slate-50 p-2";

function plural(n: number, one: string, many: string): string {
  return `${n.toLocaleString("en-PH")} ${n === 1 ? one : many}`;
}

export default function OfficeBoard({
  offices,
  fiscalYear,
}: {
  offices: OfficeSummary[];
  fiscalYear: number | null;
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
        {COLUMNS.map((col) => {
          const items = offices.filter((o) => o.readinessColumn === col.key);
          const compact = col.key === "NotStarted";
          return (
            <div key={col.key} className="flex min-w-0 flex-col gap-2">
              <div className="flex items-center justify-between gap-2">
                <span className={HEADING}>{col.title}</span>
                <span className={COUNT}>{items.length}</span>
              </div>

              <div className={LANE}>
                {items.length === 0 ? (
                  // The lane stays, so the board keeps its shape.
                  <p className="my-auto text-center text-xs text-slate-500">No offices</p>
                ) : compact ? (
                  <>
                    {items.map((o) => (
                      <NotStartedRow key={o.officeId} office={o} href={searchHref(o.officeId)} />
                    ))}
                    {items.some((o) => o.reviewerName == null) && (
                      <p className="mt-0.5 flex items-center gap-1.5 text-[11px] leading-4 text-slate-600">
                        <span className="inline-block h-1.5 w-1.5 rounded-full bg-danger-500" aria-hidden />
                        No reviewer — cannot submit
                      </p>
                    )}
                  </>
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

function NotStartedRow({ office, href }: { office: OfficeSummary; href: string }) {
  const cannotSubmit = office.reviewerName == null;
  return (
    <Link
      href={href}
      title={`${office.officeName} — open in AIP Review`}
      className="flex items-center justify-between gap-1.5 border border-slate-200 bg-white px-2 py-1 hover:border-green-600"
    >
      <span className="flex min-w-0 items-center gap-1.5">
        <span className="truncate text-xs font-medium text-slate-800">{office.officeCode}</span>
        {office.isHostOffice && <HostBadge />}
      </span>
      <span className="flex shrink-0 items-center gap-1.5 whitespace-nowrap text-[11px] leading-4 text-slate-600 tabular-nums">
        {office.assignedProgramCount.toLocaleString("en-PH")} prog.
        {/* Always rendered, so the program counts line up whether or not the dot shows. */}
        <span
          className={`inline-block h-1.5 w-1.5 rounded-full ${cannotSubmit ? "bg-danger-500" : ""}`}
          aria-label={cannotSubmit ? "No reviewer — cannot submit" : undefined}
        />
      </span>
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
        {COLUMNS.map((col, i) => (
          <div key={col.key} className="flex flex-col gap-2">
            <span className={HEADING}>{col.title}</span>
            <div className={LANE}>
              {Array.from({ length: i === 0 ? 4 : 2 }).map((_, r) => (
                <div
                  key={r}
                  className={`${i === 0 ? "h-7" : "h-[118px]"} animate-pulse border border-slate-100 bg-white`}
                />
              ))}
            </div>
          </div>
        ))}
      </div>
    </div>
  );
}
