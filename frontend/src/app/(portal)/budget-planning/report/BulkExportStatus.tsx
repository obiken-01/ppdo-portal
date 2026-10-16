"use client";

/**
 * Status strip for the bulk initial WFP export (PPDO-230, spec §6) — sits under the selector row.
 * Renders nothing when the year has no job and nothing went wrong; while the first GET is in
 * flight it holds the strip's height with a skeleton so the page below does not jump.
 */

import type { BulkExportState } from "./useBulkExport";
import type { InitialWfpExportJobDto } from "@/types";

const BUTTON =
  "px-3 py-1 text-sm font-medium border border-slate-300 text-slate-800 bg-white hover:bg-slate-50 disabled:opacity-50 disabled:cursor-not-allowed";
const PRIMARY =
  "px-3 py-1 text-sm font-medium bg-green-600 text-white hover:bg-green-500 disabled:opacity-50 disabled:cursor-not-allowed";

function formatManila(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleString("en-PH", {
    timeZone: "Asia/Manila", year: "numeric", month: "short", day: "numeric", hour: "numeric", minute: "2-digit",
  });
}

function formatManilaDate(iso: string | null | undefined): string {
  if (!iso) return "—";
  return new Date(iso).toLocaleDateString("en-PH", {
    timeZone: "Asia/Manila", year: "numeric", month: "short", day: "numeric",
  });
}

/** Done, but its expiry has passed before the server's cleanup has marked it — treated as Expired too. */
function effectiveStatus(job: InitialWfpExportJobDto): InitialWfpExportJobDto["status"] {
  const finished = job.status === "Done" || job.status === "DoneWithProblems";
  if (finished && job.expiresAt && new Date(job.expiresAt).getTime() < Date.now()) return "Expired";
  return job.status;
}

export default function BulkExportStatus({
  state, fiscalYear,
}: {
  state: BulkExportState; fiscalYear: number;
}) {
  const { job, loading, requesting, downloading, error, request, download, dismissError } = state;

  if (loading) {
    return <div className="mb-4 h-14 bg-slate-100 animate-pulse print:hidden" aria-hidden="true" />;
  }
  if (job == null && error == null) return null;

  const status = job ? effectiveStatus(job) : null;

  return (
    <div className="mb-4 space-y-2 print:hidden" aria-live="polite">
      {error && (
        <div className="flex flex-wrap items-center justify-between gap-3 border border-red-200 bg-red-50 px-4 py-3 text-sm text-red-700">
          <span>{error}</span>
          <button type="button" onClick={dismissError} className={BUTTON}>Dismiss</button>
        </div>
      )}

      {job && status && (
        <div className="border border-slate-200 bg-white px-4 py-3 text-sm text-slate-600">
          <p className="mb-1 text-xs font-medium uppercase tracking-wide text-slate-600">
            Download all — FY {fiscalYear} initial WFP
          </p>

          {status === "Queued" && <p>Queued…</p>}

          {status === "Running" && (
            <div>
              <p className="mb-1.5">Building {job.done} of {job.total} offices…</p>
              <div
                className="h-2 w-full max-w-md bg-slate-200"
                role="progressbar"
                aria-valuemin={0}
                aria-valuemax={job.total}
                aria-valuenow={job.done}
              >
                <div
                  className="h-2 bg-green-600 transition-[width]"
                  style={{ width: `${job.total > 0 ? Math.min(100, (job.done / job.total) * 100) : 0}%` }}
                />
              </div>
            </div>
          )}

          {(status === "Done" || status === "DoneWithProblems") && (
            <div className="space-y-2">
              <div className="flex flex-wrap items-center gap-3">
                <button type="button" onClick={() => void download()} disabled={downloading} className={PRIMARY}>
                  {downloading ? "Downloading…" : "Download ZIP"}
                </button>
                <span>
                  Built {formatManila(job.finishedAt)}, expires {formatManilaDate(job.expiresAt)}
                </span>
              </div>
              {status === "DoneWithProblems" && job.problems.length > 0 && (
                <div className="border border-amber-200 bg-amber-50 px-3 py-2 text-amber-800">
                  <p className="font-medium">
                    {job.problems.length} {job.problems.length === 1 ? "office" : "offices"} skipped
                  </p>
                  <ul className="mt-1 list-disc pl-5">
                    {job.problems.map((p) => (
                      <li key={p.officeCode}><span className="font-mono">{p.officeCode}</span> — {p.reason}</li>
                    ))}
                  </ul>
                </div>
              )}
            </div>
          )}

          {status === "Failed" && (
            <div className="flex flex-wrap items-center gap-3">
              <span className="text-red-700">{job.message || "The export failed."}</span>
              <button type="button" onClick={() => void request()} disabled={requesting} className={BUTTON}>
                {requesting ? "Starting…" : "Try again"}
              </button>
            </div>
          )}

          {status === "Expired" && (
            <div className="flex flex-wrap items-center gap-3">
              <span>Expired — run it again</span>
              <button type="button" onClick={() => void request()} disabled={requesting} className={BUTTON}>
                {requesting ? "Starting…" : "Download all"}
              </button>
            </div>
          )}
        </div>
      )}
    </div>
  );
}
