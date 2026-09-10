"use client";

/**
 * CsvImportSummary — the "Import complete" dialog every config page shows after a CSV upsert.
 *
 * Renders the three counts (added / updated / skipped) and, when the import reported any, the
 * list of skipped rows with their reasons. The dialog turns amber when there were errors so a
 * partially-applied import does not read as a clean success.
 *
 * Extracted 2026-09-02 from seven identical copies — accounts, offices, funding-sources,
 * divisions, price-index, cc-typologies and esre-codes each carried this block plus its own
 * private `Stat` helper, byte-for-byte the same. Same cleanup as the `TextAction` helper that
 * `RowActions` replaced (docs/DESIGN_SYSTEM.md §5).
 *
 * Usage — the caller owns the state, this component only renders it:
 *   {importResult && (
 *     <CsvImportSummary result={importResult} onClose={() => setImportResult(null)} />
 *   )}
 *
 * Pair with `CsvUploadButton` / `CsvDownloadButton` for the triggers.
 */

import MessageDialog from "./MessageDialog";
import type { CsvImportResult } from "@/types";

export interface CsvImportSummaryProps {
  /** The counts returned by the page's `import…Csv()` call. */
  result: CsvImportResult;
  onClose: () => void;
}

export default function CsvImportSummary({ result, onClose }: CsvImportSummaryProps) {
  const hasErrors = result.errors.length > 0;

  return (
    <MessageDialog
      title="Import complete"
      variant={hasErrors ? "warning" : "success"}
      size="md"
      onClose={onClose}
    >
      <div className="space-y-3">
        <div className="flex gap-4">
          <Stat label="Added" value={result.new} tone="green" />
          <Stat label="Updated" value={result.updated} tone="blue" />
          <Stat label="Skipped" value={result.skipped} tone="slate" />
        </div>
        {hasErrors && (
          <div>
            <p className="text-xs font-semibold text-amber-500 uppercase tracking-wide mb-1">
              {result.errors.length} row{result.errors.length === 1 ? "" : "s"} skipped
            </p>
            <ul className="max-h-40 overflow-y-auto text-xs text-slate-600 list-disc pl-4 space-y-0.5">
              {result.errors.map((e, i) => (
                <li key={i}>{e}</li>
              ))}
            </ul>
          </div>
        )}
      </div>
    </MessageDialog>
  );
}

/** One of the three counts. Flat by design — portal surfaces carry no rounding. */
function Stat({
  label,
  value,
  tone,
}: {
  label: string;
  value: number;
  tone: "green" | "blue" | "slate";
}) {
  const cls: Record<typeof tone, string> = {
    green: "text-green-700",
    blue: "text-info-500",
    slate: "text-slate-600",
  };
  return (
    <div className="flex-1 border border-slate-200 px-3 py-2 text-center">
      <div className={`text-2xl font-bold ${cls[tone]}`}>{value}</div>
      <div className="text-[11px] text-slate-600 uppercase tracking-wide">{label}</div>
    </div>
  );
}
