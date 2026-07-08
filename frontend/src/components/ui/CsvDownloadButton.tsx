"use client";

/**
 * CsvDownloadButton — exports the current config table as a CSV file (v1.1 shared).
 *
 * Self-contained: calls `fetchCsv()` (which must go through the authed Axios
 * instance so the JWT is attached), wraps the returned text in a Blob, and
 * triggers a browser download. Shows a spinner while fetching and reports
 * failures via `onError`. Flat design — no rounded corners.
 *
 * The downloaded file is always prefixed with a `yyyyMMdd_HHmmss_` local
 * timestamp (see docs/v1.1/UI_Component_Standards.md §3.4) so repeated
 * exports of the same config page don't collide/overwrite in a Downloads
 * folder and sort chronologically. Every config page's `filename` prop is
 * just the base name — do not add your own timestamp/date to it.
 *
 * Usage:
 *   <CsvDownloadButton
 *     filename="accounts.csv"           // downloads as 20260708_143022_accounts.csv
 *     fetchCsv={exportAccountsCsv}
 *     onError={(msg) => toast.error("Export failed", msg)}
 *   />
 */

import { useState } from "react";

export interface CsvDownloadButtonProps {
  fetchCsv: () => Promise<string>;
  /** Base file name, e.g. "accounts.csv" — a timestamp prefix is added automatically. */
  filename: string;
  label?: string;
  disabled?: boolean;
  onError?: (message: string) => void;
}

/** `yyyyMMdd_HHmmss` in local time — 24-hour clock so filenames sort chronologically. */
function timestampPrefix(): string {
  const d = new Date();
  const pad = (n: number) => String(n).padStart(2, "0");
  return (
    `${d.getFullYear()}${pad(d.getMonth() + 1)}${pad(d.getDate())}_` +
    `${pad(d.getHours())}${pad(d.getMinutes())}${pad(d.getSeconds())}`
  );
}

export default function CsvDownloadButton({
  fetchCsv,
  filename,
  label = "Export CSV",
  disabled,
  onError,
}: CsvDownloadButtonProps) {
  const [busy, setBusy] = useState(false);

  async function handleClick() {
    if (busy) return;
    setBusy(true);
    try {
      const csv = await fetchCsv();
      const blob = new Blob([csv], { type: "text/csv;charset=utf-8;" });
      const url = URL.createObjectURL(blob);
      const a = document.createElement("a");
      a.href = url;
      a.download = `${timestampPrefix()}_${filename}`;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch {
      onError?.("Could not export the CSV file. Please try again.");
    } finally {
      setBusy(false);
    }
  }

  return (
    <button
      type="button"
      onClick={handleClick}
      disabled={disabled || busy}
      className="flex items-center gap-1.5 px-3 py-2.5 text-sm font-medium border border-slate-200 text-slate-600 bg-white hover:bg-slate-50 transition-colors disabled:opacity-60 disabled:cursor-not-allowed shrink-0"
    >
      {busy ? (
        <span className="w-4 h-4 border-2 border-slate-400 border-t-transparent rounded-full animate-spin" />
      ) : (
        <span className="text-base leading-none">↓</span>
      )}
      {label}
    </button>
  );
}
