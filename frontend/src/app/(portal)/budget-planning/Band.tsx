"use client";

/**
 * Band — one section of the dashboard, with its own loading, error and empty states (PPDO-20).
 *
 * **Errors are per band, not per page.** The dashboard composes four independent fetches; if the
 * office table 500s, the rail and the tiles are still correct and still worth reading. A single
 * page-level error state would throw all of that away to report one failure, which is what the
 * page did before. Each band therefore carries its own `Retry`, refetching only itself.
 *
 * `TableBandSkeleton` renders a header plus grey rows at the real row height — never a centred
 * spinner that is later replaced by a full-height table, which is a measurable layout shift
 * (`docs/PERFORMANCE_GUIDELINES.md` §6, the CLS regression fixed in RAL-192).
 */

export default function Band({
  title,
  description,
  actions,
  loading,
  error,
  onRetry,
  skeleton,
  children,
}: {
  title: string;
  description?: string;
  actions?: React.ReactNode;
  loading?: boolean;
  error?: string | null;
  onRetry?: () => void;
  /** Shown while loading. Must match the loaded structure — see the doc comment. */
  skeleton?: React.ReactNode;
  children: React.ReactNode;
}) {
  return (
    <section className="bg-white border border-slate-200">
      <div className="px-5 py-4 border-b border-slate-100 flex flex-col gap-2 sm:flex-row sm:items-center sm:justify-between">
        <div className="min-w-0">
          <h2 className="text-sm font-semibold text-slate-800">{title}</h2>
          {description && <p className="text-xs text-slate-600 mt-0.5">{description}</p>}
        </div>
        {actions && <div className="flex flex-wrap items-center gap-2 sm:shrink-0">{actions}</div>}
      </div>

      {loading ? (
        (skeleton ?? <TableBandSkeleton />)
      ) : error ? (
        <div className="px-5 py-6 flex flex-wrap items-center gap-3">
          <p className="text-sm text-danger-500">{error}</p>
          {onRetry && (
            <button
              type="button"
              onClick={onRetry}
              className="px-3 py-1.5 bg-white border border-slate-200 hover:bg-slate-50 text-slate-800 text-xs font-medium transition-colors"
            >
              Retry
            </button>
          )}
        </div>
      ) : (
        children
      )}
    </section>
  );
}

/** Header row plus `rows` grey rows at the real 41px row height. */
export function TableBandSkeleton({ rows = 5, columns = 5 }: { rows?: number; columns?: number }) {
  return (
    <div className="px-5 py-3">
      <div className="flex gap-4 pb-2 border-b border-slate-100">
        {Array.from({ length: columns }).map((_, i) => (
          <div key={i} className="h-3 flex-1 bg-slate-100 animate-pulse" />
        ))}
      </div>
      {Array.from({ length: rows }).map((_, r) => (
        <div key={r} className="flex gap-4 items-center h-[41px] border-b border-slate-50">
          {Array.from({ length: columns }).map((_, c) => (
            <div key={c} className="h-3 flex-1 bg-slate-100 animate-pulse" />
          ))}
        </div>
      ))}
    </div>
  );
}

/** The band's "nothing recorded yet" state. Never blank, and always says what to do next. */
export function BandEmpty({ message, action }: { message: string; action?: React.ReactNode }) {
  return (
    <div className="px-5 py-8 flex flex-col items-start gap-3">
      <p className="text-sm text-slate-600">{message}</p>
      {action}
    </div>
  );
}
