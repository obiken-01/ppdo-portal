/**
 * Layout-preserving loading placeholder for a hand-built <table> (row actions, custom cells,
 * etc. that don't fit the shared DataTable component). Renders the REAL header labels
 * immediately plus N pulsing placeholder rows sized to the eventual row height, so swapping
 * loading -> loaded content doesn't shift the page (CLS) the way a centered spinner in an
 * otherwise-empty card does. Mirrors DataTable.tsx's own `loading` branch — use that
 * component directly when a page doesn't need custom cell rendering; use this one when it does.
 */
export default function TableSkeleton({
  columns,
  rowCount = 5,
}: {
  /** Real column header labels, in order — shown immediately so the header never shifts. */
  columns: string[];
  rowCount?: number;
}) {
  return (
    <table className="w-full text-sm border-collapse">
      <thead>
        <tr className="bg-slate-50 text-xs text-slate-600 uppercase tracking-wide border-b border-slate-100">
          {columns.map((label, i) => (
            <th key={i} className="px-4 py-2.5 text-left font-semibold select-none">
              {label}
            </th>
          ))}
        </tr>
      </thead>
      <tbody>
        {Array.from({ length: rowCount }).map((_, rowIdx) => (
          <tr key={rowIdx} className="border-b border-slate-50 last:border-0">
            {columns.map((_, colIdx) => (
              <td key={colIdx} className="px-4 py-2.5">
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
  );
}
