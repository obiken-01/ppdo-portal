"use client";

/**
 * DataTable — configurable, sortable, optionally-paginated table (v1.1 shared).
 *
 * Shared across all config pages (Accounts, Offices, Funding Sources) and any
 * future list view. Configure with a column-definition array. Flat design — no
 * rounded corners, zebra rows, green hover.
 *
 * Scope note: DataTable handles render + client-side sort + pagination + the
 * loading / error / empty states. FILTERING (search box, type / status filters)
 * is the consumer's responsibility — config pages own a dedicated filter bar and
 * pass already-filtered `rows` in. This keeps server-side filters (e.g. the
 * accountType prefix filter) and client-only sort cleanly separated.
 *
 * Usage:
 *   const columns: Column<AccountResponse>[] = [
 *     { key: "accountNumber", header: "Account Number", sortable: true },
 *     { key: "accountTitle",  header: "Account Title",  sortable: true,
 *       render: (r) => <span className="font-medium">{r.accountTitle}</span> },
 *     { key: "actions", header: "Actions", align: "right",
 *       render: (r) => <RowActions row={r} /> },
 *   ];
 *
 *   <DataTable
 *     columns={columns}
 *     rows={filtered}
 *     rowKey={(r) => r.id}
 *     loading={loading}
 *     error={error}
 *     onRetry={reload}
 *     emptyMessage="No accounts match your filters."
 *     pageSize={25}
 *   />
 */

import { useEffect, useMemo, useState } from "react";

export type Align = "left" | "center" | "right";

export interface Column<T> {
  /** Unique key. For data columns, also the default sort field via `sortValue`. */
  key: string;
  header: string;
  align?: Align;
  sortable?: boolean;
  /** Cell content. Defaults to String((row as any)[key]). */
  render?: (row: T) => React.ReactNode;
  /** Value used for sorting when `sortable`. Defaults to render-less cell value. */
  sortValue?: (row: T) => string | number;
  /** Extra classes on the <td>. */
  className?: string;
}

export interface DataTableProps<T> {
  columns: Column<T>[];
  rows: T[];
  rowKey: (row: T) => string | number;
  loading?: boolean;
  error?: string | null;
  onRetry?: () => void;
  emptyMessage?: string;
  /** When set, paginate client-side at this page size. Omit to show all rows. */
  pageSize?: number;
  /** Singular/plural noun for the row-count footer (default "row"/"rows"). */
  rowNoun?: [singular: string, plural: string];
}

const ALIGN_CLASS: Record<Align, string> = {
  left: "text-left",
  center: "text-center",
  right: "text-right",
};

