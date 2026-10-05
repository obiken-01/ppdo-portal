/**
 * Placeholder for the lazy-loaded DashboardCalendar (PPDO-188 / O14): the same card shell with the
 * legend row and a grid area sized like a six-week month (581 px card, measured), so neither the Resource Links panel nor
 * the page below moves when FullCalendar arrives.
 */
export default function DashboardCalendarSkeleton() {
  return (
    <div className="relative bg-white border border-slate-200 shadow-sm" aria-hidden="true">
      <div className="flex items-center justify-between gap-4 px-4 pt-3 pb-1" style={{ height: 32 }} />
      <div className="px-3 pb-3">
        <div className="animate-pulse bg-slate-50" style={{ height: 535 }} />
      </div>
    </div>
  );
}