export default function DataTable<T>({
  columns,
  rows,
  rowKey,
  loading,
  error,
  onRetry,
  emptyMessage = "No records found.",
  pageSize,
  rowNoun = ["row", "rows"],
}: DataTableProps<T>) {
  const [sortKey, setSortKey] = useState<string | null>(null);
  const [sortDir, setSortDir] = useState<"asc" | "desc">("asc");
  const [page, setPage] = useState(0);

  // Reset to first page whenever the data set or sort changes.
  useEffect(() => {
    setPage(0);
  }, [rows, sortKey, sortDir]);

  function defaultSortValue(row: T, col: Column<T>): string | number {
    if (col.sortValue) return col.sortValue(row);
    const raw = (row as Record<string, unknown>)[col.key];
    if (typeof raw === "number") return raw;
    return raw == null ? "" : String(raw);
  }

  const sortedRows = useMemo(() => {
    if (!sortKey) return rows;
    const col = columns.find((c) => c.key === sortKey);
    if (!col) return rows;
    const copy = [...rows];
    copy.sort((a, b) => {
      const av = defaultSortValue(a, col);
      const bv = defaultSortValue(b, col);
      let cmp: number;
      if (typeof av === "number" && typeof bv === "number") cmp = av - bv;
      else cmp = String(av).localeCompare(String(bv), undefined, { numeric: true });
      return sortDir === "asc" ? cmp : -cmp;
    });
    return copy;
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [rows, columns, sortKey, sortDir]);

  const pageCount = pageSize ? Math.max(1, Math.ceil(sortedRows.length / pageSize)) : 1;
  const safePage = Math.min(page, pageCount - 1);
  const pagedRows = pageSize ? sortedRows.slice(safePage * pageSize, safePage * pageSize + pageSize) : sortedRows;

  function toggleSort(col: Column<T>) {
    if (!col.sortable) return;
    if (sortKey === col.key) {
      setSortDir((d) => (d === "asc" ? "desc" : "asc"));
    } else {
      setSortKey(col.key);
      setSortDir("asc");
    }
  }

  // ── States ────────────────────────────────────────────────────────────────

  if (loading) {
    const skeletonCount = Math.min(pageSize ?? 8, 10);
    return (
      <div className="bg-white border border-slate-200 overflow-hidden">
        <table className="w-full text-sm border-collapse">
          <thead>
            <tr className="bg-slate-50 border-b border-slate-200">
              {columns.map((col) => (
                <th
                  key={col.key}
                  className={`px-4 py-3 text-xs font-semibold text-slate-400 uppercase tracking-wide ${ALIGN_CLASS[col.align ?? "left"]}`}
                >
                  {col.header}
                </th>
              ))}
            </tr>
          </thead>
          <tbody>
            {Array.from({ length: skeletonCount }).map((_, rowIdx) => (
              <tr key={rowIdx} className="border-b border-slate-100">
                {columns.map((col, colIdx) => (
                  <td key={col.key} className={`px-4 py-3 ${ALIGN_CLASS[col.align ?? "left"]}`}>
                    <div
                      className="h-4 bg-slate-100 animate-pulse inline-block"
                      style={{ width: `${50 + ((rowIdx * 3 + colIdx * 17) % 35)}%` }}
                    />
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>
    );
  }

  if (error) {
    return (
      <div className="bg-white border border-slate-200 flex flex-col items-center justify-center py-16 gap-3">
        <p className="text-sm text-danger-500">{error}</p>
        {onRetry && (
          <button onClick={onRetry} className="text-sm text-green-600 hover:underline">
            Retry
          </button>
        )}
      </div>
    );
  }

  if (sortedRows.length === 0) {
    return (
      <div className="bg-white border border-slate-200 flex flex-col items-center justify-center py-16 gap-2 text-slate-400">
        <span className="text-3xl">📭</span>
        <p className="text-sm">{emptyMessage}</p>
      </div>
    );
  }

  // ── Table ─────────────────────────────────────────────────────────────────

  return (
    <div className="bg-white border border-slate-200 overflow-hidden">
      <div className="overflow-x-auto">
        <table className="w-full text-sm">
          <thead>
            <tr className="bg-slate-50 border-b border-slate-200 text-xs text-slate-500 uppercase tracking-wide">
              {columns.map((col) => {
                const active = sortKey === col.key;
                return (
                  <th
                    key={col.key}
                    className={`px-4 py-3 font-medium ${ALIGN_CLASS[col.align ?? "left"]} ${
                      col.sortable ? "cursor-pointer select-none hover:text-slate-700" : ""
                    }`}
                    onClick={() => toggleSort(col)}
                  >
                    <span className="inline-flex items-center gap-1">
                      {col.header}
                      {col.sortable && (
                        <span className={`text-[10px] ${active ? "text-green-600" : "text-slate-300"}`}>
                          {active ? (sortDir === "asc" ? "▲" : "▼") : "↕"}
                        </span>
                      )}
                    </span>
                  </th>
                );
              })}
            </tr>
          </thead>
          <tbody className="divide-y divide-slate-100">
            {pagedRows.map((row, i) => (
              <tr
                key={rowKey(row)}
                className={`transition-colors hover:bg-green-50 ${i % 2 === 1 ? "bg-slate-50" : "bg-white"}`}
              >
                {columns.map((col) => (
                  <td
                    key={col.key}
                    className={`px-4 py-3 text-slate-600 ${ALIGN_CLASS[col.align ?? "left"]} ${col.className ?? ""}`}
                  >
                    {col.render
                      ? col.render(row)
                      : String((row as Record<string, unknown>)[col.key] ?? "")}
                  </td>
                ))}
              </tr>
            ))}
          </tbody>
        </table>
      </div>

      {/* Footer: row count + pagination */}
      <div className="flex items-center justify-between px-4 py-2 border-t border-slate-100 text-xs text-slate-400">
        <span>
          {sortedRows.length} {sortedRows.length === 1 ? rowNoun[0] : rowNoun[1]}
        </span>

        {pageSize && pageCount > 1 && (
          <div className="flex items-center gap-2">
            <button
              onClick={() => setPage((p) => Math.max(0, p - 1))}
              disabled={safePage === 0}
              className="px-2 py-1 border border-slate-200 text-slate-500 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed"
            >
              ‹ Prev
            </button>
            <span className="text-slate-500">
              Page {safePage + 1} of {pageCount}
            </span>
            <button
              onClick={() => setPage((p) => Math.min(pageCount - 1, p + 1))}
              disabled={safePage >= pageCount - 1}
              className="px-2 py-1 border border-slate-200 text-slate-500 hover:bg-slate-50 disabled:opacity-40 disabled:cursor-not-allowed"
            >
              Next ›
            </button>
          </div>
        )}
      </div>
    </div>
  );
}
